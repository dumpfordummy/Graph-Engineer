using System.Text.Json;
using GraphEngineering.Api.Http;
using GraphEngineering.Api.Persistence;
using GraphEngineering.Api.Runs;
using Microsoft.EntityFrameworkCore;

namespace GraphEngineering.Api.Providers;

public static class ProviderEndpoints
{
    public static IServiceCollection AddProviders(this IServiceCollection services) => services
        .AddSingleton<ISecretStore, WindowsSecretStore>().AddSingleton<ProviderDestinationPolicy>()
        .AddSingleton<ProbeAdmission>().AddSingleton<ResponsesProbe>();

    public static void MapProviderEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/providers");
        group.MapGet("/", async (WorkflowDbContext db, CancellationToken token) =>
            Results.Ok((await db.ProviderProfiles.AsNoTracking().Include(x => x.Credential).OrderBy(x => x.Name).ToListAsync(token)).Select(x => x.Public())));
        group.MapGet("/{id:guid}", async (Guid id, WorkflowDbContext db, CancellationToken token) =>
            await db.ProviderProfiles.AsNoTracking().Include(x => x.Credential).SingleOrDefaultAsync(x => x.Id == id, token) is { } record
                ? Results.Ok(record.Public()) : Missing());
        group.MapPost("/", (HttpRequest request, WorkflowDbContext db, ProviderDestinationPolicy policy, ISecretStore secrets, CancellationToken token) =>
            Write(null, request, db, policy, secrets, token));
        group.MapPut("/{id:guid}", (Guid id, HttpRequest request, WorkflowDbContext db, ProviderDestinationPolicy policy, ISecretStore secrets, CancellationToken token) =>
            Write(id, request, db, policy, secrets, token));
        group.MapDelete("/{id:guid}", Delete);
        group.MapPost("/{id:guid}/test", Test);
    }

    private static async Task<IResult> Write(Guid? id, HttpRequest request, WorkflowDbContext db,
        ProviderDestinationPolicy policy, ISecretStore secrets, CancellationToken token)
    {
        var parsed = await ProviderRequest.Read<ProviderWrite>(request, id.HasValue ? ProviderRequest.UpdateFields : ProviderRequest.CreateFields, token);
        if (parsed.Failure is not null) return parsed.Failure;
        var input = parsed.Value!;
        var errors = Validate(input, id.HasValue);
        Uri? destination = null;
        try { destination = await policy.ValidateForSaveAsync(input.BaseUrl, input.AllowPrivateNetwork, input.AllowInsecureHttp, input.AuthMode, token); }
        catch (DestinationException error) { errors["baseUrl"] = [error.Message]; }
        if (errors.Count != 0) return Invalid(errors);
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var record = id.HasValue ? await db.ProviderProfiles.Include(x => x.Credential).SingleOrDefaultAsync(x => x.Id == id, token) : null;
        if (id.HasValue && record is null) return Missing();
        if (record is not null && record.Revision != input.ExpectedRevision) return Conflict();
        var canonicalBase = destination!.AbsoluteUri.TrimEnd('/');
        var destinationChanged = record is not null && (record.BaseUrl != canonicalBase || record.AuthMode != input.AuthMode);
        if (destinationChanged && (!input.ConfirmDestinationChange || input.AuthMode == "bearer" && input.Credential.Action != "replace"))
            return Invalid(new() { ["confirmDestinationChange"] = ["Confirm the exact destination/authentication change and re-enter a bearer credential before saving."] });
        if (input.AuthMode == "none" && record?.Credential is not null && input.Credential.Action != "remove")
            return Invalid(new() { ["credential.action"] = ["Explicitly remove the saved key when switching to no-auth."] });
        byte[]? encrypted = null;
        if (input.Credential.Action == "replace")
        {
            try { encrypted = secrets.Protect(input.Credential.Value!); }
            catch (SecretStoreException) { return Problems.Create(503, "Credential storage unavailable", "Windows could not protect the credential. No changes were saved. Re-enter it under the current Windows account."); }
        }
        var now = DateTime.UtcNow;
        var isNew = record is null;
        record ??= new ProviderRecord { Id = Guid.NewGuid(), CreatedAtUtc = now, Revision = 0, ConnectionVersion = 0 };
        var connectionChanged = isNew || destinationChanged || record.ModelId != input.ModelId || record.Protocol != input.Protocol ||
            record.TimeoutSeconds != input.TimeoutSeconds || record.MaxOutputTokens != input.MaxOutputTokens ||
            record.AllowPrivateNetwork != input.AllowPrivateNetwork || record.AllowInsecureHttp != input.AllowInsecureHttp || input.Credential.Action != "keep";
        record.Name = input.Name; record.Protocol = input.Protocol; record.BaseUrl = canonicalBase; record.ModelId = input.ModelId;
        record.AuthMode = input.AuthMode; record.TimeoutSeconds = input.TimeoutSeconds; record.MaxOutputTokens = input.MaxOutputTokens;
        record.AllowPrivateNetwork = input.AllowPrivateNetwork; record.AllowInsecureHttp = input.AllowInsecureHttp;
        record.UpdatedAtUtc = now; record.Revision++;
        if (connectionChanged) { record.ConnectionVersion++; record.LastTestJson = null; }
        if (input.Credential.Action == "remove" && record.Credential is not null)
        { db.ProviderCredentials.Remove(record.Credential); record.Credential = null; }
        if (encrypted is not null)
        {
            if (record.Credential is null) record.Credential = new ProviderCredential { ProviderId = record.Id, Ciphertext = encrypted };
            else record.Credential.Ciphertext = encrypted;
        }
        if (isNew) db.ProviderProfiles.Add(record);
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return isNew ? Results.Created($"/api/providers/{record.Id}", record.Public()) : Results.Ok(record.Public());
    }

    private static async Task<IResult> Delete(Guid id, HttpRequest request, WorkflowDbContext db, CancellationToken token)
    {
        var input = await ProviderRequest.Read<DeleteRequest>(request, ["expectedRevision"], token);
        if (input.Failure is not null) return input.Failure;
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var record = await db.ProviderProfiles.SingleOrDefaultAsync(x => x.Id == id, token);
        if (record is null) return Missing();
        if (record.Revision != input.Value!.ExpectedRevision) return Conflict();
        if (await RunState.ActiveProfileReferencedAsync(db, id, token))
            return Problems.Create(409, "Profile is in an active run", "Wait for the admitted run to finish before deleting this profile. Editing the saved workflow does not remove its frozen run reference.");
        var definitions = await db.Workflows.AsNoTracking().Select(x => x.DefinitionJson).ToListAsync(token);
        foreach (var definition in definitions)
        {
            using var json = JsonDocument.Parse(definition);
            if (json.RootElement.GetProperty("nodes").EnumerateArray().Any(node =>
                node.GetProperty("configuration").TryGetProperty("providerProfileId", out var reference) &&
                reference.ValueKind == JsonValueKind.String && Guid.TryParse(reference.GetString(), out var referencedId) && referencedId == id))
                return Problems.Create(409, "Profile is referenced", "A saved workflow references this profile. Change and save those node selections before deleting it.");
        }
        db.ProviderProfiles.Remove(record);
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return Results.NoContent();
    }

    private static async Task<IResult> Test(Guid id, HttpRequest request, WorkflowDbContext db, ISecretStore secrets,
        ResponsesProbe probe, ProbeAdmission admission, CancellationToken token)
    {
        var input = await ProviderRequest.Read<TestRequest>(request, ["expectedRevision", "connectionVersion"], token);
        if (input.Failure is not null) return input.Failure;
        var admitted = admission.Enter(id);
        if (admitted != 0) return Problems.Create(admitted, "Connection test already pending", "Wait for the current connection test to finish. No additional provider request was sent.");
        try
        {
            // A single joined SELECT snapshots the credential and all its approved connection settings together.
            var record = await db.ProviderProfiles.AsNoTracking().Include(x => x.Credential).SingleOrDefaultAsync(x => x.Id == id, token);
            if (record is null) return Missing();
            if (record.Revision != input.Value!.ExpectedRevision || record.ConnectionVersion != input.Value.ConnectionVersion) return Conflict();
            TestResult? result = null;
            string? credential = null;
            if (record.AuthMode == "bearer")
            {
                if (record.Credential is null) result = LocalFailure(record, "credential_missing", "Save a credential before testing this bearer profile.");
                else
                {
                    try { credential = secrets.Unprotect(record.Credential.Ciphertext); }
                    catch (SecretStoreException) { result = LocalFailure(record, "credential_unreadable", "Windows could not open the saved credential. Replace it under the current Windows account."); }
                }
            }
            result ??= await probe.TestAsync(record, credential, token);
            var serialized = JsonSerializer.Serialize(result, ProviderJson.Options);
            await db.ProviderProfiles.Where(x => x.Id == id && x.ConnectionVersion == record.ConnectionVersion)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.LastTestJson, serialized), CancellationToken.None);
            return Results.Ok(result);
        }
        finally { admission.Leave(id); }
    }

    private static TestResult LocalFailure(ProviderRecord record, string category, string message) =>
        new(DateTimeOffset.UtcNow, record.ConnectionVersion, category, false, 0, message, null, false, null, null, null);
    private static IResult Missing() => Problems.Create(404, "Provider profile not found", "No provider profile exists with that ID.");
    private static IResult Conflict() => Problems.Create(409, "Profile changed elsewhere", "Reopen the saved profile before editing or testing. Your changes were not saved and no provider request was sent.");
    private static IResult Invalid(Dictionary<string, string[]> errors) => Results.Problem(statusCode: 422, title: "Invalid provider settings", detail: "Correct the indicated settings.", extensions: new Dictionary<string, object?> { ["errors"] = errors });

    private static Dictionary<string, string[]> Validate(ProviderWrite input, bool update)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 120) errors["name"] = ["Enter a name of 1–120 characters."];
        if (input.Protocol != "openai-responses") errors["protocol"] = ["Only openai-responses is implemented."];
        if (string.IsNullOrWhiteSpace(input.ModelId) || input.ModelId.Length > 200 || input.ModelId.Any(char.IsControl)) errors["modelId"] = ["Enter an exact model ID of 1–200 characters."];
        if (input.AuthMode is not ("bearer" or "none")) errors["authMode"] = ["Choose bearer or none."];
        if (input.TimeoutSeconds is < 5 or > 120) errors["timeoutSeconds"] = ["Timeout must be 5–120 seconds."];
        if (input.MaxOutputTokens is < 16 or > 4096) errors["maxOutputTokens"] = ["Output limit must be 16–4096 tokens."];
        if (update && input.ExpectedRevision is not (>= 1 and < 9007199254740991)) errors["expectedRevision"] = ["Supply a valid saved revision."];
        if (input.Credential is null || input.Credential.Action is not ("keep" or "replace" or "remove")) errors["credential.action"] = ["Choose Keep, Replace, or Remove."];
        else if (input.Credential.Action == "replace")
        {
            var key = input.Credential.Value;
            if (string.IsNullOrWhiteSpace(key) || key.Length > 8192 || key.Any(c => c is < '!' or > '~') ||
                key.Contains("****", StringComparison.Ordinal) || key.Contains("...", StringComparison.Ordinal) ||
                key.Equals("[redacted]", StringComparison.OrdinalIgnoreCase) || input.AuthMode != "bearer")
                errors["credential.value"] = ["Enter an actual printable bearer key, not a blank or masked placeholder."];
        }
        else if (input.Credential.Value is not null) errors["credential.value"] = ["Credential value must be absent for Keep or Remove."];
        return errors;
    }
}

