using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GraphEngineering.Core.Documents;

namespace GraphEngineering.Core.Execution;

public sealed class ExecutionException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed record NodeOutput(string Text, JsonElement? Json);
public sealed record ResolvedPrompt(string Prompt, IReadOnlyDictionary<string, JsonElement> Inputs);

public static partial class BindingResolver
{
    public const int MaximumPromptBytes = 64 * 1024;

    [GeneratedRegex(@"\{\{inputs\.([A-Za-z_][A-Za-z0-9_]*)\}\}", RegexOptions.CultureInvariant)]
    internal static partial Regex PlaceholderPattern();

    [GeneratedRegex(@"\A[A-Za-z_][A-Za-z0-9_]{0,63}\z", RegexOptions.CultureInvariant)]
    internal static partial Regex AliasPattern();

    public static ResolvedPrompt Render(WorkflowNode node, JsonElement input, IReadOnlyDictionary<string, NodeOutput> outputs)
    {
        if (node.Type != "modelCall" || node.TypeVersion != 2)
            throw new ExecutionException("execution_upgrade_required", "Upgrade this Model Call before execution.");
        var config = node.Configuration;
        var prompt = config.GetProperty("prompt").GetString()!;
        var bindings = config.GetProperty("inputBindings");
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (config.GetProperty("promptMode").GetString() == "literal")
        {
            if (bindings.GetArrayLength() != 0)
                throw new ExecutionException("literal_bindings", "Literal prompts cannot use input bindings. Remove the bindings or select bindings mode.");
            CheckPromptSize(prompt);
            return new(prompt, values);
        }
        if (bindings.GetArrayLength() > 32)
            throw new ExecutionException("too_many_bindings", "At most 32 input bindings are permitted per node.");
        foreach (var binding in bindings.EnumerateArray())
        {
            var alias = binding.GetProperty("alias").GetString()!;
            if (!AliasPattern().IsMatch(alias)) throw new ExecutionException("invalid_binding_alias", "Binding aliases must be ASCII identifiers with at most 64 characters.");
            if (!values.TryAdd(alias, Resolve(binding.GetProperty("source"), input, outputs)))
                throw new ExecutionException("duplicate_binding_alias", "Binding aliases must be unique.");
        }

        // Iterate matches in the original prompt once. Inserted values are never reparsed as templates.
        var builder = new StringBuilder(Math.Min(prompt.Length, MaximumPromptBytes));
        var bytes = 0;
        var offset = 0;
        foreach (Match match in PlaceholderPattern().Matches(prompt))
        {
            Append(prompt.AsSpan(offset, match.Index - offset));
            if (!values.TryGetValue(match.Groups[1].Value, out var value))
                throw new ExecutionException("unbound_placeholder", "Every prompt placeholder requires an input binding.");
            Append(value.ValueKind == JsonValueKind.String ? value.GetString() : StrictExecutionJson.Compact(value));
            offset = match.Index + match.Length;
        }
        Append(prompt.AsSpan(offset));
        return new(builder.ToString(), values);

        void Append(ReadOnlySpan<char> part)
        {
            var partBytes = Encoding.UTF8.GetByteCount(part);
            if (partBytes > MaximumPromptBytes - bytes) throw new ExecutionException("prompt_too_large", "The resolved prompt exceeds 64 KiB UTF-8.");
            bytes += partBytes;
            builder.Append(part);
        }
    }

    public static JsonElement Resolve(JsonElement source, JsonElement input, IReadOnlyDictionary<string, NodeOutput> outputs)
    {
        var kind = source.GetProperty("kind").GetString();
        if (kind == "runInput") return SelectPointer(input, source.GetProperty("pointer").GetString()!);
        if (kind is not ("nodeText" or "nodeJson")) throw new ExecutionException("invalid_binding_source", "This binding source is unsupported.");
        var id = source.GetProperty("nodeId").GetString()!;
        if (!outputs.TryGetValue(id, out var output))
            throw new ExecutionException("binding_source_unavailable", "The selected earlier node has no successful output in this run.");
        if (kind == "nodeText") return JsonSerializer.SerializeToElement(output.Text);
        if (output.Json is not { } json)
            throw new ExecutionException("binding_source_not_json", "The selected earlier node has no locally validated JSON object output.");
        return SelectPointer(json, source.GetProperty("pointer").GetString()!);
    }

    public static bool IsValidPointer(string pointer)
    {
        if (pointer.Length == 0) return true;
        if (pointer[0] != '/') return false;
        for (var i = 0; i < pointer.Length; i++)
            if (pointer[i] == '~' && (++i == pointer.Length || pointer[i] is not ('0' or '1'))) return false;
        return true;
    }

    public static JsonElement SelectPointer(JsonElement root, string pointer)
    {
        if (!IsValidPointer(pointer)) throw new ExecutionException("invalid_json_pointer", "Use an RFC 6901 JSON Pointer: empty for root, or slash-separated keys with ~0 and ~1 escapes.");
        var current = root;
        if (pointer.Length == 0) return current.Clone();
        foreach (var segment in pointer[1..].Split('/'))
        {
            var key = segment.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
            if (current.ValueKind == JsonValueKind.Object && current.TryGetProperty(key, out var property)) current = property;
            else if (current.ValueKind == JsonValueKind.Array && IsArrayIndex(key, current.GetArrayLength(), out var index)) current = current[index];
            else throw new ExecutionException("binding_path_missing", "The JSON Pointer does not resolve in this run's selected value.");
        }
        return current.Clone();
    }

    private static bool IsArrayIndex(string value, int length, out int index)
    {
        index = 0;
        if (value.Length == 0 || value.Length > 1 && value[0] == '0') return false;
        foreach (var character in value)
        {
            if (character is < '0' or > '9') return false;
            // No parse overflow, and no allocation proportional to an attacker-supplied index.
            if (index > (int.MaxValue - (character - '0')) / 10) return false;
            index = index * 10 + character - '0';
        }
        return index < length;
    }

    private static void CheckPromptSize(string prompt)
    {
        if (Encoding.UTF8.GetByteCount(prompt) > MaximumPromptBytes)
            throw new ExecutionException("prompt_too_large", "The resolved prompt exceeds 64 KiB UTF-8.");
    }
}
