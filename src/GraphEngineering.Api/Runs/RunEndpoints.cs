using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GraphEngineering.Api.Http;
using GraphEngineering.Api.Persistence;
using GraphEngineering.Api.Providers;
using GraphEngineering.Core.Documents;
using GraphEngineering.Core.Execution;
using Microsoft.EntityFrameworkCore;

namespace GraphEngineering.Api.Runs;

public static class RunEndpoints
{
    public static void MapRunEndpoints(this WebApplication app)
    {
        app.MapGet("/api/workflows/{id:guid}/readiness", async (Guid id, WorkflowDbContext db, CancellationToken token) =>
        {
            var workflow = await db.Workflows.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, token);
            return workflow is null ? Missing() : Results.Ok((await Readiness(db, workflow.Document(), token)).Readiness);
        });
        app.MapPost("/api/workflows/{id:guid}/runs", Submit);
        app.MapGet("/api/runs/submissions/{submissionId:guid}", async (Guid submissionId, WorkflowDbContext db, CancellationToken token) =>
            await db.Runs.AsNoTracking().SingleOrDefaultAsync(x => x.SubmissionId == submissionId, token) is { } run ? Results.Ok(run.Detail()) : Missing());
        app.MapGet("/api/runs/{id:guid}", async (Guid id, WorkflowDbContext db, CancellationToken token) =>
            await db.Runs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, token) is { } run ? Results.Ok(run.Detail()) : Missing());
        app.MapGet("/api/runs", History);
        app.MapGet("/api/runs/{id:guid}/events", Events);
        app.MapGet("/api/runs/{id:guid}/artifacts/{artifactId:guid}", async (Guid id, Guid artifactId, WorkflowDbContext db, CancellationToken token) =>
            await db.RunArtifacts.AsNoTracking().SingleOrDefaultAsync(x => x.RunId == id && x.Id == artifactId, token) is { } artifact ? Results.Ok(artifact.Public()) : Missing());
        app.MapPost("/api/runs/{id:guid}/cancel", Cancel);
    }

    private static async Task<IResult> Submit(Guid id, HttpRequest request, WorkflowDbContext db, RunCoordinator coordinator, ISecretStore secrets, CancellationToken token)
    {
        Guid submissionId; long expectedRevision; JsonElement input;
        try
        {
            using var parsed = await ReadBody(request, 18 * 1024, token);
            var body = parsed.RootElement;
            if (!Exact(body, "submissionId", "expectedRevision", "input") || body.GetProperty("submissionId").ValueKind != JsonValueKind.String || !body.GetProperty("submissionId").TryGetGuid(out submissionId) || submissionId == Guid.Empty ||
                body.GetProperty("expectedRevision").ValueKind != JsonValueKind.Number || !body.GetProperty("expectedRevision").TryGetInt64(out expectedRevision) || expectedRevision < 1 || expectedRevision > DocumentJson.MaximumSafeInteger) throw new JsonException();
            input = StrictExecutionJson.ParseInput(Encoding.UTF8.GetBytes(body.GetProperty("input").GetRawText()));
        }
        catch (ExecutionException error) { return Problem(422, error.Code, "Supply a valid bounded JSON object as run input."); }
        catch (JsonException) { return Problem(400, "invalid_request", "Supply submissionId, expectedRevision and an explicit input object, with no unknown or duplicate properties."); }
        catch (RequestTooLargeException) { return Problem(413, "input_too_large", "Run input is limited to 16 KiB."); }
        var fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{id:D}\n{expectedRevision}\n{Canonical(input)}")));
        Guid? publishedId = null; long publishedSequence = 0;
        await coordinator.Gate.WaitAsync(token);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(token);
            var existing = await db.Runs.SingleOrDefaultAsync(x => x.SubmissionId == submissionId, token);
            if (existing is not null)
                return existing.Fingerprint == fingerprint ? Results.Accepted($"/api/runs/{existing.Id}", existing.Detail()) : Problem(409, "submission_mismatch", "This submission ID already identifies a different request. No new run was created.");
            if (!coordinator.Accepting) return Problem(503, "shutting_down", "The backend is shutting down and is not admitting runs.");
            if (await db.Runs.AnyAsync(x => x.ActiveSlot != null, token)) return Problem(409, "run_busy", "A run is already active. Wait for it to finish or cancel it explicitly.");
            var workflow = await db.Workflows.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, token);
            if (workflow is null) return Missing();
            if (workflow.Revision != expectedRevision) return Problem(409, "revision_changed", "The saved workflow revision changed. Review the current revision before submitting.");
            var document = workflow.Document();
            var ready = await Readiness(db, document, token);
            if (!ready.Readiness.Ready) return Results.Problem(statusCode: 422, title: "Workflow is not ready", detail: "Correct the readiness issues before running.",
                extensions: new Dictionary<string, object?> { ["code"] = "not_ready", ["issues"] = ready.Readiness.Issues });
            try
            {
                foreach (var node in ready.Plan.OrderedNodes)
                {
                    if (node.Type == "modelCall")
                    {
                        if (node.Configuration.GetProperty("promptMode").GetString() == "literal") BindingResolver.Render(node, input, new Dictionary<string, NodeOutput>());
                        foreach (var binding in node.Configuration.GetProperty("inputBindings").EnumerateArray())
                        {
                            var source = binding.GetProperty("source");
                            if (source.GetProperty("kind").GetString() == "runInput") BindingResolver.Resolve(source, input, new Dictionary<string, NodeOutput>());
                        }
                    }
                    if (node.Type == "end" && node.Configuration.GetProperty("resultBinding").GetProperty("kind").GetString() == "runInput")
                        BindingResolver.Resolve(node.Configuration.GetProperty("resultBinding"), input, new Dictionary<string, NodeOutput>());
                }
            }
            catch (ExecutionException error) { return Problem(422, error.Code, "Run input does not satisfy its explicit bindings or a literal prompt exceeds the byte limit."); }
            var snapshotJson = RunJson.Write(document); var inputJson = RunJson.Write(input); var profilesJson = RunJson.Write(ready.Profiles);
            var selectedIds = ready.Profiles.Select(x => x.ProviderProfileId).Distinct().ToArray();
            var selected = await db.ProviderProfiles.AsNoTracking().Include(x => x.Credential).Where(x => selectedIds.Contains(x.Id)).ToListAsync(token);
            foreach (var profile in selected.Where(x => x.AuthMode == "bearer"))
            {
                string key;
                try { key = secrets.Unprotect(profile.Credential!.Ciphertext); }
                catch (SecretStoreException) { return Problem(422, "credential_unreadable", "Windows could not open a selected credential. Replace it before running."); }
                if (RunPrivacy.ContainsCredential(RunJson.Element(snapshotJson), [key]) || RunPrivacy.ContainsCredential(input, [key]) || RunPrivacy.ContainsCredential(RunJson.Element(profilesJson), [key]))
                    return Problem(422, "sensitive_input", "The workflow, input or profile metadata contains a selected credential. Remove it before running.");
            }
            var created = DateTime.UtcNow;
            var lastCreated = await db.Runs.OrderByDescending(x => x.CreatedAtUtc).Select(x => (DateTime?)x.CreatedAtUtc).FirstOrDefaultAsync(token);
            if (lastCreated is not null && created <= lastCreated) created = lastCreated.Value.AddTicks(1);
            var run = new RunRecord { Id = Guid.NewGuid(), SubmissionId = submissionId, Fingerprint = fingerprint, WorkflowId = id,
                WorkflowName = workflow.Name, WorkflowRevision = expectedRevision, CreatedAtUtc = created,
                SnapshotJson = snapshotJson, InputJson = inputJson, ProfilesJson = profilesJson,
                NodesJson = RunJson.Write(ready.Plan.OrderedNodes.Select(node => new RunNode { NodeId = node.Id, Name = node.Name, Type = node.Type }).ToList()) };
            db.Runs.Add(run); RunState.Event(db, run, "run_queued", state: "Queued");
            await db.SaveChangesAsync(token); await transaction.CommitAsync(token);
            publishedId = run.Id; publishedSequence = run.LastSequence;
            return Results.Accepted($"/api/runs/{run.Id}", run.Detail());
        }
        finally
        {
            coordinator.Gate.Release();
            if (publishedId is { } runId) await RunState.Publish(request.HttpContext.RequestServices, runId, publishedSequence);
        }
    }

    private static async Task<IResult> Cancel(Guid id, HttpRequest request, WorkflowDbContext db, RunCoordinator coordinator, CancellationToken token)
    {
        try { using var body = await ReadBody(request, 1024, token); if (!Exact(body.RootElement)) throw new JsonException(); }
        catch (Exception error) when (error is JsonException or RequestTooLargeException) { return Problem(400, "invalid_request", "Cancellation requires an empty JSON object."); }
        long sequence = 0;
        await coordinator.Gate.WaitAsync(token);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(token);
            var run = await db.Runs.SingleOrDefaultAsync(x => x.Id == id, token);
            if (run is null) return Missing();
            if (run.ActiveSlot is null || run.State == "CancelRequested") return Results.Ok(run.Detail());
            if (run.State == "Queued") RunState.Finish(db, run, RunJson.Read<List<RunNode>>(run.NodesJson), "Cancelled", "user_cancelled");
            else { run.State = "CancelRequested"; RunState.Event(db, run, "cancel_requested", state: run.State, message: "user_cancelled"); }
            await db.SaveChangesAsync(token); await transaction.CommitAsync(token);
            sequence = run.LastSequence; coordinator.Cancel(id);
            return Results.Ok(run.Detail());
        }
        finally { coordinator.Gate.Release(); if (sequence > 0) await RunState.Publish(request.HttpContext.RequestServices, id, sequence); }
    }

    private static async Task<IResult> History(HttpRequest request, WorkflowDbContext db, CancellationToken token)
    {
        if (!Limit(request, 20, 100, out var limit)) return Problem(400, "invalid_limit", "Limit must be 1–100.");
        var query = db.Runs.AsNoTracking().AsQueryable();
        if (request.Query.TryGetValue("workflowId", out var rawId))
        { if (!Guid.TryParse(rawId, out var id)) return Problem(400, "invalid_workflow", "Supply a workflow UUID."); query = query.Where(x => x.WorkflowId == id); }
        if (request.Query.TryGetValue("before", out var rawBefore))
        {
            if (!DateTimeOffset.TryParse(rawBefore, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var before)) return Problem(400, "invalid_cursor", "Supply an ISO timestamp cursor.");
            var utc = before.UtcDateTime; query = query.Where(x => x.CreatedAtUtc < utc);
        }
        var records = await query.OrderByDescending(x => x.CreatedAtUtc).Take(limit + 1).ToListAsync(token);
        return Results.Ok(new { items = records.Take(limit).Select(x => x.Summary()), nextCursor = records.Count > limit ? DateTime.SpecifyKind(records[limit - 1].CreatedAtUtc, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture) : null });
    }
    private static async Task<IResult> Events(Guid id, HttpRequest request, WorkflowDbContext db, CancellationToken token)
    {
        if (!Limit(request, 200, 200, out var limit)) return Problem(400, "invalid_limit", "Limit must be 1–200.");
        long after = 0;
        if (request.Query.TryGetValue("after", out var rawAfter) && (!long.TryParse(rawAfter, out after) || after < 0)) return Problem(400, "invalid_cursor", "Supply a nonnegative event sequence.");
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var run = await db.Runs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, token);
        if (run is null) return Missing();
        var events = await db.RunEvents.AsNoTracking().Where(x => x.RunId == id && x.Sequence > after).OrderBy(x => x.Sequence).Take(limit + 1).ToListAsync(token);
        return Results.Ok(new { items = events.Take(limit).Select(x => x.Public()), lastSequence = run.LastSequence, hasMore = events.Count > limit });
    }
    private static bool Limit(HttpRequest request, int fallback, int maximum, out int value)
    { value = fallback; return !request.Query.TryGetValue("limit", out var raw) || int.TryParse(raw, out value) && value >= 1 && value <= maximum; }
    private static async Task<(RunReadiness Readiness, ExecutionPlan Plan, List<RunProfile> Profiles)> Readiness(WorkflowDbContext db, WorkflowDocument document, CancellationToken token)
    {
        var plan = ExecutionPlanner.Validate(document);
        var issues = plan.Issues.ToList(); var profiles = new List<RunProfile>(); var nodes = new List<ReadyNode>();
        foreach (var node in plan.OrderedNodes)
        {
            Guid? profileId = null; Providers.ProviderRecord? profile = null;
            if (node.Type == "modelCall")
            {
                if (node.Configuration.TryGetProperty("providerProfileId", out var reference) && reference.ValueKind == JsonValueKind.String && Guid.TryParse(reference.GetString(), out var parsed))
                { profileId = parsed; profile = await db.ProviderProfiles.AsNoTracking().Include(x => x.Credential).SingleOrDefaultAsync(x => x.Id == parsed, token); }
                if (profile is null) issues.Add(new("provider_missing", "error", "Choose an existing provider profile.", "definition.nodes", node.Id));
                else if (profile.AuthMode == "bearer" && profile.Credential is null) issues.Add(new("credential_missing", "error", "Save a credential for the selected profile.", "definition.nodes", node.Id));
                if (profile is not null) profiles.Add(new(node.Id, profile.Id, profile.Name, profile.ConnectionVersion, profile.Protocol, profile.ModelId, profile.BaseUrl, profile.AuthMode,
                    profile.TimeoutSeconds, profile.MaxOutputTokens, profile.AllowPrivateNetwork, profile.AllowInsecureHttp));
            }
            nodes.Add(new(node.Id, node.Name, node.Type, profileId, profile?.Name, profile?.ModelId, profile?.ConnectionVersion));
        }
        return (new(issues.All(x => x.Severity != "error"), issues, nodes, nodes.Count(x => x.Type == "modelCall")), plan, profiles);
    }
    private static async Task<JsonDocument> ReadBody(HttpRequest request, int maximum, CancellationToken token)
    {
        if (request.ContentLength > maximum) throw new RequestTooLargeException();
        using var buffer = new MemoryStream(); var bytes = new byte[4096];
        while (true) { var count = await request.Body.ReadAsync(bytes, token); if (count == 0) break; if (buffer.Length + count > maximum) throw new RequestTooLargeException(); buffer.Write(bytes, 0, count); }
        return DocumentReader.ParseJson(buffer.ToArray());
    }
    private static bool Exact(JsonElement body, params string[] fields) => body.ValueKind == JsonValueKind.Object && body.EnumerateObject().Count() == fields.Length && body.EnumerateObject().All(x => fields.Contains(x.Name, StringComparer.Ordinal));
    private static string Canonical(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => "{" + string.Join(',', value.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal).Select(x => JsonSerializer.Serialize(x.Name) + ":" + Canonical(x.Value))) + "}",
        JsonValueKind.Array => "[" + string.Join(',', value.EnumerateArray().Select(Canonical)) + "]",
        _ => RunJson.Write(value)
    };
    private static IResult Missing() => Problem(404, "not_found", "The requested workflow, run or artifact does not exist.");
    private static IResult Problem(int status, string code, string message) => Results.Problem(statusCode: status, title: "Run request could not be completed", detail: message, extensions: new Dictionary<string, object?> { ["code"] = code });
    private sealed class RequestTooLargeException : Exception;
}
