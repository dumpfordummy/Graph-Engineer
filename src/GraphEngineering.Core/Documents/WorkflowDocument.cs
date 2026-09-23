using System.Text.Json;
using System.Text.Json.Serialization;

namespace GraphEngineering.Core.Documents;

public sealed record WorkflowDocument(int FormatVersion, WorkflowMetadata Workflow, WorkflowDefinition Definition, EditorLayout Layout);
public sealed record WorkflowMetadata(Guid Id, string Name, string Description, long Revision, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record WorkflowDefinition(IReadOnlyList<WorkflowNode> Nodes, IReadOnlyList<WorkflowEdge> Edges);
public sealed record WorkflowNode(string Id, string Type, int TypeVersion, string Name, string Description, JsonElement Configuration);
public sealed record WorkflowEdge(string Id, string SourceNodeId, string SourcePort, string TargetNodeId, string TargetPort);
public sealed record EditorLayout(IReadOnlyList<NodePosition> Nodes, Viewport Viewport);
public sealed record NodePosition(string NodeId, double X, double Y);
public sealed record Viewport(double X, double Y, double Zoom);
public sealed record ValidationIssue(string Code, string Severity, string Message, string Path,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? NodeId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? EdgeId = null);
public sealed record ValidationReport(bool StructurallyValid, bool Valid, IReadOnlyList<ValidationIssue> Issues, string Scope);

public static class DocumentJson
{
    public const int MaximumBytes = 1_048_576;
    public const long MaximumSafeInteger = 9_007_199_254_740_991;
    public const string ValidationScope = "Structure and draft configuration only. Provider configuration and execution readiness are checked separately before a run.";
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
}
