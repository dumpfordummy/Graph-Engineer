using System.Net;
using System.Net.Http.Json;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using GraphEngineering.Api.Security;
using GraphEngineering.Tests;
using Microsoft.Data.Sqlite;
using Xunit;

namespace GraphEngineering.Api.Tests;

public sealed class LocalSecurityTests
{
    [Theory]
    [InlineData("/api/workflows")]
    [InlineData("/api/providers")]
    public async Task AnonymousPrivateReadsAreRejected(string route)
    {
        using var data = new SecurityData();
        await using var app = new LocalApiFactory(data.Path);
        using var client = app.CreateLocalClient();
        using var response = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("session_required", (await response.Content.ReadFromJsonAsync<JsonObject>())!["code"]!.GetValue<string>());
        var status = await client.GetFromJsonAsync<JsonObject>("/api/session");
        Assert.False(status!["authenticated"]!.GetValue<bool>());
        Assert.Null(status["csrfToken"]);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/health")).StatusCode);
    }

    [Theory]
    [InlineData("http://evil.example")]
    [InlineData("null")]
    [InlineData("http://127.0.0.1:5999")]
    [InlineData("http://localhost:5173")]
    public async Task ExactOriginRequiredForPairingAndAuthenticatedReads(string origin)
    {
        using var data = new SecurityData();
        await using var app = new LocalApiFactory(data.Path);
        using var client = await app.CreatePairedClientAsync();
        client.DefaultRequestHeaders.Remove("Origin");
        client.DefaultRequestHeaders.Add("Origin", origin);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/workflows")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/session/pair", new { token = "not-a-token" })).StatusCode);
    }

    [Theory]
    [InlineData("attacker.example:5080")]
    [InlineData("localhost:5080")]
    [InlineData("127.0.0.1:5999")]
    public async Task UnknownHostIsRejectedEvenWithForwardedHeaders(string host)
    {
        using var data = new SecurityData();
        await using var app = new LocalApiFactory(data.Path);
        using var client = await app.CreatePairedClientAsync();
        client.DefaultRequestHeaders.Host = host;
        client.DefaultRequestHeaders.Add("X-Forwarded-Host", "127.0.0.1:5080");
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "http");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/workflows")).StatusCode);
    }

    [Fact]
    public async Task JsonOriginSessionAndAntiforgeryAreAllRequiredForWrites()
    {
        using var data = new SecurityData();
        await using var app = new LocalApiFactory(data.Path);
        using var client = await app.CreatePairedClientAsync();
        var document = DocumentFixture.Create();
        using var okay = await client.PostAsJsonAsync("/api/workflows", new { document });
        Assert.Equal(HttpStatusCode.Created, okay.StatusCode);
        client.DefaultRequestHeaders.Remove("X-GE-CSRF");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/workflows", new { document })).StatusCode);
        await app.PairClientAsync(client);
        using var form = new StringContent("credential=synthetic", Encoding.UTF8, "application/x-www-form-urlencoded");
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await client.PostAsync("/api/workflows", form)).StatusCode);
        using var plain = new StringContent("{}", Encoding.UTF8, "text/plain");
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await client.PostAsync("/api/workflows", plain)).StatusCode);
        client.DefaultRequestHeaders.Remove("Origin");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/workflows", new { document })).StatusCode);
        Assert.Single((await client.GetFromJsonAsync<JsonArray>("/api/workflows"))!);
    }

    [Fact]
    public async Task AntiforgeryCannotBeBorrowedFromAnotherPairedBrowser()
    {
        using var data = new SecurityData();
        await using var app = new LocalApiFactory(data.Path);
        using var first = await app.CreatePairedClientAsync();
        using var second = await app.CreatePairedClientAsync();
        first.DefaultRequestHeaders.Remove("X-GE-CSRF");
        first.DefaultRequestHeaders.Add("X-GE-CSRF", second.DefaultRequestHeaders.GetValues("X-GE-CSRF"));
        Assert.Equal(HttpStatusCode.Forbidden, (await first.PostAsJsonAsync("/api/session/logout", new { })).StatusCode);
    }

    [Fact]
    public async Task PairingInputIsBoundedThrottledAndNeverEchoed()
    {
        using var data = new SecurityData();
        await using var app = new LocalApiFactory(data.Path);
        using var client = app.CreateLocalClient();
        client.DefaultRequestHeaders.Add("Origin", "http://127.0.0.1:5173");
        using var tooLarge = await client.PostAsJsonAsync("/api/session/pair", new { token = new string('x', 2048) });
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooLarge.StatusCode);
        const string sentinel = "SYNTHETIC-PAIRING-REJECTED-DO-NOT-ECHO";
        for (var i = 0; i < 9; i++)
        {
            using var rejected = await client.PostAsJsonAsync("/api/session/pair", new { token = sentinel });
            Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
            Assert.DoesNotContain(sentinel, await rejected.Content.ReadAsStringAsync());
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync("/api/session/pair", new { token = sentinel })).StatusCode);
    }

    [Fact]
    public async Task CookiesAreRestrictedAndLogoutRevokesThisBrowser()
    {
        using var data = new SecurityData();
        await using var app = new LocalApiFactory(data.Path);
        using var client = app.CreateLocalClient();
        client.DefaultRequestHeaders.Add("Origin", "http://127.0.0.1:5173");
        var token = await File.ReadAllTextAsync(Path.Combine(data.Path, "runtime", "pairing-token.txt"));
        using var paired = await client.PostAsJsonAsync("/api/session/pair", new { token });
        var cookie = paired.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("GE.LocalSession="));
        Assert.Contains("httponly", cookie.ToLowerInvariant());
        Assert.Contains("samesite=strict", cookie.ToLowerInvariant());
        Assert.DoesNotContain("expires=", cookie.ToLowerInvariant());
        Assert.DoesNotContain("secure", cookie.ToLowerInvariant()); // Explicit loopback HTTP development exception.
        var status = await client.GetFromJsonAsync<JsonObject>("/api/session");
        client.DefaultRequestHeaders.Add("X-GE-CSRF", status!["csrfToken"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/session/logout", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/workflows")).StatusCode);
    }

    [Fact]
    public async Task RestartRotatesPairingAndInvalidatesPreviouslyIssuedCookie()
    {
        using var data = new SecurityData();
        string oldToken;
        string oldCookie;
        await using (var first = new LocalApiFactory(data.Path))
        {
            using var client = first.CreateLocalClient();
            client.DefaultRequestHeaders.Add("Origin", "http://127.0.0.1:5173");
            oldToken = await File.ReadAllTextAsync(Path.Combine(data.Path, "runtime", "pairing-token.txt"));
            using var pair = await client.PostAsJsonAsync("/api/session/pair", new { token = oldToken });
            oldCookie = pair.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("GE.LocalSession=")).Split(';')[0];
        }
        SqliteConnection.ClearAllPools();
        await using var second = new LocalApiFactory(data.Path);
        using var stale = second.CreateLocalClient();
        stale.DefaultRequestHeaders.Add("Origin", "http://127.0.0.1:5173");
        stale.DefaultRequestHeaders.Add("Cookie", oldCookie);
        Assert.Equal(HttpStatusCode.Unauthorized, (await stale.GetAsync("/api/workflows")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await stale.PostAsJsonAsync("/api/session/pair", new { token = oldToken })).StatusCode);
        using var current = await second.CreatePairedClientAsync();
        Assert.Equal(HttpStatusCode.OK, (await current.GetAsync("/api/workflows")).StatusCode);
    }

    [Fact]
    public async Task RuntimeBootstrapIsOwnerRestrictedAndNotAnHttpResource()
    {
        Assert.True(OperatingSystem.IsWindows(), "Windows acceptance requires a real Windows runner.");
        if (!OperatingSystem.IsWindows()) return;
        using var data = new SecurityData();
        await using var app = new LocalApiFactory(data.Path);
        using var client = await app.CreatePairedClientAsync();
        var runtime = new DirectoryInfo(Path.Combine(data.Path, "runtime"));
        var security = runtime.GetAccessControl();
        Assert.True(security.AreAccessRulesProtected);
        var owner = WindowsIdentity.GetCurrent().User!;
        foreach (var rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>())
        {
            Assert.Equal(owner, rule.IdentityReference);
            Assert.Equal(AccessControlType.Allow, rule.AccessControlType);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/runtime/pairing-token.txt")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/session/token")).StatusCode);
        var status = await client.GetAsync("/api/session");
        Assert.True(status.Headers.CacheControl!.NoStore);
    }

    [Fact]
    public async Task PreexistingPairingFileLosesExplicitBroadReadGrantsBeforeRotation()
    {
        Assert.True(OperatingSystem.IsWindows(), "Windows acceptance requires a real Windows runner.");
        if (!OperatingSystem.IsWindows()) return;
        using var data = new SecurityData();
        var runtime = Directory.CreateDirectory(Path.Combine(data.Path, "runtime"));
        var tokenFile = new FileInfo(Path.Combine(runtime.FullName, "pairing-token.txt"));
        await File.WriteAllTextAsync(tokenFile.FullName, "synthetic-old-bootstrap");
        var permissive = tokenFile.GetAccessControl();
        permissive.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            FileSystemRights.ReadData, AccessControlType.Allow));
        tokenFile.SetAccessControl(permissive);
        await using var app = new LocalApiFactory(data.Path);
        using var paired = await app.CreatePairedClientAsync();
        var restricted = tokenFile.GetAccessControl();
        Assert.True(restricted.AreAccessRulesProtected);
        var owner = WindowsIdentity.GetCurrent().User!;
        foreach (var rule in restricted.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>())
            Assert.Equal(owner, rule.IdentityReference);
        Assert.Equal(HttpStatusCode.OK, (await paired.GetAsync("/api/workflows")).StatusCode);
    }

    [Fact]
    public void KestrelListenerOverridesAreRejectedBeforeServerStartup()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["urls"] = "http://127.0.0.1:5080",
            ["Kestrel:Endpoints:Untrusted:Url"] = "http://0.0.0.0:5299"
        }).Build();
        var error = Assert.Throws<InvalidOperationException>(() => new LocalLaunch(configuration));
        Assert.Contains("endpoint overrides are not supported", error.Message);
    }

    [Fact]
    public async Task HttpsPairingIssuesSecureCookie()
    {
        using var data = new SecurityData();
        await using var app = new LocalApiFactory(data.Path);
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://127.0.0.1:5080") });
        client.DefaultRequestHeaders.Add("Origin", "http://127.0.0.1:5173");
        var token = await File.ReadAllTextAsync(Path.Combine(data.Path, "runtime", "pairing-token.txt"));
        using var pair = await client.PostAsJsonAsync("/api/session/pair", new { token });
        var cookie = pair.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("GE.LocalSession="));
        Assert.Contains("; secure", cookie.ToLowerInvariant());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/workflows")).StatusCode);
    }

    [Fact]
    public async Task AbsoluteCookieLifetimeExpiresWithoutSlidingExtension()
    {
        using var data = new SecurityData();
        var clock = new SessionClock();
        await using var app = new LocalApiFactory(data.Path, services =>
            services.AddOptions<CookieAuthenticationOptions>("LocalBrowser").Configure(options => options.TimeProvider = clock));
        using var client = await app.CreatePairedClientAsync();
        clock.Advance(TimeSpan.FromHours(7));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/workflows")).StatusCode);
        clock.Advance(TimeSpan.FromHours(2));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/workflows")).StatusCode);
    }

    private sealed class SessionClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan amount) => now += amount;
    }

    private sealed class SecurityData : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "GraphEngineering.Security.Tests-" + Guid.NewGuid().ToString("N"));
        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
