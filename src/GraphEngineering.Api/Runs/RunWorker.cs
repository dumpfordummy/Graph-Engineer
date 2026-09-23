using System.Text.Json;
using GraphEngineering.Api.Persistence;
using GraphEngineering.Api.Providers;
using GraphEngineering.Core.Documents;
using GraphEngineering.Core.Execution;
using Microsoft.EntityFrameworkCore;

namespace GraphEngineering.Api.Runs;

public sealed class RunWorker(IServiceProvider services, RunCoordinator coordinator, ILogger<RunWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var runId = await Claim(stoppingToken);
                if (runId is { } id) await ExecuteRun(id, stoppingToken);
                else await Task.Delay(150, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
    public override Task StopAsync(CancellationToken cancellationToken)
    { coordinator.StopAdmission(); return base.StopAsync(cancellationToken); }

    private async Task<Guid?> Claim(CancellationToken token)
    {
        Guid? id = null; long sequence = 0;
        await coordinator.Gate.WaitAsync(token);
        try
        {
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(token);
            var run = await db.Runs.SingleOrDefaultAsync(x => x.State == "Queued" && x.ActiveSlot != null, token);
            if (run is null) return null;
            run.State = "Running"; run.StartedAtUtc = DateTime.UtcNow;
            RunState.Event(db, run, "run_started", state: run.State);
            await db.SaveChangesAsync(token); await transaction.CommitAsync(token);
            id = run.Id; sequence = run.LastSequence;
        }
        finally { coordinator.Gate.Release(); }
        if (id is { } value) await RunState.Publish(services, value, sequence);
        return id;
    }

    private async Task ExecuteRun(Guid id, CancellationToken stoppingToken)
    {
        using var local = coordinator.Register(id, stoppingToken);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            RunDetail initial;
            await using (var scope = services.CreateAsyncScope())
                initial = (await scope.ServiceProvider.GetRequiredService<WorkflowDbContext>().Runs.AsNoTracking().SingleAsync(x => x.Id == id, stoppingToken)).Detail();
            var definition = initial.Snapshot.Definition.Nodes.ToDictionary(x => x.Id, StringComparer.Ordinal);
            var outputs = new Dictionary<string, NodeOutput>(StringComparer.Ordinal);
            var deadline = initial.CreatedAt.AddMinutes(10);
            foreach (var plannedNode in initial.Nodes)
            {
                if (await CheckStop(id, deadline, stoppingToken)) return;
                var node = definition[plannedNode.NodeId];
                if (node.Type == "modelCall")
                {
                    var pinned = initial.Profiles.Single(x => x.NodeId == node.Id);
                    var acquisition = await Acquire(pinned, local.Token);
                    if (acquisition.Failure is { } failure) { await FailNode(id, node.Id, failure, "NotStarted"); return; }
                    var profile = acquisition.Profile!; var credential = acquisition.Credential;
                    if (credential is not null) keys.Add(credential);
                    if (ResponsesProbe.ContainsSensitive(profile.Name, keys) || ResponsesProbe.ContainsSensitive(profile.ModelId, keys) || ResponsesProbe.ContainsSensitive(profile.BaseUrl, keys))
                    { await FailNode(id, node.Id, "sensitive_configuration", "NotStarted"); return; }
                    ResolvedPrompt resolved;
                    try { resolved = BindingResolver.Render(node, initial.Input, outputs); }
                    catch (ExecutionException error) { await FailNode(id, node.Id, error.Code, "NotStarted"); return; }
                    if (ResponsesProbe.ContainsSensitive(resolved.Prompt, keys) || resolved.Inputs.Values.Any(value => RunPrivacy.ContainsCredential(value, keys)))
                    { await FailNode(id, node.Id, "sensitive_input", "NotStarted"); return; }
                    var admission = services.GetRequiredService<ProbeAdmission>();
                    if (admission.Enter(profile.Id) != 0) { await FailNode(id, node.Id, "provider_busy", "NotStarted"); return; }
                    try
                    {
                        var prepared = await Mutate(id, (db, run, nodes) =>
                        {
                            if (run.State != "Running") return false;
                            var current = nodes.Single(x => x.NodeId == node.Id);
                            current.State = "Running";
                            current.Attempt = new RunAttempt { Prompt = resolved.Prompt, ResolvedInputs = new(resolved.Inputs, StringComparer.Ordinal) };
                            RunState.Artifact(db, run, current, "resolved_inputs", "application/json", RunJson.Write(resolved.Inputs));
                            RunState.Artifact(db, run, current, "prompt", "text/plain", resolved.Prompt);
                            RunState.Event(db, run, "node_started", node.Id, "Running");
                            return true;
                        });
                        if (!prepared || await CheckStop(id, deadline, stoppingToken)) return;
                        var intended = await Mutate(id, (db, run, nodes) =>
                        {
                            if (run.State != "Running") return false;
                            var attempt = nodes.Single(x => x.NodeId == node.Id).Attempt!;
                            attempt.DispatchIntentAt = DateTimeOffset.UtcNow;
                            // Once intent is durable, a reader cannot know whether the request
                            // crossed the network. A definitive local preflight failure may reset it.
                            attempt.ExternalOutcome = "Unknown";
                            RunState.Event(db, run, "dispatch_intent", node.Id, "Running"); return true;
                        });
                        if (!intended || await CheckStop(id, deadline, stoppingToken)) return;
                        // Reacquire a coherent joined profile/key immediately before sending. Nothing from a prior connection version is archived.
                        acquisition = await Acquire(pinned, local.Token);
                        if (acquisition.Failure is { } changed) { await FailNode(id, node.Id, changed, "NotStarted"); return; }
                        if (acquisition.Credential is not null) keys.Add(acquisition.Credential);
                        using var call = CancellationTokenSource.CreateLinkedTokenSource(local.Token);
                        var remaining = deadline - DateTimeOffset.UtcNow;
                        if (remaining <= TimeSpan.Zero) { await FailNode(id, node.Id, "run_deadline", "NotStarted"); return; }
                        call.CancelAfter(remaining);
                        var response = await services.GetRequiredService<ResponsesProbe>().ExecuteAsync(acquisition.Profile!, acquisition.Credential, resolved.Prompt, keys, call.Token);
                        if (await CheckStop(id, deadline, stoppingToken, node.Id, response.ExternalOutcome)) return;
                        if (!response.Success) { await FailNode(id, node.Id, response.Category, response.ExternalOutcome, response.Message); return; }
                        JsonElement? json = null;
                        try { if (node.Configuration.GetProperty("outputMode").GetString() == "jsonObject") json = StrictExecutionJson.ParseObjectOutput(response.Text!); }
                        catch (ExecutionException error) { await FailNode(id, node.Id, error.Code, response.ExternalOutcome); return; }
                        if (json is { } decoded && RunPrivacy.ContainsCredential(decoded, keys))
                        { await FailNode(id, node.Id, "sensitive_output", response.ExternalOutcome); return; }
                        var committed = await Mutate(id, (db, run, nodes) =>
                        {
                            if (run.State != "Running") return false;
                            if (stoppingToken.IsCancellationRequested || DateTimeOffset.UtcNow >= deadline)
                            { RunState.Finish(db, run, nodes, stoppingToken.IsCancellationRequested ? "Interrupted" : "Failed", stoppingToken.IsCancellationRequested ? "host_shutdown" : "run_deadline"); return true; }
                            var current = nodes.Single(x => x.NodeId == node.Id); var attempt = current.Attempt!;
                            current.State = "Succeeded"; attempt.FinishedAt = DateTimeOffset.UtcNow;
                            attempt.ExternalOutcome = response.ExternalOutcome; attempt.OutputText = response.Text; attempt.OutputJson = json;
                            attempt.ObservedModel = response.ObservedModel; attempt.RequestId = response.RequestId; attempt.Usage = response.Usage;
                            RunState.Artifact(db, run, current, "output_text", "text/plain", response.Text!);
                            if (json is { } parsed) RunState.Artifact(db, run, current, "output_json", "application/json", RunJson.Write(parsed));
                            RunState.Event(db, run, "node_finished", node.Id, "Succeeded"); return true;
                        });
                        if (!committed) { await CheckStop(id, deadline, stoppingToken, node.Id, response.ExternalOutcome); return; }
                        outputs.Add(node.Id, new(response.Text!, json));
                    }
                    finally { admission.Leave(profile.Id); }
                }
                else
                {
                    JsonElement? finalResult = null;
                    if (node.Type == "end")
                    {
                        try { finalResult = BindingResolver.Resolve(node.Configuration.GetProperty("resultBinding"), initial.Input, outputs); }
                        catch (ExecutionException error) { await FailNode(id, node.Id, error.Code, "NotStarted"); return; }
                    }
                    var completed = await Mutate(id, (db, run, nodes) =>
                    {
                        if (run.State != "Running") return false;
                        if (stoppingToken.IsCancellationRequested || DateTimeOffset.UtcNow >= deadline)
                        { RunState.Finish(db, run, nodes, stoppingToken.IsCancellationRequested ? "Interrupted" : "Failed", stoppingToken.IsCancellationRequested ? "host_shutdown" : "run_deadline"); return true; }
                        var current = nodes.Single(x => x.NodeId == node.Id);
                        current.State = "Succeeded"; current.Attempt = new RunAttempt { FinishedAt = DateTimeOffset.UtcNow };
                        RunState.Event(db, run, "node_finished", node.Id, "Succeeded");
                        if (node.Type == "end")
                        {
                            run.ResultJson = RunJson.Write(finalResult);
                            RunState.Artifact(db, run, current, "result", "application/json", run.ResultJson);
                            RunState.Finish(db, run, nodes, "Succeeded", null);
                        }
                        return true;
                    });
                    if (!completed) { await CheckStop(id, deadline, stoppingToken); return; }
                }
            }
        }
        catch (OperationCanceledException)
        {
            await CheckStop(id, DateTimeOffset.MaxValue, stoppingToken);
        }
        catch (Exception)
        {
            // A failed write after sending must never enter the queue again. Durable intent is retained for restart recovery.
            logger.LogError("Run worker stopped after a local processing or persistence failure. No call will be retried.");
            try { await Mutate(id, (db, run, nodes) => { RunState.Finish(db, run, nodes, "Interrupted", "local_processing_failure"); return true; }); }
            catch (Exception) { logger.LogError("Interrupted run state could not be persisted. Startup recovery will reconcile the retained dispatch intent."); }
        }
        finally { coordinator.Remove(id); keys.Clear(); }
    }

    private async Task<(ProviderRecord? Profile, string? Credential, string? Failure)> Acquire(RunProfile pinned, CancellationToken token)
    {
        await using var scope = services.CreateAsyncScope();
        var profile = await scope.ServiceProvider.GetRequiredService<WorkflowDbContext>().ProviderProfiles.AsNoTracking().Include(x => x.Credential)
            .SingleOrDefaultAsync(x => x.Id == pinned.ProviderProfileId, token);
        if (profile is null || profile.ConnectionVersion != pinned.ConnectionVersion) return (null, null, "configuration_changed");
        string? credential = null;
        if (profile.AuthMode == "bearer")
        {
            if (profile.Credential is null) return (null, null, "credential_missing");
            try { credential = scope.ServiceProvider.GetRequiredService<ISecretStore>().Unprotect(profile.Credential.Ciphertext); }
            catch (SecretStoreException) { return (null, null, "credential_unreadable"); }
        }
        return (profile, credential, null);
    }
    private async Task<bool> CheckStop(Guid id, DateTimeOffset deadline, CancellationToken stoppingToken, string? nodeId = null, string? externalOutcome = null)
    {
        var stopped = false;
        await Mutate(id, (db, run, nodes) =>
        {
            if (run.ActiveSlot is null) { stopped = true; return false; }
            string? state = null, reason = null;
            if (stoppingToken.IsCancellationRequested) { state = "Interrupted"; reason = "host_shutdown"; }
            else if (run.State == "CancelRequested") { state = "Cancelled"; reason = "user_cancelled"; }
            else if (DateTimeOffset.UtcNow >= deadline) { state = "Failed"; reason = "run_deadline"; }
            if (state is null) return false;
            if (nodeId is not null && externalOutcome is not null && nodes.Single(x => x.NodeId == nodeId).Attempt is { } attempt) attempt.ExternalOutcome = externalOutcome;
            RunState.Finish(db, run, nodes, state, reason); stopped = true; return true;
        });
        return stopped;
    }
    private async Task FailNode(Guid id, string nodeId, string code, string outcome, string? message = null)
    {
        await Mutate(id, (db, run, nodes) =>
        {
            if (run.State == "CancelRequested") { RunState.Finish(db, run, nodes, "Cancelled", "user_cancelled"); return true; }
            if (run.State != "Running") return false;
            var node = nodes.Single(x => x.NodeId == nodeId); node.State = "Failed"; node.Attempt ??= new();
            node.Attempt.FinishedAt = DateTimeOffset.UtcNow; node.Attempt.ExternalOutcome = outcome; node.Attempt.FailureCode = code;
            node.Attempt.Message = message ?? "The node could not complete. No automatic retry was sent.";
            RunState.Event(db, run, "node_finished", nodeId, "Failed", code);
            RunState.Finish(db, run, nodes, "Failed", code); return true;
        });
    }
    private async Task<bool> Mutate(Guid id, Func<WorkflowDbContext, RunRecord, List<RunNode>, bool> mutation)
    {
        long sequence = 0; bool changed;
        await coordinator.Gate.WaitAsync();
        try
        {
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            var run = await db.Runs.SingleAsync(x => x.Id == id);
            var nodes = RunJson.Read<List<RunNode>>(run.NodesJson);
            changed = mutation(db, run, nodes);
            if (!changed) return false;
            run.NodesJson = RunJson.Write(nodes);
            await db.SaveChangesAsync(); await transaction.CommitAsync(); sequence = run.LastSequence;
        }
        finally { coordinator.Gate.Release(); }
        if (sequence > 0) await RunState.Publish(services, id, sequence);
        return changed;
    }
}
