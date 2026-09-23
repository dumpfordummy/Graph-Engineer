using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using GraphEngineering.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GraphEngineering.Api.Runs;

public sealed class RunCoordinator : IDisposable
{
    private readonly CancellationTokenRegistration stoppingRegistration;
    private volatile bool accepting = true;
    public RunCoordinator(IHostApplicationLifetime lifetime) => stoppingRegistration = lifetime.ApplicationStopping.Register(StopAdmission);
    internal SemaphoreSlim Gate { get; } = new(1, 1);
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> cancellations = new();
    public bool Accepting => accepting;
    internal CancellationTokenSource Register(Guid runId, CancellationToken stopping)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        if (!cancellations.TryAdd(runId, source)) { source.Dispose(); throw new InvalidOperationException("Run is already owned by the worker."); }
        return source;
    }
    internal void Remove(Guid runId) => cancellations.TryRemove(runId, out _);
    internal void Cancel(Guid runId) { if (cancellations.TryGetValue(runId, out var source)) { try { source.Cancel(); } catch (ObjectDisposedException) { } } }
    public void StopAdmission() => accepting = false;
    public void Dispose() { stoppingRegistration.Dispose(); Gate.Dispose(); foreach (var source in cancellations.Values) source.Dispose(); }
}

public static class RunState
{
    public static IServiceCollection AddRuns(this IServiceCollection services)
    {
        services.AddSingleton<RunCoordinator>();
        services.TryAddSingleton<IRunNotifier, NullRunNotifier>();
        services.AddHostedService<RunWorker>();
        return services;
    }
    public static async Task<bool> ActiveProfileReferencedAsync(WorkflowDbContext db, Guid profileId, CancellationToken token)
    {
        var snapshots = await db.Runs.AsNoTracking().Where(x => x.ActiveSlot != null).Select(x => x.ProfilesJson).ToListAsync(token);
        return snapshots.Any(json => RunJson.Read<List<RunProfile>>(json).Any(profile => profile.ProviderProfileId == profileId));
    }
    public static async Task RecoverRunsAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var unfinished = await db.Runs.Where(x => x.ActiveSlot != null).ToListAsync();
        foreach (var run in unfinished)
        {
            var nodes = RunJson.Read<List<RunNode>>(run.NodesJson);
            Finish(db, run, nodes, "Interrupted", "backend_restarted");
        }
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }
    internal static void Event(WorkflowDbContext db, RunRecord run, string kind, string? nodeId = null, string? state = null, string? message = null)
    {
        // Called only inside the same write transaction as the corresponding state transition.
        db.RunEvents.Add(new RunEventRecord { RunId = run.Id, Sequence = ++run.LastSequence,
            AtUtc = DateTime.UtcNow, Kind = kind, NodeId = nodeId, State = state, Message = message });
    }
    internal static void Artifact(WorkflowDbContext db, RunRecord run, RunNode node, string kind, string contentType, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var artifact = new RunArtifactRecord { Id = Guid.NewGuid(), RunId = run.Id, NodeId = node.NodeId,
            Kind = kind, ContentType = contentType, Text = text, ByteCount = bytes.Length, Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)) };
        db.RunArtifacts.Add(artifact); node.ArtifactIds.Add(artifact.Id);
    }
    internal static void Finish(WorkflowDbContext db, RunRecord run, List<RunNode> nodes, string state, string? failureCode)
    {
        if (run.ActiveSlot is null) return;
        run.State = state; run.ActiveSlot = null; run.FailureCode = failureCode; run.FinishedAtUtc = DateTime.UtcNow;
        foreach (var node in nodes.Where(x => x.State is "Running" or "Pending"))
        {
            node.State = node.State == "Pending" ? "Skipped" : state is "Cancelled" or "Failed" ? state : "Interrupted";
            node.SkipReason = failureCode ?? state;
            if (node.Attempt is { } attempt)
            {
                attempt.FinishedAt = DateTimeOffset.UtcNow; attempt.FailureCode ??= failureCode;
                if (attempt.DispatchIntentAt is not null && attempt.ExternalOutcome == "NotStarted") attempt.ExternalOutcome = "Unknown";
            }
            Event(db, run, "node_finished", node.NodeId, node.State, failureCode);
        }
        run.NodesJson = RunJson.Write(nodes);
        Event(db, run, "run_finished", state: state, message: failureCode);
    }
    internal static async Task Publish(IServiceProvider services, Guid id, long sequence)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try { await services.GetRequiredService<IRunNotifier>().PublishAsync(id, sequence, timeout.Token).WaitAsync(timeout.Token); }
        catch (Exception) { services.GetRequiredService<ILogger<RunWorker>>().LogWarning("Run notification delivery failed; committed state remains available through REST."); }
    }
}
