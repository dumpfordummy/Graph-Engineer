using System.Text;
using System.Text.Json;
using GraphEngineering.Core.Documents;

namespace GraphEngineering.Core.Execution;

public static class StrictExecutionJson
{
    public const int MaximumInputBytes = 16 * 1024;
    public const int MaximumTextBytes = 128 * 1024;

    public static JsonElement ParseInput(ReadOnlyMemory<byte> utf8)
    {
        if (utf8.Length > MaximumInputBytes) throw new ExecutionException("input_too_large", "Run input exceeds 16 KiB UTF-8.");
        return ParseObject(utf8, "invalid_run_input", "Run input must be one valid JSON object with unique keys and depth at most 32.");
    }

    public static JsonElement ParseObjectOutput(string text)
    {
        ValidateText(text);
        return ParseObject(Encoding.UTF8.GetBytes(text), "invalid_json_output", "The response must be one complete JSON object with unique keys and depth at most 32. Markdown, prose and repair are not supported.");
    }

    public static void ValidateText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ExecutionException("empty_output", "The response contained no nonempty assistant text.");
        if (Encoding.UTF8.GetByteCount(text) > MaximumTextBytes) throw new ExecutionException("output_too_large", "Assistant text exceeds 128 KiB UTF-8.");
    }

    public static string Compact(JsonElement value)
    {
        // JsonElement writes original JSON number tokens; no float/decimal conversion loses precision.
        return JsonSerializer.Serialize(value);
    }

    private static JsonElement ParseObject(ReadOnlyMemory<byte> utf8, string code, string message)
    {
        try
        {
            using var document = DocumentReader.ParseJson(utf8);
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new ExecutionException(code, message);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            // Parser exceptions can contain excerpts from untrusted inputs or provider output.
            throw new ExecutionException(code, message);
        }
    }
}
