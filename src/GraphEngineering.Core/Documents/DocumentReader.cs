using System.Text.Json;

namespace GraphEngineering.Core.Documents;

public sealed record DocumentReadResult(WorkflowDocument? Document, IReadOnlyList<ValidationIssue> Issues)
{
    public bool Success => Document is not null && Issues.Count == 0;
}

/// <summary>Checks the wire shape before deserialization. Draft semantics belong to GraphValidator.</summary>
public static class DocumentReader
{
    public static JsonDocument ParseJson(ReadOnlyMemory<byte> bytes)
    {
        var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
        try
        {
            RejectDuplicateProperties(json.RootElement, "$");
            return json;
        }
        catch (InvalidOperationException exception)
        {
            json.Dispose();
            throw new JsonException("JSON property names and strings must contain valid Unicode.", exception);
        }
        catch
        {
            json.Dispose();
            throw;
        }
    }

    private static void RejectDuplicateProperties(JsonElement element, string path)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new JsonException($"Duplicate property at {path}.{property.Name}.", path + "." + property.Name, null, null);
                RejectDuplicateProperties(property.Value, path + "." + property.Name);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var child in element.EnumerateArray()) RejectDuplicateProperties(child, $"{path}[{index++}]");
        }
        else if (element.ValueKind == JsonValueKind.String)
        {
            // JsonDocument defers UTF-16 escape validation until a string is decoded.
            _ = element.GetString();
        }
    }

    public static DocumentReadResult Read(JsonElement root)
    {
        var reader = new ShapeReader();
        reader.Object(root, "", ["formatVersion", "workflow", "definition", "layout"]);
        reader.Integer(root, "formatVersion", "", 1, 1);
        if (reader.Child(root, "workflow", out var workflow))
        {
            const string p = "workflow";
            reader.Object(workflow, p, ["id", "name", "description", "revision", "createdAt", "updatedAt"]);
            reader.Text(workflow, "id", p, 100, false);
            if (reader.StringValue(workflow, "id") is { } id && !Guid.TryParseExact(id, "D", out _)) reader.Error("invalid_id", "Use a UUID for the workflow ID.", p + ".id");
            reader.Text(workflow, "name", p, 120);
            reader.Text(workflow, "description", p, 2000);
            reader.Integer(workflow, "revision", p, 0, DocumentJson.MaximumSafeInteger);
            reader.Timestamp(workflow, "createdAt", p);
            reader.Timestamp(workflow, "updatedAt", p);
        }

        var nodeTypes = new Dictionary<string, string>(StringComparer.Ordinal);
        if (reader.Child(root, "definition", out var definition))
        {
            reader.Object(definition, "definition", ["nodes", "edges"]);
            if (reader.Array(definition, "nodes", "definition", 200, out var nodes))
            {
                var i = 0;
                foreach (var node in nodes.EnumerateArray())
                {
                    var p = $"definition.nodes[{i++}]";
                    reader.Object(node, p, ["id", "type", "typeVersion", "name", "description", "configuration"]);
                    reader.Text(node, "id", p, 100, false);
                    reader.Text(node, "type", p, 30, false);
                    reader.Text(node, "name", p, 120);
                    reader.Text(node, "description", p, 2000);
                    var id = reader.StringValue(node, "id");
                    var type = reader.StringValue(node, "type");
                    reader.Integer(node, "typeVersion", p, 1, type is "modelCall" or "end" ? 2 : 1);
                    var version = reader.Child(node, "typeVersion", out var versionValue) && versionValue.ValueKind == JsonValueKind.Number && versionValue.TryGetInt32(out var parsedVersion) ? parsedVersion : 0;
                    if (id is not null && !nodeTypes.TryAdd(id, type ?? "")) reader.Error("duplicate_node_id", "Node IDs must be unique.", p + ".id", id);
                    if (type is not null && type is not ("start" or "modelCall" or "end")) reader.Error("unsupported_node_type", "Supported node types are start, modelCall, and end.", p + ".type", id);
                    if (!reader.Child(node, "configuration", out var config)) continue;
                    switch (type)
                    {
                        case "start":
                            reader.Object(config, p + ".configuration", ["sampleInput"]);
                            reader.Text(config, "sampleInput", p + ".configuration", 50_000);
                            break;
                        case "modelCall":
                            reader.Object(config, p + ".configuration", version == 2 ? ["prompt", "promptMode", "inputBindings", "outputMode"] : ["prompt"], ["providerProfileId"]);
                            reader.Text(config, "prompt", p + ".configuration", 50_000);
                            if (reader.Child(config, "providerProfileId", out var provider) && provider.ValueKind != JsonValueKind.Null)
                                reader.Text(config, "providerProfileId", p + ".configuration", 120);
                            if (version == 2)
                            {
                                reader.Choice(config, "promptMode", p + ".configuration", ["literal", "bindings"]);
                                reader.Choice(config, "outputMode", p + ".configuration", ["text", "jsonObject"]);
                                if (reader.Array(config, "inputBindings", p + ".configuration", 32, out var bindings))
                                {
                                    var bindingIndex = 0;
                                    foreach (var binding in bindings.EnumerateArray())
                                    {
                                        var bindingPath = p + $".configuration.inputBindings[{bindingIndex++}]";
                                        reader.Object(binding, bindingPath, ["alias", "source"]);
                                        reader.Text(binding, "alias", bindingPath, 64);
                                        if (reader.Child(binding, "source", out var source)) ReadBindingSource(reader, source, bindingPath + ".source");
                                    }
                                }
                            }
                            break;
                        case "end":
                            reader.Object(config, p + ".configuration", version == 2 ? ["resultReference", "resultBinding"] : ["resultReference"]);
                            reader.Text(config, "resultReference", p + ".configuration", 2000);
                            if (version == 2 && reader.Child(config, "resultBinding", out var resultBinding) && resultBinding.ValueKind != JsonValueKind.Null)
                                ReadBindingSource(reader, resultBinding, p + ".configuration.resultBinding");
                            break;
                    }
                }
            }

            if (reader.Array(definition, "edges", "definition", 400, out var edges))
            {
                var ids = new HashSet<string>(StringComparer.Ordinal);
                var connections = new HashSet<(string?, string?, string?, string?)>();
                var i = 0;
                foreach (var edge in edges.EnumerateArray())
                {
                    var p = $"definition.edges[{i++}]";
                    reader.Object(edge, p, ["id", "sourceNodeId", "sourcePort", "targetNodeId", "targetPort"]);
                    foreach (var field in new[] { "id", "sourceNodeId", "sourcePort", "targetNodeId", "targetPort" }) reader.Text(edge, field, p, 100, false);
                    var id = reader.StringValue(edge, "id");
                    var source = reader.StringValue(edge, "sourceNodeId");
                    var target = reader.StringValue(edge, "targetNodeId");
                    var sourcePort = reader.StringValue(edge, "sourcePort");
                    var targetPort = reader.StringValue(edge, "targetPort");
                    if (id is not null && !ids.Add(id)) reader.Error("duplicate_edge_id", "Edge IDs must be unique.", p + ".id", edgeId: id);
                    if (!connections.Add((source, sourcePort, target, targetPort))) reader.Error("duplicate_connection", "This connection already exists.", p, edgeId: id);
                    if (source is not null && !nodeTypes.ContainsKey(source)) reader.Error("missing_source", "The source node does not exist.", p + ".sourceNodeId", edgeId: id);
                    if (target is not null && !nodeTypes.ContainsKey(target)) reader.Error("missing_target", "The target node does not exist.", p + ".targetNodeId", edgeId: id);
                    if (sourcePort is not null && (sourcePort != "out" || source is not null && nodeTypes.GetValueOrDefault(source) == "end")) reader.Error("invalid_source_port", "Use output port out on a Start or Model Call node.", p + ".sourcePort", edgeId: id);
                    if (targetPort is not null && (targetPort != "in" || target is not null && nodeTypes.GetValueOrDefault(target) == "start")) reader.Error("invalid_target_port", "Use input port in on a Model Call or End node.", p + ".targetPort", edgeId: id);
                }
            }
        }

        if (reader.Child(root, "layout", out var layout))
        {
            reader.Object(layout, "layout", ["nodes", "viewport"]);
            if (reader.Array(layout, "nodes", "layout", 200, out var positions))
            {
                var ids = new HashSet<string>(StringComparer.Ordinal);
                var i = 0;
                foreach (var position in positions.EnumerateArray())
                {
                    var p = $"layout.nodes[{i++}]";
                    reader.Object(position, p, ["nodeId", "x", "y"]);
                    reader.Text(position, "nodeId", p, 100, false);
                    reader.Number(position, "x", p, -100_000, 100_000);
                    reader.Number(position, "y", p, -100_000, 100_000);
                    var id = reader.StringValue(position, "nodeId");
                    if (id is null) continue;
                    if (!ids.Add(id)) reader.Error("duplicate_position", "Each node must have exactly one layout entry.", p + ".nodeId", id);
                    if (!nodeTypes.ContainsKey(id)) reader.Error("unknown_layout_node", "Layout refers to a node that does not exist.", p + ".nodeId", id);
                }
                foreach (var id in nodeTypes.Keys.Where(id => !ids.Contains(id))) reader.Error("missing_position", "Each node requires a layout entry.", "layout.nodes", id);
            }
            if (reader.Child(layout, "viewport", out var viewport))
            {
                reader.Object(viewport, "layout.viewport", ["x", "y", "zoom"]);
                reader.Number(viewport, "x", "layout.viewport", -100_000, 100_000);
                reader.Number(viewport, "y", "layout.viewport", -100_000, 100_000);
                reader.Number(viewport, "zoom", "layout.viewport", 0.1, 4);
            }
        }
        if (reader.Issues.Count != 0) return new(null, reader.Issues);
        return new(root.Deserialize<WorkflowDocument>(DocumentJson.Options), reader.Issues);
    }

    private static void ReadBindingSource(ShapeReader reader, JsonElement source, string path)
    {
        var kind = reader.StringValue(source, "kind");
        reader.Object(source, path, kind switch
        {
            "runInput" => ["kind", "pointer"],
            "nodeText" => ["kind", "nodeId"],
            "nodeJson" => ["kind", "nodeId", "pointer"],
            _ => ["kind"]
        });
        reader.Choice(source, "kind", path, ["runInput", "nodeText", "nodeJson"]);
        if (kind is "nodeText" or "nodeJson") reader.Text(source, "nodeId", path, 100);
        if (kind is "runInput" or "nodeJson") reader.Text(source, "pointer", path, 2048);
    }

    public sealed class ShapeReader
    {
        public List<ValidationIssue> Issues { get; } = [];
        public void Error(string code, string message, string path, string? nodeId = null, string? edgeId = null) => Issues.Add(new(code, "error", message, path.Length == 0 ? "$" : path, nodeId, edgeId));
        private static string Path(string path, string name) => path.Length == 0 ? name : path + "." + name;
        public bool Child(JsonElement element, string name, out JsonElement value)
        {
            value = default;
            return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value);
        }
        public string? StringValue(JsonElement element, string name) => Child(element, name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        public void Object(JsonElement element, string path, string[] required, string[]? optional = null)
        {
            if (element.ValueKind != JsonValueKind.Object) { Error("invalid_shape", "Expected an object.", path); return; }
            foreach (var property in element.EnumerateObject())
                if (!required.Contains(property.Name, StringComparer.Ordinal) && !(optional?.Contains(property.Name, StringComparer.Ordinal) ?? false)) Error("unknown_property", "Unknown property is not supported.", Path(path, property.Name));
            foreach (var name in required)
                if (!element.TryGetProperty(name, out _)) Error("missing_property", "Required property is missing.", Path(path, name));
        }
        public void Text(JsonElement element, string name, string path, int maximum, bool allowEmpty = true)
        {
            if (!Child(element, name, out var value)) return;
            if (value.ValueKind != JsonValueKind.String) { Error("invalid_type", "Expected a string.", Path(path, name)); return; }
            var text = value.GetString()!;
            if (text.Length > maximum || !allowEmpty && string.IsNullOrWhiteSpace(text)) Error("invalid_length", $"Use {(allowEmpty ? "0" : "1")} to {maximum} characters.", Path(path, name));
        }
        public void Choice(JsonElement element, string name, string path, string[] choices)
        {
            if (!Child(element, name, out var value)) return;
            if (value.ValueKind != JsonValueKind.String || !choices.Contains(value.GetString(), StringComparer.Ordinal))
                Error("invalid_choice", "Use one of: " + string.Join(", ", choices) + ".", Path(path, name));
        }
        public void Integer(JsonElement element, string name, string path, long minimum, long maximum)
        {
            if (!Child(element, name, out var value)) return;
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var number) || number < minimum || number > maximum) Error("invalid_integer", $"Expected an integer from {minimum} to {maximum}.", Path(path, name));
        }
        public void Number(JsonElement element, string name, string path, double minimum, double maximum)
        {
            if (!Child(element, name, out var value)) return;
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number) || !double.IsFinite(number) || number < minimum || number > maximum) Error("invalid_number", $"Expected a finite number from {minimum} to {maximum}.", Path(path, name));
        }
        public bool Array(JsonElement element, string name, string path, int maximum, out JsonElement array)
        {
            if (!Child(element, name, out array)) return false;
            if (array.ValueKind != JsonValueKind.Array) { Error("invalid_type", "Expected an array.", Path(path, name)); return false; }
            if (array.GetArrayLength() > maximum) { Error("too_many_items", $"At most {maximum} items are allowed.", Path(path, name)); return false; }
            return true;
        }
        public void Timestamp(JsonElement element, string name, string path)
        {
            if (!Child(element, name, out var value)) return;
            var text = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            if (text is null || !text.Contains('T') || !(text.EndsWith('Z') || text.EndsWith("+00:00", StringComparison.Ordinal)) || !value.TryGetDateTimeOffset(out var date) || date.Offset != TimeSpan.Zero)
                Error("invalid_timestamp", "Expected an ISO-8601 UTC timestamp.", Path(path, name));
        }
    }
}
