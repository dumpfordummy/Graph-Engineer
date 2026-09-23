using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GraphEngineering.Core.Documents;
using GraphEngineering.Core.Validation;

namespace GraphEngineering.Core.Execution;

public sealed record ExecutionPlan(bool Ready, IReadOnlyList<WorkflowNode> OrderedNodes, IReadOnlyList<ValidationIssue> Issues);

/// <summary>Validates pure execution configuration. Provider existence and authorization remain backend concerns.</summary>
public static class ExecutionPlanner
{
    public const int MaximumModelCalls = 16;

    public static ExecutionPlan Validate(WorkflowDocument document)
    {
        var shape = DocumentReader.Read(JsonSerializer.SerializeToElement(document, DocumentJson.Options));
        if (!shape.Success) return new(false, [], shape.Issues);
        var structural = GraphValidator.Validate(document);
        if (!structural.Valid) return new(false, [], structural.Issues);

        var issues = new List<ValidationIssue>();
        var nodes = document.Definition.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var edges = document.Definition.Edges.ToDictionary(edge => edge.SourceNodeId, StringComparer.Ordinal);
        var ordered = new List<WorkflowNode>();
        var current = document.Definition.Nodes.Single(node => node.Type == "start");
        while (true)
        {
            ordered.Add(current);
            if (!edges.TryGetValue(current.Id, out var next)) break;
            current = nodes[next.TargetNodeId];
        }
        if (ordered.Count != nodes.Count || ordered[^1].Type != "end")
            return new(false, ordered, [new("incomplete_path", "error", "Connect every node on one Start-to-End path.", "definition.edges")]);
        if (ordered.Count(node => node.Type == "modelCall") > MaximumModelCalls)
            Add("too_many_model_calls", "At most 16 Model Calls are permitted in a run.", "definition.nodes");

        var previous = new Dictionary<string, WorkflowNode>(StringComparer.Ordinal);
        foreach (var node in ordered)
        {
            var index = document.Definition.Nodes.ToList().FindIndex(item => item.Id == node.Id);
            var path = $"definition.nodes[{index}].configuration";
            if (node.Type is "modelCall" or "end" && node.TypeVersion != 2)
            {
                Add("execution_upgrade_required", "Explicitly upgrade this node to configure M3 execution. Existing prompts retain literal semantics.", $"definition.nodes[{index}].typeVersion", node.Id);
                previous.Add(node.Id, node);
                continue;
            }

            if (node.Type == "modelCall")
            {
                var config = node.Configuration;
                if (!config.TryGetProperty("providerProfileId", out var profile) || profile.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(profile.GetString()))
                    Add("provider_required", "Select a saved provider profile for this Model Call.", path + ".providerProfileId", node.Id);
                var prompt = config.GetProperty("prompt").GetString()!;
                if (Encoding.UTF8.GetByteCount(prompt) > BindingResolver.MaximumPromptBytes)
                    Add("prompt_too_large", "The prompt exceeds 64 KiB UTF-8 before resolving bindings.", path + ".prompt", node.Id);
                var bindings = config.GetProperty("inputBindings");
                var mode = config.GetProperty("promptMode").GetString();
                if (mode == "literal" && bindings.GetArrayLength() != 0)
                    Add("literal_bindings", "Literal mode requires an empty binding list. Remove bindings or select bindings mode.", path + ".inputBindings", node.Id);
                var aliases = new HashSet<string>(StringComparer.Ordinal);
                var used = mode == "bindings"
                    ? BindingResolver.PlaceholderPattern().Matches(prompt).Cast<Match>().Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal)
                    : [];
                var bindingIndex = 0;
                foreach (var binding in bindings.EnumerateArray())
                {
                    var bindingPath = path + $".inputBindings[{bindingIndex++}]";
                    var alias = binding.GetProperty("alias").GetString()!;
                    if (!BindingResolver.AliasPattern().IsMatch(alias))
                        Add("invalid_binding_alias", "Use a case-sensitive ASCII identifier of at most 64 characters, beginning with a letter or underscore.", bindingPath + ".alias", node.Id);
                    if (!aliases.Add(alias)) Add("duplicate_binding_alias", "Each input alias must be unique within this node.", bindingPath + ".alias", node.Id);
                    ValidateSource(binding.GetProperty("source"), bindingPath + ".source", node.Id, previous);
                    if (mode == "bindings" && !used.Contains(alias))
                        issues.Add(new("unused_binding", "warning", "This input binding is not used by a canonical prompt placeholder.", bindingPath + ".alias", node.Id));
                }
                foreach (var alias in used.Where(alias => !aliases.Contains(alias)))
                    Add("unbound_placeholder", "Every canonical prompt placeholder needs exactly one binding.", path + ".prompt", node.Id);
            }
            else if (node.Type == "end")
            {
                var source = node.Configuration.GetProperty("resultBinding");
                if (source.ValueKind == JsonValueKind.Null) Add("result_binding_required", "Select the explicit final result source for End.", path + ".resultBinding", node.Id);
                else ValidateSource(source, path + ".resultBinding", node.Id, previous);
            }
            previous.Add(node.Id, node);
        }
        return new(issues.All(issue => issue.Severity != "error"), ordered, issues);

        void Add(string code, string message, string path, string? nodeId = null) => issues.Add(new(code, "error", message, path, nodeId));

        void ValidateSource(JsonElement source, string path, string ownerId, IReadOnlyDictionary<string, WorkflowNode> earlier)
        {
            var kind = source.GetProperty("kind").GetString();
            if (kind is "runInput" or "nodeJson" && !BindingResolver.IsValidPointer(source.GetProperty("pointer").GetString()!))
                Add("invalid_json_pointer", "Use a valid RFC 6901 pointer: empty for root, or slash-separated keys with ~0 and ~1 escapes.", path + ".pointer", ownerId);
            if (kind is not ("nodeText" or "nodeJson")) return;
            var id = source.GetProperty("nodeId").GetString()!;
            if (!earlier.TryGetValue(id, out var selected) || selected.Type != "modelCall")
            {
                Add("invalid_binding_source", "Select an earlier Model Call on this workflow's current control path. Self, future and missing nodes cannot supply data.", path + ".nodeId", ownerId);
                return;
            }
            if (kind == "nodeJson" && (selected.TypeVersion != 2 || selected.Configuration.GetProperty("outputMode").GetString() != "jsonObject"))
                Add("binding_source_not_json", "Select an earlier Model Call configured for JSON object — local validation.", path + ".nodeId", ownerId);
        }
    }
}
