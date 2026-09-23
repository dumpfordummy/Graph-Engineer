using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GraphEngineering.Api.Persistence;
using GraphEngineering.Api.Runs;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace GraphEngineering.Api.Tests;

public sealed class RunExecutionTests
{
    [Fact]
    public async Task LiteralRunPersistsFullTextArtifactsOrderedEventsAndMetadataWithoutKeys()
    {
        await using var fixture = await RunTestFixture.Start();
        var output = new string('z', 900) + " <script>alert('data')</script>";
        fixture.Provider.Handler = context => context.Response.WriteAsJsonAsync(ProviderFixture.Completed(output));
        var saved = await fixture.Save(fixture.Graph());
        var admitted = await fixture.Submit(saved);
        var run = await fixture.Terminal(admitted.Id);
        Assert.Equal("Succeeded", run.State); Assert.Equal(output, run.Result!.Value.GetString()); Assert.Equal(1, fixture.Provider.Count);
        Assert.Equal("Literal {braces} {{inputs.unbound}} remains literal.", fixture.Provider.Requests.Single().Body["input"]!.GetValue<string>());
        Assert.Equal(["input", "max_output_tokens", "model", "store", "stream"], fixture.Provider.Requests.Single().Body.Select(x => x.Key).Order().ToArray());
        var attempt = Assert.Single(run.Nodes, x => x.Type == "modelCall").Attempt!;
        Assert.Equal("ResponseReceived", attempt.ExternalOutcome); Assert.Equal(output, attempt.OutputText); Assert.Equal(7, attempt.Usage!.InputTokens);
        var events = (await fixture.Client.GetFromJsonAsync<JsonObject>($"/api/runs/{run.Id}/events"))!["items"]!.AsArray();
        Assert.Equal(Enumerable.Range(1, events.Count).Select(x => (long)x), events.Select(x => x!["sequence"]!.GetValue<long>()));
        Assert.Equal(run.LastSequence, events.Count);
        foreach (var node in run.Nodes)
            foreach (var artifactId in node.ArtifactIds)
            {
                var artifact = (await fixture.Client.GetFromJsonAsync<RunArtifact>($"/api/runs/{run.Id}/artifacts/{artifactId}"))!;
                Assert.Equal(Encoding.UTF8.GetByteCount(artifact.Text), artifact.ByteCount);
                Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(artifact.Text))), artifact.Sha256);
                Assert.DoesNotContain(RunTestFixture.Key, artifact.Text);
            }
        var serialized = JsonSerializer.Serialize(run); Assert.DoesNotContain(RunTestFixture.Key, serialized); Assert.DoesNotContain("Ciphertext", serialized);
        Assert.Equal(HttpStatusCode.OK, (await fixture.Client.GetAsync($"/api/runs?workflowId={run.WorkflowId}&limit=1")).StatusCode);
    }

    [Fact]
    public async Task TwoNodesUseIndependentProfilesActualUpstreamValueAndControlOrder()
    {
        await using var fixture = await RunTestFixture.Start();
        await using var other = await ProviderFixture.Start();
        var second = await ProviderApiTests.Create(fixture.Client, ProviderApiTests.Write(other.BaseUrl, "SYNTHETIC_M3_SECOND_KEY"));
        fixture.Provider.Handler = context => context.Response.WriteAsJsonAsync(ProviderFixture.Completed("{\"varX\":19,\"ignored\":\"never forwarded\"}"));
        other.Handler = context => context.Response.WriteAsJsonAsync(ProviderFixture.Completed("{\"varY\":190}"));
        var graph = fixture.Graph(true, true, second["id"]!.GetValue<string>());
        var nodes = graph["definition"]!["nodes"]!.AsArray(); var first = nodes[0]!.DeepClone(); nodes.RemoveAt(0); nodes.Add(first);
        var run = await fixture.Terminal((await fixture.Submit(await fixture.Save(graph))).Id);
        Assert.Equal("Succeeded", run.State); Assert.Equal(190, run.Result!.Value.GetProperty("varY").GetInt32());
        Assert.Equal(1, fixture.Provider.Count); Assert.Equal(1, other.Count);
        Assert.Equal("Double 7.", fixture.Provider.Requests.Single().Body["input"]!.GetValue<string>());
        Assert.Equal("Multiply 19 by ten.", other.Requests.Single().Body["input"]!.GetValue<string>());
        Assert.Equal("Bearer SYNTHETIC_M3_SECOND_KEY", other.Requests.Single().Authorization);
        Assert.Equal(["start", "model-1", "model-2", "end"], run.Nodes.Select(x => x.NodeId).ToArray());
        Assert.Equal(19, run.Nodes[2].Attempt!.ResolvedInputs["varX"].GetInt32());
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("```json\n{\"varX\":14}\n```")]
    [InlineData("{\"varX\":14,\"varX\":15}")]
    [InlineData("[]")]
    [InlineData("{} trailing")]
    public async Task InvalidJsonOutputStopsDownstreamWithoutRepair(string text)
    {
        await using var fixture = await RunTestFixture.Start();
        fixture.Provider.Handler = context => context.Response.WriteAsJsonAsync(ProviderFixture.Completed(text));
        var run = await fixture.Terminal((await fixture.Submit(await fixture.Save(fixture.Graph(true, true)))).Id);
        Assert.Equal("Failed", run.State); Assert.Equal(1, fixture.Provider.Count);
        Assert.Equal("Failed", run.Nodes[1].State); Assert.Equal("Skipped", run.Nodes[2].State); Assert.Null(run.Nodes[1].Attempt!.OutputText);
    }

    [Theory]
    [InlineData("incomplete")]
    [InlineData("refusal")]
    [InlineData("tool")]
    [InlineData("large_text")]
    [InlineData("large_body")]
    [InlineData("malformed")]
    public async Task UnsupportedAndOversizedProviderPayloadsDoNotProgress(string variant)
    {
        await using var fixture = await RunTestFixture.Start();
        fixture.Provider.Handler = async context =>
        {
            var payload = ProviderFixture.Completed();
            if (variant == "incomplete") payload["status"] = "incomplete";
            if (variant == "refusal") payload["output"]![1]!["content"] = new JsonArray(new JsonObject { ["type"] = "refusal", ["refusal"] = "synthetic" });
            if (variant == "tool") payload["output"] = new JsonArray(new JsonObject { ["type"] = "function_call", ["name"] = "never_execute" });
            if (variant == "large_text") payload = ProviderFixture.Completed(new string('x', 128 * 1024 + 1));
            if (variant == "large_body") payload["padding"] = new string('x', 256 * 1024);
            if (variant == "malformed") { context.Response.ContentType = "application/json"; await context.Response.WriteAsync("{malformed"); }
            else await context.Response.WriteAsJsonAsync(payload);
        };
        var run = await fixture.Terminal((await fixture.Submit(await fixture.Save(fixture.Graph(true)))).Id);
        Assert.Equal("Failed", run.State); Assert.Equal(1, fixture.Provider.Count); Assert.Equal("Skipped", run.Nodes[2].State);
    }

    [Fact]
    public async Task MissingBoundOutputFieldFailsBeforeSecondDispatch()
    {
        await using var fixture = await RunTestFixture.Start();
        fixture.Provider.Handler = context => context.Response.WriteAsJsonAsync(ProviderFixture.Completed("{\"other\":4}"));
        var run = await fixture.Terminal((await fixture.Submit(await fixture.Save(fixture.Graph(true, true)))).Id);
        Assert.Equal("Failed", run.State); Assert.Equal(1, fixture.Provider.Count); Assert.Equal("Succeeded", run.Nodes[1].State);
        Assert.Equal("NotStarted", run.Nodes[2].Attempt!.ExternalOutcome);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActiveSecretEchoFailsWithoutPersistingOrForwarding(bool controlSeparated)
    {
        await using var fixture = await RunTestFixture.Start();
        var echo = controlSeparated ? string.Join('\u0001', RunTestFixture.Key.ToCharArray()) : RunTestFixture.Key;
        fixture.Provider.Handler = context => context.Response.WriteAsJsonAsync(ProviderFixture.Completed(echo));
        var run = await fixture.Terminal((await fixture.Submit(await fixture.Save(fixture.Graph(true)))).Id);
        Assert.Equal("sensitive_output", run.FailureCode); Assert.Equal(1, fixture.Provider.Count);
        Assert.DoesNotContain(RunTestFixture.Key, JsonSerializer.Serialize(run)); Assert.Null(run.Nodes[1].Attempt!.OutputText);
        await fixture.WithDb(async db => Assert.DoesNotContain(RunTestFixture.Key, string.Join('\n', await db.RunArtifacts.Select(x => x.Text).ToListAsync())));
    }

    [Fact]
    public async Task ConcurrentIdempotencyLostResponseAndBusyAdmissionNeverDuplicateDispatch()
    {
        await using var fixture = await RunTestFixture.Start();
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Provider.Handler = async context => { observed.TrySetResult(); await release.Task.WaitAsync(context.RequestAborted); await context.Response.WriteAsJsonAsync(ProviderFixture.Completed()); };
        var saved = await fixture.Save(fixture.Graph()); var submission = Guid.NewGuid();
        var responses = await Task.WhenAll(fixture.SubmitResponse(saved, submission: submission), fixture.SubmitResponse(saved, submission: submission));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Accepted, response.StatusCode));
        var first = (await responses[0].Content.ReadFromJsonAsync<RunDetail>())!;
        Assert.Equal(first.Id, (await responses[1].Content.ReadFromJsonAsync<RunDetail>())!.Id);
        await observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(HttpStatusCode.Conflict, (await fixture.SubmitResponse(saved)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await fixture.SubmitResponse(saved, new { x = 8 }, submission)).StatusCode);
        Assert.Equal(first.Id, (await fixture.Client.GetFromJsonAsync<RunDetail>($"/api/runs/submissions/{submission}"))!.Id);
        await fixture.App.PairClientAsync(fixture.Client);
        Assert.Equal(first.Id, (await fixture.Client.GetFromJsonAsync<RunDetail>($"/api/runs/submissions/{submission}"))!.Id);
        release.SetResult(); Assert.Equal("Succeeded", (await fixture.Terminal(first.Id)).State);
        Assert.Equal(first.Id, (await fixture.Submit(saved, submission: submission)).Id); Assert.Equal(1, fixture.Provider.Count);
    }

    [Fact]
    public async Task QueueCancellationHasNoCallsAndRepeatedCancellationIsStable()
    {
        await using var fixture = await RunTestFixture.Start(services =>
        {
            var worker = services.Single(x => x.ServiceType == typeof(IHostedService) && x.ImplementationType == typeof(RunWorker)); services.Remove(worker);
        });
        var run = await fixture.Submit(await fixture.Save(fixture.Graph())); Assert.Equal("Queued", run.State);
        var first = (await (await fixture.Client.PostAsJsonAsync($"/api/runs/{run.Id}/cancel", new { })).Content.ReadFromJsonAsync<RunDetail>())!;
        var second = (await (await fixture.Client.PostAsJsonAsync($"/api/runs/{run.Id}/cancel", new { })).Content.ReadFromJsonAsync<RunDetail>())!;
        Assert.Equal("Cancelled", first.State); Assert.Equal(first.LastSequence, second.LastSequence); Assert.Equal(0, fixture.Provider.Count);
        Assert.All(first.Nodes, node => Assert.Equal("Skipped", node.State));
    }

    [Fact]
    public async Task ActiveCancellationAndCompletionRaceNeverResurrectOrDispatchDownstream()
    {
        await using var fixture = await RunTestFixture.Start();
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Provider.Handler = async context => { observed.TrySetResult(); await release.Task; try { await context.Response.WriteAsJsonAsync(ProviderFixture.Completed()); } catch (IOException) { } };
        var run = await fixture.Submit(await fixture.Save(fixture.Graph(true))); await observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var inFlight = await fixture.Client.GetFromJsonAsync<RunDetail>($"/api/runs/{run.Id}");
        Assert.Equal("Unknown", inFlight!.Nodes[1].Attempt!.ExternalOutcome);
        var cancel = await fixture.Client.PostAsJsonAsync($"/api/runs/{run.Id}/cancel", new { }); Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        release.SetResult(); var terminal = await fixture.Terminal(run.Id);
        Assert.Equal("Cancelled", terminal.State); Assert.Equal("Cancelled", terminal.Nodes[1].State); Assert.Equal("Skipped", terminal.Nodes[2].State);
        Assert.Contains(terminal.Nodes[1].Attempt!.ExternalOutcome, new[] { "Unknown", "ResponseReceived" }); Assert.Equal(1, fixture.Provider.Count);
        var repeated = await (await fixture.Client.PostAsJsonAsync($"/api/runs/{run.Id}/cancel", new { })).Content.ReadFromJsonAsync<RunDetail>();
        Assert.Equal(terminal.LastSequence, repeated!.LastSequence);
    }

    [Fact]
    public async Task ProviderTimeoutHasUnknownExternalOutcomeAndNoRetry()
    {
        await using var fixture = await RunTestFixture.Start();
        fixture.Provider.Handler = context => Task.Delay(Timeout.Infinite, context.RequestAborted);
        var terminal = await fixture.Terminal((await fixture.Submit(await fixture.Save(fixture.Graph(true)))).Id);
        Assert.Equal("timeout", terminal.FailureCode); Assert.Equal("Unknown", terminal.Nodes[1].Attempt!.ExternalOutcome); Assert.Equal(1, fixture.Provider.Count);
    }
}
