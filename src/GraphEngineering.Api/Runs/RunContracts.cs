using System.Text.Json;
using GraphEngineering.Api.Providers;
using GraphEngineering.Core.Documents;

namespace GraphEngineering.Api.Runs;

public sealed record ReadyNode(string NodeId, string Name, string Type, Guid? ProviderProfileId,
    string? ProfileName, string? ModelId, long? ConnectionVersion);
public sealed record RunReadiness(bool Ready, IReadOnlyList<ValidationIssue> Issues,
    IReadOnlyList<ReadyNode> OrderedNodes, int MaximumModelCalls);
public record RunSummary(Guid Id, Guid SubmissionId, Guid WorkflowId, string WorkflowName,
    long WorkflowRevision, string State, DateTimeOffset CreatedAt, DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt, long LastSequence, string? FailureCode);
public sealed record RunDetail(Guid Id, Guid SubmissionId, Guid WorkflowId, string WorkflowName,
    long WorkflowRevision, string State, DateTimeOffset CreatedAt, DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt, long LastSequence, string? FailureCode, WorkflowDocument Snapshot,
    JsonElement Input, IReadOnlyList<RunProfile> Profiles, IReadOnlyList<RunNode> Nodes, JsonElement? Result)
    : RunSummary(Id, SubmissionId, WorkflowId, WorkflowName, WorkflowRevision, State, CreatedAt, StartedAt, FinishedAt, LastSequence, FailureCode);
public sealed record RunProfile(string NodeId, Guid ProviderProfileId, string Name, long ConnectionVersion,
    string Protocol, string ModelId, string BaseUrl, string AuthMode, int TimeoutSeconds, int MaxOutputTokens,
    bool AllowPrivateNetwork, bool AllowInsecureHttp);
public sealed class RunNode
{
    public required string NodeId { get; init; }
    public required string Name { get; init; }
    public required string Type { get; init; }
    public string State { get; set; } = "Pending";
    public RunAttempt? Attempt { get; set; }
    public List<Guid> ArtifactIds { get; set; } = [];
    public string? SkipReason { get; set; }
}
public sealed class RunAttempt
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedAt { get; set; }
    public DateTimeOffset? DispatchIntentAt { get; set; }
    public string ExternalOutcome { get; set; } = "NotStarted";
    public Dictionary<string, JsonElement> ResolvedInputs { get; set; } = new(StringComparer.Ordinal);
    public string? Prompt { get; set; }
    public string? OutputText { get; set; }
    public JsonElement? OutputJson { get; set; }
    public string? FailureCode { get; set; }
    public string? Message { get; set; }
    public string? ObservedModel { get; set; }
    public string? RequestId { get; set; }
    public TestUsage? Usage { get; set; }
}
public sealed record RunEvent(long Sequence, DateTimeOffset At, string Kind, string? NodeId, string? State, string? Message);
public sealed record RunArtifact(Guid Id, string NodeId, string Kind, string ContentType, int ByteCount, string Sha256, string Text);
public interface IRunNotifier
{
    Task PublishAsync(Guid runId, long eventSequence, CancellationToken cancellationToken);
}
internal sealed class NullRunNotifier : IRunNotifier
{
    public Task PublishAsync(Guid runId, long eventSequence, CancellationToken cancellationToken) => Task.CompletedTask;
}
internal static class RunJson
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = false };
    internal static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);
    internal static T Read<T>(string value) => JsonSerializer.Deserialize<T>(value, Options)!;
    internal static JsonElement Element(string value) { using var parsed = JsonDocument.Parse(value); return parsed.RootElement.Clone(); }
}
