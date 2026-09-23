using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GraphEngineering.Api.Tests;

internal sealed class ProviderFixture : IAsyncDisposable
{
    private readonly WebApplication app;
    private readonly X509Certificate2? certificate;
    private int count;
    public string BaseUrl { get; private set; } = "";
    public int Count => Volatile.Read(ref count);
    public List<(string Path, string? Authorization, JsonObject Body)> Requests { get; } = [];
    public Func<HttpContext, Task>? Handler { get; set; }

    private ProviderFixture(WebApplication application, X509Certificate2? cert)
    {
        app = application; certificate = cert;
        app.Run(async context =>
        {
            Interlocked.Increment(ref count);
            var body = await context.Request.ReadFromJsonAsync<JsonObject>();
            lock (Requests) Requests.Add((context.Request.Path, context.Request.Headers.Authorization, body!));
            if (Handler is not null) await Handler(context);
            else await context.Response.WriteAsJsonAsync(Completed());
        });
    }

    public static async Task<ProviderFixture> Start(bool tls = false)
    {
        X509Certificate2? cert = null;
        if (tls)
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var names = new SubjectAlternativeNameBuilder(); names.AddIpAddress(IPAddress.Loopback);
            request.CertificateExtensions.Add(names.Build());
            cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        }
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ResponseHeaderEncodingSelector = name => name.Equals("x-request-id", StringComparison.OrdinalIgnoreCase) ? Encoding.Latin1 : null;
            options.Listen(IPAddress.Loopback, 0, listen => { if (cert is not null) listen.UseHttps(cert); });
        });
        var fixture = new ProviderFixture(builder.Build(), cert);
        await fixture.app.StartAsync();
        fixture.BaseUrl = fixture.app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        return fixture;
    }

    public static JsonObject Completed(string text = "GE_CONNECTION_OK") => new()
    {
        ["object"] = "response", ["status"] = "completed", ["model"] = "fixture-model",
        ["output"] = new JsonArray(
            new JsonObject { ["type"] = "reasoning", ["summary"] = new JsonArray() },
            new JsonObject { ["type"] = "message", ["role"] = "assistant", ["status"] = "completed", ["content"] = new JsonArray(new JsonObject { ["type"] = "output_text", ["text"] = text }) }),
        ["usage"] = new JsonObject { ["input_tokens"] = 7, ["output_tokens"] = 3, ["total_tokens"] = 10 }
    };

    public async ValueTask DisposeAsync() { await app.StopAsync(); await app.DisposeAsync(); certificate?.Dispose(); }
}

internal sealed class ProviderTestData
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "GraphEngineering-M2-tests", Guid.NewGuid().ToString("N"));
}
