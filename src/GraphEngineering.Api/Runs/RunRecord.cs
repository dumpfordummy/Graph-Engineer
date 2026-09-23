using GraphEngineering.Core.Documents;
using Microsoft.EntityFrameworkCore;

namespace GraphEngineering.Api.Runs;

public sealed class RunRecord
{
    public Guid Id { get; set; }
    public Guid SubmissionId { get; set; }
    public string Fingerprint { get; set; } = "";
    public Guid WorkflowId { get; set; }
    public string WorkflowName { get; set; } = "";
    public long WorkflowRevision { get; set; }
    public string State { get; set; } = "Queued";
    // The unique nullable slot is a database-enforced, application-wide admission constraint.
    public int? ActiveSlot { get; set; } = 1;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? FinishedAtUtc { get; set; }
    public long LastSequence { get; set; }
    public string? FailureCode { get; set; }
    public string SnapshotJson { get; set; } = "";
    public string InputJson { get; set; } = "";
    public string ProfilesJson { get; set; } = "[]";
    public string NodesJson { get; set; } = "[]";
    public string? ResultJson { get; set; }
    public RunSummary Summary() => new(Id, SubmissionId, WorkflowId, WorkflowName, WorkflowRevision, State,
        At(CreatedAtUtc), At(StartedAtUtc), At(FinishedAtUtc), LastSequence, FailureCode);
    public RunDetail Detail() => new(Id, SubmissionId, WorkflowId, WorkflowName, WorkflowRevision, State,
        At(CreatedAtUtc), At(StartedAtUtc), At(FinishedAtUtc), LastSequence, FailureCode,
        RunJson.Read<WorkflowDocument>(SnapshotJson), RunJson.Element(InputJson), RunJson.Read<List<RunProfile>>(ProfilesJson),
        RunJson.Read<List<RunNode>>(NodesJson), ResultJson is null ? null : RunJson.Element(ResultJson));
    private static DateTimeOffset At(DateTime time) => new(DateTime.SpecifyKind(time, DateTimeKind.Utc));
    private static DateTimeOffset? At(DateTime? time) => time is null ? null : At(time.Value);
}
public sealed class RunEventRecord
{
    public Guid RunId { get; set; }
    public long Sequence { get; set; }
    public DateTime AtUtc { get; set; }
    public string Kind { get; set; } = "";
    public string? NodeId { get; set; }
    public string? State { get; set; }
    public string? Message { get; set; }
    public RunEvent Public() => new(Sequence, new(DateTime.SpecifyKind(AtUtc, DateTimeKind.Utc)), Kind, NodeId, State, Message);
}
public sealed class RunArtifactRecord
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public string NodeId { get; set; } = "";
    public string Kind { get; set; } = "";
    public string ContentType { get; set; } = "";
    public int ByteCount { get; set; }
    public string Sha256 { get; set; } = "";
    public string Text { get; set; } = "";
    public RunArtifact Public() => new(Id, NodeId, Kind, ContentType, ByteCount, Sha256, Text);
}
public static class RunModel
{
    public static void Configure(ModelBuilder model)
    {
        var run = model.Entity<RunRecord>();
        run.ToTable("Runs"); run.HasKey(x => x.Id); run.Property(x => x.Id).ValueGeneratedNever();
        run.Property(x => x.LastSequence).IsConcurrencyToken();
        run.HasIndex(x => x.SubmissionId).IsUnique(); run.HasIndex(x => x.ActiveSlot).IsUnique();
        run.HasIndex(x => new { x.WorkflowId, x.CreatedAtUtc }); run.HasIndex(x => x.CreatedAtUtc);
        var events = model.Entity<RunEventRecord>(); events.ToTable("RunEvents"); events.HasKey(x => new { x.RunId, x.Sequence });
        events.HasOne<RunRecord>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
        var artifacts = model.Entity<RunArtifactRecord>(); artifacts.ToTable("RunArtifacts"); artifacts.HasKey(x => x.Id);
        artifacts.Property(x => x.Id).ValueGeneratedNever(); artifacts.HasIndex(x => x.RunId);
        artifacts.HasOne<RunRecord>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.Cascade);
    }
}
