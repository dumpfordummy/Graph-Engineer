using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using GraphEngineering.Api.Persistence;
using GraphEngineering.Api.Runs;
using GraphEngineering.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GraphEngineering.Api.Tests;

internal sealed class RunTestFixture : IAsyncDisposable
{
    public const string Key = "SYNTHETIC_M3_ACTIVE_KEY_NOT_REAL_7e32";
    public required ProviderFixture Provider { get; init; }
    public required LocalApiFactory App { get; init; }
    public required HttpClient Client { get; init; }
    public required JsonObject Profile { get; init; }
    public string ProfileId => Profile["id"]!.GetValue<string>();
    public static async Task<RunTestFixture> Start(Action<IServiceCollection>? configure = null)
    {
        var fixture = await ProviderFixture.Start();
        var app = new LocalApiFactory(Path.Combine(Path.GetTempPath(), "GraphEngineering-M3-tests", Guid.NewGuid().ToString("N")), configure);
        var client = await app.CreatePairedClientAsync();
        var profile = await ProviderApiTests.Create(client, ProviderApiTests.Write(fixture.BaseUrl, Key));
        return new() { Provider = fixture, App = app, Client = client, Profile = profile };
    }
    public JsonObject Graph(bool twoNodes = false, bool json = false, string? secondProfile = null)
    {
        var graph = DocumentFixture.Create();
        if (!twoNodes) { DocumentFixture.RemoveNode(graph, "model-2"); DocumentFixture.AddEdge(graph, "final", "model-1", "end"); }
        foreach (var node in DocumentFixture.Nodes(graph))
        {
            if (node!["type"]!.GetValue<string>() == "modelCall")
            {
                node["typeVersion"] = 2;
                var second = node["id"]!.GetValue<string>() == "model-2";
                node["configuration"] = new JsonObject
                {
                    ["providerProfileId"] = second ? secondProfile ?? ProfileId : ProfileId,
                    ["prompt"] = json ? second ? "Multiply {{inputs.varX}} by ten." : "Double {{inputs.x}}." : "Literal {braces} {{inputs.unbound}} remains literal.",
                    ["promptMode"] = json ? "bindings" : "literal", ["outputMode"] = json ? "jsonObject" : "text",
                    ["inputBindings"] = json ? new JsonArray(new JsonObject
                    {
                        ["alias"] = second ? "varX" : "x",
                        ["source"] = second ? new JsonObject { ["kind"] = "nodeJson", ["nodeId"] = "model-1", ["pointer"] = "/varX" } : new JsonObject { ["kind"] = "runInput", ["pointer"] = "/x" }
                    }) : new JsonArray()
                };
            }
            if (node["type"]!.GetValue<string>() == "end")
            {
                node["typeVersion"] = 2; node["configuration"] = new JsonObject { ["resultReference"] = "Explicit result", ["resultBinding"] =
                    json ? new JsonObject { ["kind"] = "nodeJson", ["nodeId"] = twoNodes ? "model-2" : "model-1", ["pointer"] = "" } : new JsonObject { ["kind"] = "nodeText", ["nodeId"] = twoNodes ? "model-2" : "model-1" } };
            }
        }
        return graph;
    }
    public async Task<JsonObject> Save(JsonObject graph)
    {
        var response = await Client.PostAsJsonAsync("/api/workflows", new { document = graph });
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!;
    }
    public async Task<RunDetail> Submit(JsonObject saved, object? input = null, Guid? submission = null)
    {
        var response = await SubmitResponse(saved, input, submission);
        Assert.True(response.StatusCode == HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<RunDetail>())!;
    }
    public Task<HttpResponseMessage> SubmitResponse(JsonObject saved, object? input = null, Guid? submission = null) => Client.PostAsJsonAsync(
        $"/api/workflows/{saved["workflow"]!["id"]!.GetValue<string>()}/runs",
        new { submissionId = submission ?? Guid.NewGuid(), expectedRevision = saved["workflow"]!["revision"]!.GetValue<long>(), input = input ?? new { x = 7, unrelated = "must-not-forward" } });
    public async Task<RunDetail> Terminal(Guid id)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (true)
        {
            var detail = (await Client.GetFromJsonAsync<RunDetail>($"/api/runs/{id}", timeout.Token))!;
            if (detail.State is "Succeeded" or "Failed" or "Cancelled" or "Interrupted") return detail;
            await Task.Delay(25, timeout.Token);
        }
    }
    public async Task WithDb(Func<WorkflowDbContext, Task> action)
    { await using var scope = App.Services.CreateAsyncScope(); await action(scope.ServiceProvider.GetRequiredService<WorkflowDbContext>()); }
    public async ValueTask DisposeAsync() { Client.Dispose(); await App.DisposeAsync(); await Provider.DisposeAsync(); }
}