public sealed class ProbeAdmission
{
    private readonly object gate = new();
    private readonly HashSet<Guid> running = [];
    public int Enter(Guid id)
    {
        lock (gate)
        {
            if (running.Contains(id)) return 409;
            if (running.Count >= 2) return 429;
            running.Add(id); return 0;
        }
    }
    public void Leave(Guid id) { lock (gate) running.Remove(id); }
}

internal static class ProviderRequest
{
    internal static readonly string[] CreateFields = ["name", "protocol", "baseUrl", "modelId", "authMode", "timeoutSeconds", "maxOutputTokens", "allowPrivateNetwork", "allowInsecureHttp", "credential", "confirmDestinationChange"];
    internal static readonly string[] UpdateFields = [.. CreateFields, "expectedRevision"];
    internal static async Task<(T? Value, IResult? Failure)> Read<T>(HttpRequest request, string[] fields, CancellationToken token)
    {
        if (!request.HasJsonContentType()) return (default, Problems.Create(415, "JSON required", "Use application/json."));
        const int maximum = 24 * 1024;
        if (request.ContentLength > maximum) return (default, Problems.Create(413, "Payload too large", "Provider requests are limited to 24 KiB."));
        using var buffer = new MemoryStream();
        var bytes = new byte[4096];
        while (true)
        {
            var count = await request.Body.ReadAsync(bytes, token);
            if (count == 0) break;
            if (buffer.Length + count > maximum) return (default, Problems.Create(413, "Payload too large", "Provider requests are limited to 24 KiB."));
            buffer.Write(bytes, 0, count);
        }
        try
        {
            using var json = JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 8 });
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !Exact(root, fields)) throw new JsonException();
            if (root.TryGetProperty("credential", out var credential))
            {
                if (credential.ValueKind != JsonValueKind.Object || !credential.TryGetProperty("action", out var action) || action.ValueKind != JsonValueKind.String) throw new JsonException();
                var expected = credential.TryGetProperty("value", out _) ? new[] { "action", "value" } : ["action"];
                if (!Exact(credential, expected)) throw new JsonException();
                if (credential.GetProperty("action").GetString() is "keep" or "remove" && credential.TryGetProperty("value", out _)) throw new JsonException();
            }
            return (root.Deserialize<T>(ProviderJson.Options) ?? throw new JsonException(), null);
        }
        catch (JsonException) { return (default, Problems.Create(400, "Invalid provider request", "Supply all required fields with the documented JSON types and no unknown or duplicate properties.")); }
    }
    private static bool Exact(JsonElement value, string[] fields)
    {
        var actual = value.EnumerateObject().Select(x => x.Name).ToArray();
        return actual.Length == fields.Length && actual.Distinct(StringComparer.Ordinal).Count() == actual.Length && actual.All(fields.Contains);
    }
}
