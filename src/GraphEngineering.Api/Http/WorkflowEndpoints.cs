using System.Text.Json;
using GraphEngineering.Api.Persistence;
using GraphEngineering.Core.Documents;
using GraphEngineering.Core.Validation;
using Microsoft.EntityFrameworkCore;

namespace GraphEngineering.Api.Http;

internal static class WorkflowEndpoints
{
    public static void MapWorkflowEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api");
        api.MapGet("/health", async (WorkflowDbContext db, CancellationToken token) =>
            await db.Database.CanConnectAsync(token) ? Results.Ok(new { status = "ok" }) : Problems.Create(503, "Persistence unavailable", "Cannot connect to the workflow database."));
        api.MapGet("/workflows", async (WorkflowDbContext db, CancellationToken token) =>
        {
            var records = await db.Workflows.AsNoTracking().OrderByDescending(record => record.UpdatedAtUtc).ToListAsync(token);
            return Results.Ok(records.Select(record => record.Metadata()));
        });
        api.MapGet("/workflows/{id:guid}", async (Guid id, WorkflowDbContext db, CancellationToken token) =>
        {
            var record = await db.Workflows.AsNoTracking().SingleOrDefaultAsync(record => record.Id == id, token);
            return record is null ? Missing() : Results.Ok(record.Document());
        });
        api.MapPost("/workflows/validate", async (HttpRequest request, CancellationToken token) =>
        {
            var input = await DocumentRequest.Read(request, false, token);
            return input.Failure ?? Results.Ok(GraphValidator.Validate(input.Parsed!));
        });
        api.MapPost("/workflows", async (HttpRequest request, WorkflowDbContext db, CancellationToken token) =>
        {
            var input = await DocumentRequest.Read(request, false, token);
            if (input.Failure is not null) return input.Failure;
            if (!input.Parsed!.Success) return Problems.Invalid(input.Parsed.Issues);
            var document = input.Parsed.Document!;
            var now = DateTime.UtcNow;
            var record = new WorkflowRecord
            {
                Id = Guid.NewGuid(), Name = document.Workflow.Name, Description = document.Workflow.Description,
                Revision = 1, CreatedAtUtc = now, UpdatedAtUtc = now,
                DefinitionJson = JsonSerializer.Serialize(document.Definition, DocumentJson.Options),
                LayoutJson = JsonSerializer.Serialize(document.Layout, DocumentJson.Options)
            };
            db.Workflows.Add(record);
            await db.SaveChangesAsync(token);
            return Results.Created($"/api/workflows/{record.Id}", record.Document());
        });
        api.MapPut("/workflows/{id:guid}", async (Guid id, HttpRequest request, WorkflowDbContext db, CancellationToken token) =>
        {
            var input = await DocumentRequest.Read(request, true, token);
            if (input.Failure is not null) return input.Failure;
            if (!input.Parsed!.Success) return Problems.Invalid(input.Parsed.Issues);
            var document = input.Parsed.Document!;
            var issues = new List<ValidationIssue>();
            if (id != document.Workflow.Id) issues.Add(new("identity_mismatch", "error", "The document ID must match the route ID.", "workflow.id"));
            if (document.Workflow.Revision != input.ExpectedRevision) issues.Add(new("revision_mismatch", "error", "The document revision must equal expectedRevision.", "workflow.revision"));
            if (issues.Count > 0) return Problems.Invalid(issues);
            if (input.ExpectedRevision == DocumentJson.MaximumSafeInteger) return Problems.Create(409, "Revision limit reached", "Export this draft and import it as a new workflow.");
            var definitionJson = JsonSerializer.Serialize(document.Definition, DocumentJson.Options);
            var layoutJson = JsonSerializer.Serialize(document.Layout, DocumentJson.Options);
            var updatedAt = DateTime.UtcNow;
            await using var transaction = await db.Database.BeginTransactionAsync(token);
            // The predicate and increment execute as one SQLite UPDATE, so concurrent clients cannot both win.
            var changed = await db.Workflows.Where(record => record.Id == id && record.Revision == input.ExpectedRevision)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(record => record.Name, document.Workflow.Name)
                    .SetProperty(record => record.Description, document.Workflow.Description)
                    .SetProperty(record => record.DefinitionJson, definitionJson)
                    .SetProperty(record => record.LayoutJson, layoutJson)
                    .SetProperty(record => record.UpdatedAtUtc, updatedAt)
                    .SetProperty(record => record.Revision, record => record.Revision + 1), token);
            if (changed == 0)
            {
                if (!await db.Workflows.AnyAsync(record => record.Id == id, token)) return Missing();
                return Problems.Create(409, "Draft changed elsewhere", "The stored revision has changed. Your edits were not saved. Reopen the latest version or export your draft before resolving the conflict.");
            }
            var saved = await db.Workflows.AsNoTracking().SingleAsync(record => record.Id == id, token);
            await transaction.CommitAsync(token);
            return Results.Ok(saved.Document());
        });
    }

    private static IResult Missing() => Problems.Create(404, "Workflow not found", "No saved workflow exists with that ID.");
}
