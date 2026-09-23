using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using GraphEngineering.Api.Notifications;
using GraphEngineering.Api.Runs;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GraphEngineering.Api.Tests;

public sealed class HubSecurityTests
{
    [Fact]
    public async Task NegotiateIsTheOnlyEmptyPostExceptionAndStillRequiresSessionOriginAndCsrf()
    {
        using var data = new IsolatedData();
        await using var app = new LocalApiFactory(data.Path);
        using var paired = await app.CreatePairedClientAsync();
        Assert.Equal(HttpStatusCode.OK, (await paired.PostAsync("/hubs/runs/negotiate?negotiateVersion=1", null)).StatusCode);
        paired.DefaultRequestHeaders.Remove("X-GE-CSRF");
        Assert.Equal(HttpStatusCode.Forbidden, (await paired.PostAsync("/hubs/runs/negotiate?negotiateVersion=1", null)).StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await paired.PostAsync("/api/workflows", null)).StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await paired.PostAsync("/hubs/runs", null)).StatusCode);
        using var anonymous = app.CreateLocalClient();
        anonymous.DefaultRequestHeaders.Add("Origin", "http://127.0.0.1:5173");
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("/hubs/runs/negotiate", null)).StatusCode);
    }

    [Theory]
    [InlineData(null, true, null, "")]
    [InlineData("http://evil.example", true, null, "")]
    [InlineData("null", true, null, "")]
    [InlineData("http://127.0.0.1:5173", false, null, "")]
    [InlineData("http://127.0.0.1:5173", true, "evil.example:5080", "")]
    [InlineData("http://127.0.0.1:5173", true, null, "?access_token=SYNTHETIC_NOT_ALLOWED")]
    public async Task ActualWebSocketRejectsInvalidOriginSessionHostAndQueryToken(string? origin, bool authenticated, string? host, string query)
    {
        using var data = new IsolatedData();
        await using var app = new LocalApiFactory(data.Path);
        var cookie = await PairCookie(app, data.Path);
        var sockets = app.Server.CreateWebSocketClient();
        sockets.ConfigureRequest = request =>
        {
            if (origin is not null) request.Headers["Origin"] = origin;
            if (authenticated) request.Headers["Cookie"] = cookie;
            if (host is not null) request.Headers.Host = host;
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => sockets.ConnectAsync(new Uri("ws://127.0.0.1:5080/hubs/runs" + query), CancellationToken.None));
    }

    [Fact]
    public async Task ActualHubReceivesOnlySmallNoticesHasNoMutationsAndClosesWhenSessionExpires()
    {
        using var data = new IsolatedData();
        await using var app = new LocalApiFactory(data.Path, services => services
            .AddOptions<CookieAuthenticationOptions>("LocalBrowser").Configure(options => options.ExpireTimeSpan = TimeSpan.FromSeconds(4)));
        var cookie = await PairCookie(app, data.Path);
        var sockets = app.Server.CreateWebSocketClient();
        sockets.ConfigureRequest = request =>
        {
            request.Headers["Origin"] = "http://127.0.0.1:5173";
            request.Headers["Cookie"] = cookie;
        };
        using var socket = await sockets.ConnectAsync(new Uri("ws://127.0.0.1:5080/hubs/runs"), CancellationToken.None);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        await socket.SendAsync(Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}\u001e"), WebSocketMessageType.Text, true, timeout.Token);
        var buffer = new byte[2048];
        var handshake = await socket.ReceiveAsync(buffer, timeout.Token);
        Assert.Equal("{}\u001e", Encoding.UTF8.GetString(buffer, 0, handshake.Count));
        var id = Guid.NewGuid();
        await app.Services.GetRequiredService<IRunNotifier>().PublishAsync(id, 7, timeout.Token);
        var notice = await socket.ReceiveAsync(buffer, timeout.Token);
        var raw = Encoding.UTF8.GetString(buffer, 0, notice.Count).TrimEnd('\u001e');
        using (var json = JsonDocument.Parse(raw))
        {
            Assert.Equal("RunChanged", json.RootElement.GetProperty("target").GetString());
            var value = json.RootElement.GetProperty("arguments")[0];
            Assert.Equal(2, value.EnumerateObject().Count());
            Assert.Equal(id, value.GetProperty("runId").GetGuid());
            Assert.Equal(7, value.GetProperty("eventSequence").GetInt64());
        }
        Assert.Empty(typeof(RunHub).GetMethods(System.Reflection.BindingFlags.DeclaredOnly | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance));
        // SignalR sends a close frame when authentication expires, without a client logout.
        WebSocketReceiveResult next;
        do { next = await socket.ReceiveAsync(buffer, timeout.Token); }
        while (next.MessageType != WebSocketMessageType.Close);
        Assert.False(timeout.IsCancellationRequested);
        using var expired = app.CreateLocalClient();
        expired.DefaultRequestHeaders.Add("Cookie", cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, (await expired.GetAsync("/api/runs")).StatusCode);
    }

    private static async Task<string> PairCookie(LocalApiFactory app, string directory)
    {
        using var client = app.CreateLocalClient();
        client.DefaultRequestHeaders.Add("Origin", "http://127.0.0.1:5173");
        var token = await File.ReadAllTextAsync(System.IO.Path.Combine(directory, "runtime", "pairing-token.txt"));
        using var response = await client.PostAsJsonAsync("/api/session/pair", new { token });
        response.EnsureSuccessStatusCode();
        return response.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("GE.LocalSession=", StringComparison.Ordinal)).Split(';')[0];
    }

    private sealed class IsolatedData : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "GE.M3.Hub-" + Guid.NewGuid().ToString("N"));
        public void Dispose()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(Path)) Directory.Delete(Path, true);
        }
    }
}
