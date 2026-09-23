using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GraphEngineering.Api.Persistence;
using GraphEngineering.Api.Providers;
using GraphEngineering.Api.Runs;
using GraphEngineering.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace GraphEngineering.Api.Tests;

public sealed class RunDurabilityTests
{
    [Fact]
    public async Task FrozenSnapshotSurvivesGraphEditsAndChangedConnectionStopsTheNextNode()
    {
        await using var fixture = await RunTestFixture.Start();
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Provider.Handler = async context => { observed.TrySetResult(); await release.Task.WaitAsync(context.RequestAborted); await context.Response.WriteAsJsonAsync(ProviderFixture.Completed("first retained")); };
        var saved = await fixture.Save(fixture.Graph(true)); var run = await fixture.Submit(saved);
        await observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var document = saved.DeepClone().AsObject(); document["layout"]!["nodes"]![1]!["x"] = 999;
        foreach (var node in DocumentFixture.Nodes(document).Where(x => x!["type"]!.GetValue<string>() == "modelCall"))
        { node!["configuration"]!["prompt"] = "New edited prompt"; node["configuration"]!["providerProfileId"] = null; }
        Assert.Equal(HttpStatusCode.OK, (await fixture.Client.PutAsJsonAsync($"/api/workflows/{run.WorkflowId}", new { expectedRevision = 1, document })).StatusCode);
        var delete = new HttpRequestMessage(HttpMethod.Delete, $"/api/providers/{fixture.ProfileId}") { Content = JsonContent.Create(new { expectedRevision = 1 }) };
        Assert.Equal(HttpStatusCode.Conflict, (await fixture.Client.SendAsync(delete)).StatusCode);
        var change = ProviderApiTests.Write(fixture.Provider.BaseUrl, null); change["modelId"] = "new-model"; change["expectedRevision"] = 1;
        Assert.Equal(HttpStatusCode.OK, (await fixture.Client.PutAsJsonAsync($"/api/providers/{fixture.ProfileId}", change)).StatusCode);
        release.SetResult(); var finished = await fixture.Terminal(run.Id);
        Assert.Equal("Failed", finished.State); Assert.Equal("configuration_changed", finished.FailureCode); Assert.Equal(1, fixture.Provider.Count);
        Assert.Equal("Succeeded", finished.Nodes[1].State); Assert.Equal("first retained", finished.Nodes[1].Attempt!.OutputText);
        Assert.Equal("NotStarted", finished.Nodes[2].Attempt!.ExternalOutcome);
        Assert.Equal(400, finished.Snapshot.Layout.Nodes.Single(x => x.NodeId == "model-1").X);
        Assert.Equal("Literal {braces} {{inputs.unbound}} remains literal.", finished.Snapshot.Definition.Nodes.Single(x => x.Id == "model-2").Configuration.GetProperty("prompt").GetString());
        Assert.All(finished.Profiles, x => Assert.Equal("user/exact-model", x.ModelId));
        Assert.All(finished.Profiles, x => Assert.Equal("openai-responses", x.Protocol));
        var completedDelete = new HttpRequestMessage(HttpMethod.Delete, $"/api/providers/{fixture.ProfileId}") { Content = JsonContent.Create(new { expectedRevision = 2 }) };
        Assert.Equal(HttpStatusCode.NoContent, (await fixture.Client.SendAsync(completedDelete)).StatusCode);
        var historical = (await fixture.Client.GetFromJsonAsync<RunDetail>($"/api/runs/{run.Id}"))!;
        Assert.Equal(finished.LastSequence, historical.LastSequence); Assert.Equal("first retained", historical.Nodes[1].Attempt!.OutputText);
        var replay = await fixture.Submit(saved, submission: run.SubmissionId); Assert.Equal(run.Id, replay.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SqliteFailureBeforeDispatchOrAfterResponseNeverRetriesOrAdvances(bool afterResponse)
    {
        await using var fixture = await RunTestFixture.Start();
        var saved = await fixture.Save(fixture.Graph(true));
        await fixture.WithDb(db => db.Database.ExecuteSqlRawAsync(afterResponse ? """
            CREATE TRIGGER SyntheticFailOutput BEFORE UPDATE ON Runs
            WHEN json_extract(NEW.NodesJson, '$[1].state') = 'Succeeded'
            BEGIN SELECT RAISE(ABORT, 'synthetic_output_commit_failure'); END;
            """ : """
            CREATE TRIGGER SyntheticFailIntent BEFORE UPDATE ON Runs
            WHEN json_extract(NEW.NodesJson, '$[1].attempt.dispatchIntentAt') IS NOT NULL
            BEGIN SELECT RAISE(ABORT, 'synthetic_dispatch_commit_failure'); END;
            """));
        var admitted = await fixture.Submit(saved); var run = await fixture.Terminal(admitted.Id);
        Assert.Equal("Interrupted", run.State); Assert.Equal(afterResponse ? 1 : 0, fixture.Provider.Count);
        Assert.Equal("Skipped", run.Nodes[2].State); Assert.Null(run.Nodes[1].Attempt!.OutputText);
        Assert.Equal(afterResponse ? "Unknown" : "NotStarted", run.Nodes[1].Attempt!.ExternalOutcome);
        await fixture.WithDb(async db => Assert.DoesNotContain(await db.RunArtifacts.ToListAsync(), x => x.Kind == "output_text"));
        var replay = await fixture.Submit(saved, submission: run.SubmissionId); Assert.Equal(run.Id, replay.Id); Assert.Equal(afterResponse ? 1 : 0, fixture.Provider.Count);
    }

    [Fact]
    public async Task EventsAreCommittedBeforePublicationAndNotifierFailureDoesNotUndoSuccess()
    {
        var notices = new CommittedNoticeRecorder();
        await using var fixture = await RunTestFixture.Start(services => services.Replace(ServiceDescriptor.Singleton<IRunNotifier>(notices)));
        notices.Services = fixture.App.Services;
        var run = await fixture.Terminal((await fixture.Submit(await fixture.Save(fixture.Graph()))).Id);
        Assert.Equal("Succeeded", run.State); Assert.Equal(1, fixture.Provider.Count);
        Assert.True(notices.Count > 0); Assert.False(notices.Uncommitted);
        Assert.True(notices.LastObservedSequence <= run.LastSequence);
    }

    [Fact]
    public async Task MissingRunInputBindingIsRejectedBeforeAnyDurableRunOrProviderCall()
    {
        await using var fixture = await RunTestFixture.Start();
        var graph = fixture.Graph(true, true);
        DocumentFixture.Nodes(graph)[2]!["configuration"]!["inputBindings"]![0]!["source"] = new JsonObject { ["kind"] = "runInput", ["pointer"] = "/missing" };
        var response = await fixture.SubmitResponse(await fixture.Save(graph));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode); Assert.Equal(0, fixture.Provider.Count);
        await fixture.WithDb(async db => Assert.Equal(0, await db.Runs.CountAsync()));
    }

    [Theory]
    [InlineData("{\"submissionId\":4,\"expectedRevision\":1,\"input\":{}}")]
    [InlineData("{\"submissionId\":\"11111111-1111-4111-8111-111111111111\",\"expectedRevision\":\"one\",\"input\":{}}")]
    [InlineData("{\"submissionId\":\"11111111-1111-4111-8111-111111111111\",\"expectedRevision\":1,\"input\":{\"x\":1,\"x\":2}}")]
    public async Task MalformedSubmissionTypesAndDuplicatesAreRejected(string body)
    {
        await using var fixture = await RunTestFixture.Start();
        var saved = await fixture.Save(fixture.Graph());
        var response = await fixture.Client.PostAsync($"/api/workflows/{saved["workflow"]!["id"]!.GetValue<string>()}/runs", new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Equal(0, fixture.Provider.Count);
    }

    [Fact]
    public async Task LargeIntegerInputIsPreservedInActualHttpPromptAndFinalJson()
    {
        await using var fixture = await RunTestFixture.Start();
        fixture.Provider.Handler = context => context.Response.WriteAsJsonAsync(ProviderFixture.Completed("{\"exact\":9007199254740993123456789}"));
        var saved = await fixture.Save(fixture.Graph(false, true));
        var body = "{\"submissionId\":\"" + Guid.NewGuid() + "\",\"expectedRevision\":1,\"input\":{\"x\":9007199254740993123456789}}";
        var response = await fixture.Client.PostAsync($"/api/workflows/{saved["workflow"]!["id"]!.GetValue<string>()}/runs", new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var run = await fixture.Terminal((await response.Content.ReadFromJsonAsync<RunDetail>())!.Id);
        Assert.Equal("Succeeded", run.State); Assert.Equal("Double 9007199254740993123456789.", fixture.Provider.Requests.Single().Body["input"]!.GetValue<string>());
        Assert.Equal("9007199254740993123456789", run.Result!.Value.GetProperty("exact").GetRawText());
    }

    [Fact]
    public async Task M2DatabaseUpgradePreservesLegacyWorkflowProfileAndProtectedCredential()
    {
        var path = Path.Combine(Path.GetTempPath(), "GraphEngineering-M3-migration", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path);
        var workflowId = Guid.NewGuid(); var providerId = Guid.NewGuid(); var key = new WindowsSecretStore().Protect(RunTestFixture.Key);
        var legacy = DocumentFixture.Read(DocumentFixture.Create()).Document!;
        await using (var db = new WorkflowDbContext(new DbContextOptionsBuilder<WorkflowDbContext>().UseSqlite($"Data Source={Path.Combine(path, "workflows.db")}").Options))
        {
            await db.GetService<IMigrator>().MigrateAsync("20260923000200_ProviderProfiles");
            db.Workflows.Add(new() { Id = workflowId, Name = "Legacy retained", Description = "M2", Revision = 8, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
                DefinitionJson = JsonSerializer.Serialize(legacy.Definition, GraphEngineering.Core.Documents.DocumentJson.Options), LayoutJson = JsonSerializer.Serialize(legacy.Layout, GraphEngineering.Core.Documents.DocumentJson.Options) });
            db.ProviderProfiles.Add(new() { Id = providerId, Name = "Old profile", Revision = 3, ConnectionVersion = 2, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow,
                BaseUrl = "https://example.invalid/v1", ModelId = "old-model", AuthMode = "bearer", TimeoutSeconds = 5, MaxOutputTokens = 128, Credential = new() { ProviderId = providerId, Ciphertext = key } });
            await db.SaveChangesAsync();
        }
        await using var app = new LocalApiFactory(path); using var client = await app.CreatePairedClientAsync();
        await using var scope = app.Services.CreateAsyncScope(); var upgraded = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        Assert.Equal(3, (await upgraded.Database.GetAppliedMigrationsAsync()).Count()); Assert.False(upgraded.Database.HasPendingModelChanges());
        Assert.Equal(8, (await upgraded.Workflows.SingleAsync()).Revision); Assert.Equal("old-model", (await upgraded.ProviderProfiles.SingleAsync()).ModelId);
        Assert.Equal(key, (await upgraded.ProviderCredentials.SingleAsync()).Ciphertext); Assert.Equal(RunTestFixture.Key, new WindowsSecretStore().Unprotect(key));
        Assert.False((await client.GetFromJsonAsync<RunReadiness>($"/api/workflows/{workflowId}/readiness"))!.Ready); Assert.Equal(0, await upgraded.Runs.CountAsync());
    }

    [Fact]
    public async Task RestartConservativelyInterruptsQueuedWorkWithoutDispatch()
    {
        var fixture = await RunTestFixture.Start(services => services.Remove(services.Single(x => x.ServiceType == typeof(IHostedService) && x.ImplementationType == typeof(RunWorker))));
        var saved = await fixture.Save(fixture.Graph()); var run = await fixture.Submit(saved); var path = fixture.App.DataDirectory;
        await fixture.App.DisposeAsync(); fixture.Client.Dispose();
        await using var restarted = new LocalApiFactory(path); using var client = await restarted.CreatePairedClientAsync();
        var recovered = (await client.GetFromJsonAsync<RunDetail>($"/api/runs/{run.Id}"))!;
        Assert.Equal("Interrupted", recovered.State); Assert.Equal("backend_restarted", recovered.FailureCode); Assert.Equal(0, fixture.Provider.Count);
        Assert.All(recovered.Nodes, node => Assert.Equal("Skipped", node.State));
        await fixture.Provider.DisposeAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EscapedCredentialInParsedOutputKeysOrValuesFailsBeforePersistence(bool propertyName)
    {
        await using var fixture = await RunTestFixture.Start();
        var encoded = "\\u0053" + RunTestFixture.Key[1..];
        var text = propertyName ? "{\"nested\":{\"" + encoded + "\":1}}" : "{\"nested\":[{\"secret\":\"" + encoded + "\"}]}";
        fixture.Provider.Handler = context => context.Response.WriteAsJsonAsync(ProviderFixture.Completed(text));
        var run = await fixture.Terminal((await fixture.Submit(await fixture.Save(fixture.Graph(true, true)))).Id);
        Assert.Equal("sensitive_output", run.FailureCode); Assert.Equal(1, fixture.Provider.Count); Assert.Null(run.Nodes[1].Attempt!.OutputJson);
        Assert.Null(run.Nodes[1].Attempt!.OutputText); Assert.DoesNotContain(RunTestFixture.Key, JsonSerializer.Serialize(run));
        await fixture.WithDb(async db => Assert.DoesNotContain(await db.RunArtifacts.ToListAsync(), artifact => artifact.Kind.StartsWith("output", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task EscapedSelectedCredentialInRunInputIsRejectedBeforeSnapshotPersistence()
    {
        await using var fixture = await RunTestFixture.Start();
        var saved = await fixture.Save(fixture.Graph());
        var input = JsonSerializer.Serialize(new { userData = RunTestFixture.Key }).Replace("SYNTHETIC", "\\u0053YNTHETIC", StringComparison.Ordinal);
        var body = "{\"submissionId\":\"" + Guid.NewGuid() + "\",\"expectedRevision\":1,\"input\":" + input + "}";
        var response = await fixture.Client.PostAsync($"/api/workflows/{saved["workflow"]!["id"]!.GetValue<string>()}/runs", new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode); Assert.Equal(0, fixture.Provider.Count);
        await fixture.WithDb(async db => Assert.Equal(0, await db.Runs.CountAsync()));
    }

    private sealed class CommittedNoticeRecorder : IRunNotifier
    {
        public IServiceProvider? Services { get; set; }
        public int Count { get; private set; }
        public bool Uncommitted { get; private set; }
        public long LastObservedSequence { get; private set; }
        public async Task PublishAsync(Guid runId, long sequence, CancellationToken token)
        {
            await using var scope = Services!.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
            var record = await db.Runs.AsNoTracking().SingleAsync(x => x.Id == runId, token);
            var events = await db.RunEvents.CountAsync(x => x.RunId == runId && x.Sequence <= sequence, token);
            Uncommitted |= record.LastSequence < sequence || events != sequence; LastObservedSequence = sequence; Count++;
            throw new IOException("synthetic notification outage");
        }
    }

    [Fact]
    public async Task HistoryPaginationUsesUtcCursorWithoutLosingOlderRuns()
    {
        await using var fixture = await RunTestFixture.Start(); var saved = await fixture.Save(fixture.Graph());
        var first = await fixture.Terminal((await fixture.Submit(saved)).Id); var second = await fixture.Terminal((await fixture.Submit(saved)).Id);
        var page = (await fixture.Client.GetFromJsonAsync<JsonObject>($"/api/runs?workflowId={first.WorkflowId}&limit=1"))!;
        Assert.Equal(second.Id, page["items"]![0]!["id"]!.GetValue<Guid>());
        var cursor = page["nextCursor"]!.GetValue<string>(); Assert.EndsWith("Z", cursor);
        var next = (await fixture.Client.GetFromJsonAsync<JsonObject>($"/api/runs?workflowId={first.WorkflowId}&limit=1&before={Uri.EscapeDataString(cursor)}"))!;
        Assert.Equal(first.Id, next["items"]![0]!["id"]!.GetValue<Guid>()); Assert.Null(next["nextCursor"]);
    }

    [Fact]
    public async Task StalledNotificationCannotHoldAcceptedRunOrTriggerAnotherProviderCall()
    {
        var notifier = new StallFirstNotice();
        await using var fixture = await RunTestFixture.Start(services => services.Replace(ServiceDescriptor.Singleton<IRunNotifier>(notifier)));
        var pending = fixture.Submit(await fixture.Save(fixture.Graph()));
        var accepted = await pending.WaitAsync(TimeSpan.FromSeconds(8));
        Assert.Equal("Succeeded", (await fixture.Terminal(accepted.Id)).State); Assert.Equal(1, fixture.Provider.Count);
        await notifier.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(3));
        notifier.Release.TrySetResult();
    }
    private sealed class StallFirstNotice : IRunNotifier
    {
        private int count;
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task PublishAsync(Guid id, long sequence, CancellationToken token)
        {
            if (Interlocked.Increment(ref count) != 1) return;
            using var registration = token.Register(() => Cancelled.TrySetResult());
            // This controlled publisher deliberately ignores cancellation; the outer bound must still release the caller.
            await Release.Task;
        }
    }
}
