using System.Text.Json;
using GraphEngineering.Core.Documents;

namespace GraphEngineering.Api.Http;

internal sealed record DocumentRequest(DocumentReadResult? Parsed, long? ExpectedRevision, IResult? Failure)
{
    public static async Task<DocumentRequest> Read(HttpRequest request, bool update, CancellationToken cancellationToken)
    {
        if (request.ContentLength > DocumentJson.MaximumBytes) return Failed(413, "Payload too large", "The maximum request size is 1 MiB.");
        if (!request.HasJsonContentType()) return Failed(415, "JSON required", "Use application/json for document requests.");
        using var stream = new MemoryStream();
        var buffer = new byte[16_384];
        while (true)
        {
            var count = await request.Body.ReadAsync(buffer, cancellationToken);
            if (count == 0) break;
            if (stream.Length + count > DocumentJson.MaximumBytes) return Failed(413, "Payload too large", "The maximum request size is 1 MiB.");
            stream.Write(buffer, 0, count);
        }
        try
        {
            using var json = DocumentReader.ParseJson(stream.ToArray());
            var shape = new DocumentReader.ShapeReader();
            shape.Object(json.RootElement, "", update ? ["expectedRevision", "document"] : ["document"]);
            if (update) shape.Integer(json.RootElement, "expectedRevision", "", 1, DocumentJson.MaximumSafeInteger);
            DocumentReadResult? parsed = null;
            if (shape.Child(json.RootElement, "document", out var document)) parsed = DocumentReader.Read(document);
            if (shape.Issues.Count > 0) return new(new(null, shape.Issues), null, null);
            var revision = update ? json.RootElement.GetProperty("expectedRevision").GetInt64() : (long?)null;
            return new(parsed, revision, null);
        }
        catch (JsonException exception)
        {
            return new(null, null, Problems.Create(400, "Invalid JSON", "The body must be valid JSON with no duplicate property names.",
                [new("invalid_json", "error", "Invalid JSON or duplicate property name.", exception.Path ?? "$")]));
        }
    }

    private static DocumentRequest Failed(int status, string title, string detail) => new(null, null, Problems.Create(status, title, detail));
}

internal static class Problems
{
    public static IResult Create(int status, string title, string detail, IReadOnlyList<ValidationIssue>? issues = null)
    {
        IDictionary<string, object?>? extensions = issues is null ? null : new Dictionary<string, object?>
        {
            ["errors"] = issues.GroupBy(issue => issue.Path).ToDictionary(group => group.Key, group => group.Select(issue => issue.Message).ToArray())
        };
        return Results.Problem(statusCode: status, title: title, detail: detail, type: "about:blank", extensions: extensions);
    }
    public static IResult Invalid(IReadOnlyList<ValidationIssue> issues) => Create(422, "Invalid document", "Correct the structural errors before saving this draft.", issues);
}
