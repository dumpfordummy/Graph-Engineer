using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GraphEngineering.Api.Tests;

public class LocalApiFactory(string directory, Action<IServiceCollection>? configureServices = null) : WebApplicationFactory<Program>
{
    public string DataDirectory => directory;
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GRAPH_ENGINEERING_DATA_DIR"] = directory,
            ["GRAPH_ENGINEERING_BROWSER_ORIGIN"] = "http://127.0.0.1:5173",
            ["urls"] = "http://127.0.0.1:5080",
            ["Logging:LogLevel:Default"] = "Warning"
        }));
        if (configureServices is not null) builder.ConfigureServices(configureServices);
    }

    public HttpClient CreateLocalClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("http://127.0.0.1:5080"), AllowAutoRedirect = false
    });

    public async Task<HttpClient> CreatePairedClientAsync()
    {
        var client = CreateLocalClient();
        client.DefaultRequestHeaders.Add("Origin", "http://127.0.0.1:5173");
        await PairClientAsync(client);
        return client;
    }

    public async Task PairClientAsync(HttpClient client)
    {
        var token = await File.ReadAllTextAsync(Path.Combine(directory, "runtime", "pairing-token.txt"));
        using var paired = await client.PostAsJsonAsync("/api/session/pair", new { token });
        if (!paired.IsSuccessStatusCode) throw new InvalidOperationException($"Isolated test pairing failed: {(int)paired.StatusCode}");
        var status = await client.GetFromJsonAsync<JsonObject>("/api/session");
        client.DefaultRequestHeaders.Remove("X-GE-CSRF");
        client.DefaultRequestHeaders.Add("X-GE-CSRF", status!["csrfToken"]!.GetValue<string>());
    }
}
