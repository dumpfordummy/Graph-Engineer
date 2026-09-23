using GraphEngineering.Core.Documents;

namespace GraphEngineering.Core.Validation;

public static class GraphValidator
{
    public static ValidationReport Validate(DocumentReadResult parsed)
    {
        if (!parsed.Success) return new(false, false, parsed.Issues, DocumentJson.ValidationScope);
        return Validate(parsed.Document!);
    }

    // Call only on a document that passed DocumentReader; references and IDs are guaranteed safe.
    public static ValidationReport Validate(WorkflowDocument document)
    {
        var issues = new List<ValidationIssue>();
        var nodes = document.Definition.Nodes;
        var edges = document.Definition.Edges;
        void Add(string code, string message, string path, string? nodeId = null, string? edgeId = null) => issues.Add(new(code, "error", message, path, nodeId, edgeId));
        if (string.IsNullOrWhiteSpace(document.Workflow.Name)) Add("empty_workflow_name", "Give the workflow a name.", "workflow.name");
        var starts = nodes.Where(node => node.Type == "start").ToList();
        var ends = nodes.Where(node => node.Type == "end").ToList();
        if (starts.Count != 1) Add(starts.Count == 0 ? "missing_start" : "multiple_starts", "Add exactly one Start node.", "definition.nodes");
        if (ends.Count != 1) Add(ends.Count == 0 ? "missing_end" : "multiple_ends", "Add exactly one End node.", "definition.nodes");
        var outgoing = nodes.ToDictionary(node => node.Id, node => edges.Where(edge => edge.SourceNodeId == node.Id).ToList(), StringComparer.Ordinal);
        var incoming = nodes.ToDictionary(node => node.Id, node => edges.Count(edge => edge.TargetNodeId == node.Id), StringComparer.Ordinal);
        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            var path = $"definition.nodes[{i}]";
            if (string.IsNullOrWhiteSpace(node.Name)) Add("empty_node_name", "Give this node a name.", path + ".name", node.Id);
            if (node.Type == "modelCall" && string.IsNullOrWhiteSpace(node.Configuration.GetProperty("prompt").GetString())) Add("empty_prompt", "Write a prompt for this Model Call.", path + ".configuration.prompt", node.Id);
            if (outgoing[node.Id].Count > 1) Add("unsupported_branch", "A linear workflow supports one outgoing connection per node. Remove this branch.", path, node.Id);
            if (incoming[node.Id] > 1) Add("unsupported_merge", "A linear workflow supports one incoming connection per node. Remove this merge.", path, node.Id);
            if (incoming[node.Id] == 0 && outgoing[node.Id].Count == 0) Add("disconnected_node", "Connect this node to the workflow path.", path, node.Id);
        }

        if (starts.Count == 1)
        {
            var reachable = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Stack<string>();
            pending.Push(starts[0].Id);
            while (pending.TryPop(out var id))
            {
                if (!reachable.Add(id)) continue;
                foreach (var edge in outgoing[id]) pending.Push(edge.TargetNodeId);
            }
            foreach (var node in nodes.Where(node => !reachable.Contains(node.Id))) Add("unreachable_node", "This node is not reachable from Start.", "definition.nodes", node.Id);
            foreach (var end in ends.Where(end => !reachable.Contains(end.Id))) Add("unreachable_end", "Connect a path from Start to this End.", "definition.nodes", end.Id);
        }

        // A grey-to-grey DFS edge is on an actual cycle, including disconnected components.
        var colors = new Dictionary<string, int>(StringComparer.Ordinal);
        WorkflowEdge? FindCycle(string id)
        {
            colors[id] = 1;
            foreach (var edge in outgoing[id])
            {
                if (colors.GetValueOrDefault(edge.TargetNodeId) == 1) return edge;
                if (colors.GetValueOrDefault(edge.TargetNodeId) == 0 && FindCycle(edge.TargetNodeId) is { } cycle) return cycle;
            }
            colors[id] = 2;
            return null;
        }
        foreach (var node in nodes)
        {
            if (colors.GetValueOrDefault(node.Id) != 0 || FindCycle(node.Id) is not { } cycle) continue;
            Add("unsupported_cycle", "Cycles are not supported. Remove a connection in this cycle.", "definition.edges", edgeId: cycle.Id);
            break;
        }
        return new(true, issues.Count == 0, issues, DocumentJson.ValidationScope);
    }
}
