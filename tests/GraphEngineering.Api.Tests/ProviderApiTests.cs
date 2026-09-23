using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using GraphEngineering.Api.Persistence;
using GraphEngineering.Api.Providers;
using GraphEngineering.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace GraphEngineering.Api.Tests;

public sealed class ProviderApiTests
{
    private const string Sentinel = "SYNTHETIC_M2_KEY_NOT_A_REAL_CREDENTIAL_53bc";
    internal static JsonObject Write(string url, string? key = Sentinel) => new()
    {
        ["name"] = "Synthetic provider", ["protocol"] = "openai-responses", ["baseUrl"] = url,
        ["modelId"] = "user/exact-model", ["authMode"] = "bearer", ["timeoutSeconds"] = 5,
        ["maxOutputTokens"] = 128, ["allowPrivateNetwork"] = true, ["allowInsecureHttp"] = true,
        ["credential"] = key is null ? new JsonObject { ["action"] = "keep" } : new JsonObject { ["action"] = "replace", ["value"] = key },
        ["confirmDestinationChange"] = false
    };
    internal static async Task<JsonObject> Create(HttpClient client, JsonObject input)
    {
        using var response = await client.PostAsJsonAsync("/api/providers", input);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!;
    }
    internal static string Url(JsonObject profile) => "/api/providers/" + profile["id"]!.GetValue<string>();
    internal static Task<HttpResponseMessage> Test(HttpClient client, JsonObject profile) => client.PostAsJsonAsync(Url(profile) + "/test",
        new { expectedRevision = profile["revision"]!.GetValue<long>(), connectionVersion = profile["connectionVersion"]!.GetValue<long>() });

    [Fact]
    public async Task WindowsDpapiAndProfileRestartKeepReplaceRemoveAreAtomicAndNeverExposeKey()
    {
        Assert.True(OperatingSystem.IsWindows());
        var secrets = new WindowsSecretStore();
        var protectedBytes = secrets.Protect(Sentinel);
        Assert.Equal(Sentinel, secrets.Unprotect(protectedBytes));
        Assert.DoesNotContain(Sentinel, Encoding.UTF8.GetString(protectedBytes));
        Assert.Throws<SecretStoreException>(() => secrets.Unprotect([1, 2, 3]));
        Assert.Throws<SecretStoreException>(() => secrets.Unprotect([]));
        var data = new ProviderTestData();
        JsonObject profile;
        await using (var factory = new LocalApiFactory(data.Path))
        {
            using var client = await factory.CreatePairedClientAsync();
            var input = Write("https://127.0.0.1:60001/custom/v1");
            profile = await Create(client, input);
            Assert.True(profile["hasCredential"]!.GetValue<bool>());
            Assert.DoesNotContain(Sentinel, profile.ToJsonString());
            Assert.Null(profile["credential"]); Assert.Null(profile["ciphertext"]); Assert.Null(profile["credentialReference"]);
            input["credential"] = new JsonObject { ["action"] = "keep" }; input["expectedRevision"] = 1; input["name"] = "Renamed";
            using var rename = await client.PutAsJsonAsync(Url(profile), input);
            Assert.Equal(HttpStatusCode.OK, rename.StatusCode);
            profile = (await rename.Content.ReadFromJsonAsync<JsonObject>())!;
            Assert.Equal(1, profile["connectionVersion"]!.GetValue<int>());
            Assert.True(profile["hasCredential"]!.GetValue<bool>());
            input["credential"] = new JsonObject { ["action"] = "remove" };
            Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(Url(profile), input)).StatusCode);
            input["expectedRevision"] = 2; input["modelId"] = "different-model"; input["credential"] = new JsonObject { ["action"] = "keep" };
            using var modelEdit = await client.PutAsJsonAsync(Url(profile), input);
            Assert.Equal(HttpStatusCode.OK, modelEdit.StatusCode);
            profile = (await modelEdit.Content.ReadFromJsonAsync<JsonObject>())!;
            Assert.Equal(2, profile["connectionVersion"]!.GetValue<int>());
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
            Assert.Equal(Sentinel, secrets.Unprotect((await db.ProviderCredentials.SingleAsync()).Ciphertext));
        }
        SqliteConnection.ClearAllPools();
        await using (var restarted = new LocalApiFactory(data.Path))
        {
            using var client = await restarted.CreatePairedClientAsync();
            Assert.True(JsonNode.DeepEquals(profile, await client.GetFromJsonAsync<JsonObject>(Url(profile))));
            var input = Write("https://127.0.0.1:60001/custom/v1", "SYNTHETIC_REPLACEMENT"); input["expectedRevision"] = 3;
            using var replaced = await client.PutAsJsonAsync(Url(profile), input);
            Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
            profile = (await replaced.Content.ReadFromJsonAsync<JsonObject>())!;
            input["expectedRevision"] = 4; input["credential"] = new JsonObject { ["action"] = "remove" };
            using var removed = await client.PutAsJsonAsync(Url(profile), input);
            Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
            profile = (await removed.Content.ReadFromJsonAsync<JsonObject>())!;
            Assert.False(profile["hasCredential"]!.GetValue<bool>());
            using var test = await Test(client, profile);
            Assert.Equal("credential_missing", (await test.Content.ReadFromJsonAsync<JsonObject>())!["category"]!.GetValue<string>());
            using var delete = new HttpRequestMessage(HttpMethod.Delete, Url(profile)) { Content = JsonContent.Create(new { expectedRevision = 5 }) };
            Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(delete)).StatusCode);
            await using var scope = restarted.Services.CreateAsyncScope();
            Assert.Empty(await scope.ServiceProvider.GetRequiredService<WorkflowDbContext>().ProviderCredentials.ToListAsync());
            Assert.Empty((await client.GetFromJsonAsync<JsonArray>("/api/providers"))!);
        }
        SqliteConnection.ClearAllPools();
        foreach (var path in Directory.GetFiles(data.Path, "workflows.db*"))
        {
            var bytes = await File.ReadAllBytesAsync(path);
            Assert.DoesNotContain(Sentinel, Encoding.UTF8.GetString(bytes));
            Assert.DoesNotContain("SYNTHETIC_REPLACEMENT", Encoding.UTF8.GetString(bytes));
        }
    }

    [Fact]
    public async Task DestinationChangeRequiresConfirmationAndFreshCredentialFailedStoragePreservesOldData()
    {
        var data = new ProviderTestData();
        var store = new SwitchableStore();
        await using var factory = new LocalApiFactory(data.Path, services => { services.RemoveAll<ISecretStore>(); services.AddSingleton<ISecretStore>(store); });
        using var client = await factory.CreatePairedClientAsync();
        var input = Write("https://127.0.0.1:60001/v1"); var original = await Create(client, input);
        input["expectedRevision"] = 1; input["baseUrl"] = "https://127.0.0.1:60002/v1"; input["credential"] = new JsonObject { ["action"] = "keep" };
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PutAsJsonAsync(Url(original), input)).StatusCode);
        input["confirmDestinationChange"] = true;
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PutAsJsonAsync(Url(original), input)).StatusCode);
        input["credential"] = new JsonObject { ["action"] = "replace", ["value"] = "SYNTHETIC_NEW_DESTINATION" };
        store.Fail = true;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PutAsJsonAsync(Url(original), input)).StatusCode);
        Assert.True(JsonNode.DeepEquals(original, await client.GetFromJsonAsync<JsonObject>(Url(original))));
        store.Fail = false;
        using var updated = await client.PutAsJsonAsync(Url(original), input);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.True((await updated.Content.ReadFromJsonAsync<JsonObject>())!["hasCredential"]!.GetValue<bool>());
    }

    [Fact]
    public async Task RealM1DatabaseMigrationPreservesWorkflowsAndRejectsReferencedProfileDeletion()
    {
        var data = new ProviderTestData(); Directory.CreateDirectory(data.Path);
        var options = new DbContextOptionsBuilder<WorkflowDbContext>().UseSqlite($"Data Source={Path.Combine(data.Path, "workflows.db")}").Options;
        var doc = DocumentFixture.Create();
        var read = DocumentFixture.Read(doc).Document!;
        await using (var db = new WorkflowDbContext(options))
        {
            // M1 shipped a short timestamp ID; use EF's own name resolution without rewriting that applied migration.
            var m1Name = db.GetService<IMigrationsIdGenerator>().GetName("202609230001_InitialWorkflows");
            await db.GetService<IMigrator>().MigrateAsync(m1Name);
            db.Workflows.Add(new WorkflowRecord { Id = read.Workflow.Id, Name = "Existing M1", Description = "Preserved", Revision = 3, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow, DefinitionJson = doc["definition"]!.ToJsonString(), LayoutJson = doc["layout"]!.ToJsonString() });
            await db.SaveChangesAsync();
        }
        await using var factory = new LocalApiFactory(data.Path);
        using var client = await factory.CreatePairedClientAsync();
        var retained = (await client.GetFromJsonAsync<JsonObject>("/api/workflows/" + read.Workflow.Id))!;
        Assert.Equal("Existing M1", retained["workflow"]!["name"]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(doc["definition"], retained["definition"]));
        Assert.True(JsonNode.DeepEquals(doc["layout"], retained["layout"]));
        var profile = await Create(client, Write("https://127.0.0.1:60001/v1"));
        DocumentFixture.Nodes(doc)[1]!["configuration"]!["providerProfileId"] = profile["id"]!.GetValue<string>();
        using var saved = await client.PostAsJsonAsync("/api/workflows", new { document = doc }); Assert.Equal(HttpStatusCode.Created, saved.StatusCode);
        var exported = await saved.Content.ReadAsStringAsync(); Assert.DoesNotContain(Sentinel, exported); Assert.DoesNotContain("baseUrl", exported);
        using var delete = new HttpRequestMessage(HttpMethod.Delete, Url(profile)) { Content = JsonContent.Create(new { expectedRevision = 1 }) };
        Assert.Equal(HttpStatusCode.Conflict, (await client.SendAsync(delete)).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); var migrated = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        Assert.Equal(2, (await migrated.Database.GetAppliedMigrationsAsync()).Count());
        Assert.False(migrated.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task CorruptCredentialReportsSanitizedActionableResultAndCanBeReplaced()
    {
        var data = new ProviderTestData(); await using var factory = new LocalApiFactory(data.Path);
        using var client = await factory.CreatePairedClientAsync();
        var input = Write("https://127.0.0.1:60001/v1"); var profile = await Create(client, input);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
            (await db.ProviderCredentials.SingleAsync()).Ciphertext = [1, 2, 3]; await db.SaveChangesAsync();
        }
        using var failed = await Test(client, profile);
        var text = await failed.Content.ReadAsStringAsync(); Assert.DoesNotContain(Sentinel, text);
        Assert.Equal("credential_unreadable", JsonNode.Parse(text)!["category"]!.GetValue<string>());
        input["expectedRevision"] = 1;
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(Url(profile), input)).StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("********")]
    [InlineData("[redacted]")]
    [InlineData("key with spaces")]
    [InlineData("sk-...masked")]
    public async Task BlankAndMaskedCredentialReplacementsAreRejected(string value)
    {
        await using var factory = new LocalApiFactory(new ProviderTestData().Path); using var client = await factory.CreatePairedClientAsync();
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync("/api/providers", Write("https://127.0.0.1:60001/v1", value))).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<JsonArray>("/api/providers"))!);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("unknown")]
    [InlineData("wrongtype")]
    [InlineData("nullkeepvalue")]
    [InlineData("duplicate")]
    public async Task MalformedProviderContractsFailBeforePersistence(string kind)
    {
        await using var factory = new LocalApiFactory(new ProviderTestData().Path); using var client = await factory.CreatePairedClientAsync();
        var input = Write("https://127.0.0.1:60001/v1");
        if (kind == "missing") input.Remove("allowPrivateNetwork");
        if (kind == "unknown") input["extra"] = "not in contract";
        if (kind == "wrongtype") input["credential"]!["action"] = 123;
        if (kind == "nullkeepvalue") input["credential"] = new JsonObject { ["action"] = "keep", ["value"] = null };
        var json = input.ToJsonString();
        if (kind == "duplicate") json = json.Replace("\"name\":", "\"name\":\"Duplicate\",\"name\":", StringComparison.Ordinal);
        using var response = await client.PostAsync("/api/providers", new StringContent(json, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<JsonArray>("/api/providers"))!);
    }

    [Fact]
    public async Task ConcurrentReplacementsHaveExactlyOneWinnerAndItsMetadataMatchesItsCredential()
    {
        await using var factory = new LocalApiFactory(new ProviderTestData().Path); using var client = await factory.CreatePairedClientAsync();
        var profile = await Create(client, Write("https://127.0.0.1:60001/v1"));
        var left = Write("https://127.0.0.1:60001/v1", "SYNTHETIC_LEFT_KEY"); left["name"] = "Left"; left["expectedRevision"] = 1;
        var right = Write("https://127.0.0.1:60001/v1", "SYNTHETIC_RIGHT_KEY"); right["name"] = "Right"; right["expectedRevision"] = 1;
        var replies = await Task.WhenAll(client.PutAsJsonAsync(Url(profile), left), client.PutAsJsonAsync(Url(profile), right));
        Assert.Single(replies, response => response.StatusCode == HttpStatusCode.OK); Assert.Single(replies, response => response.StatusCode == HttpStatusCode.Conflict);
        var winner = (await replies.Single(response => response.IsSuccessStatusCode).Content.ReadFromJsonAsync<JsonObject>())!;
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var stored = await db.ProviderProfiles.Include(x => x.Credential).SingleAsync();
        Assert.Equal(winner["name"]!.GetValue<string>(), stored.Name); Assert.Equal(2, stored.Revision);
        Assert.Equal(stored.Name == "Left" ? "SYNTHETIC_LEFT_KEY" : "SYNTHETIC_RIGHT_KEY", new WindowsSecretStore().Unprotect(stored.Credential!.Ciphertext));
        foreach (var reply in replies) reply.Dispose();
    }

    [Fact]
    public async Task NoAuthRequiresExplicitKeyRemovalAndSuccessfulTestMetadataSurvivesRename()
    {
        await using var fixture = await ProviderFixture.Start();
        await using var factory = new LocalApiFactory(new ProviderTestData().Path); using var client = await factory.CreatePairedClientAsync();
        var input = Write(fixture.BaseUrl); var profile = await Create(client, input);
        input["authMode"] = "none"; input["credential"] = new JsonObject { ["action"] = "keep" }; input["confirmDestinationChange"] = true; input["expectedRevision"] = 1;
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PutAsJsonAsync(Url(profile), input)).StatusCode);
        input["credential"] = new JsonObject { ["action"] = "remove" };
        using var changed = await client.PutAsJsonAsync(Url(profile), input); Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        profile = (await changed.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.False(profile["hasCredential"]!.GetValue<bool>());
        using var tested = await Test(client, profile); Assert.Equal(HttpStatusCode.OK, tested.StatusCode);
        Assert.True((await tested.Content.ReadFromJsonAsync<JsonObject>())!["success"]!.GetValue<bool>());
        var lastTest = (await client.GetFromJsonAsync<JsonObject>(Url(profile)))!["lastTest"];
        Assert.NotNull(lastTest); Assert.Null(Assert.Single(fixture.Requests).Authorization);
        input["credential"] = new JsonObject { ["action"] = "keep" }; input["name"] = "Renamed verified connection"; input["expectedRevision"] = 2;
        using var renamed = await client.PutAsJsonAsync(Url(profile), input); Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.True(JsonNode.DeepEquals(lastTest, (await renamed.Content.ReadFromJsonAsync<JsonObject>())!["lastTest"]));
        Assert.Equal(1, fixture.Count);
    }

    [Fact]
    public async Task ControlSplitCredentialEchoIsRedactedAfterNormalizationInApiAndPersistedDiagnostics()
    {
        const string key = "SYNTHETIC_NORMALIZATION_SENTINEL_991a";
        static string Split(string value, string control) => value.Insert(12, control);
        await using var fixture = await ProviderFixture.Start();
        fixture.Handler = async context =>
        {
            // HTTP obs-text permits this C1 character; JSON permits escaped NUL and CR.
            context.Response.Headers["x-request-id"] = Split(key, "\u0085");
            var response = ProviderFixture.Completed("GE_CONNECTION_OK " + Split(key, "\0\r"));
            response["model"] = Split(key, "\r");
            await context.Response.WriteAsJsonAsync(response);
        };
        await using var factory = new LocalApiFactory(new ProviderTestData().Path);
        using var client = await factory.CreatePairedClientAsync();
        var profile = await Create(client, Write(fixture.BaseUrl, key));
        using var tested = await Test(client, profile);
        Assert.Equal(HttpStatusCode.OK, tested.StatusCode);
        var resultText = await tested.Content.ReadAsStringAsync();
        Assert.DoesNotContain(key, resultText);
        var result = JsonNode.Parse(resultText)!;
        Assert.True(result["success"]!.GetValue<bool>());
        Assert.Equal("GE_CONNECTION_OK [redacted]", result["preview"]!.GetValue<string>());
        Assert.Equal("[redacted]", result["observedModel"]!.GetValue<string>());
        Assert.Equal("[redacted]", result["requestId"]!.GetValue<string>());
        Assert.DoesNotContain(key, await client.GetStringAsync(Url(profile)));
        await using var scope = factory.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<WorkflowDbContext>().ProviderProfiles.SingleAsync();
        Assert.DoesNotContain(key, stored.LastTestJson!);
        Assert.Contains("[redacted]", stored.LastTestJson!);
    }

    [Fact]
    public async Task PartialUsageOmitsUnsupportedCountsFromProbeAndProfileApiResponses()
    {
        await using var fixture = await ProviderFixture.Start();
        fixture.Handler = async context =>
        {
            var response = ProviderFixture.Completed();
            response["usage"] = new JsonObject { ["output_tokens"] = 3 };
            await context.Response.WriteAsJsonAsync(response);
        };
        await using var factory = new LocalApiFactory(new ProviderTestData().Path);
        using var client = await factory.CreatePairedClientAsync();
        var profile = await Create(client, Write(fixture.BaseUrl));
        using var tested = await Test(client, profile);
        Assert.Equal(HttpStatusCode.OK, tested.StatusCode);
        var result = (await tested.Content.ReadFromJsonAsync<JsonObject>())!;
        var usage = result["usage"]!.AsObject();
        Assert.Equal(3, usage["outputTokens"]!.GetValue<int>());
        Assert.False(usage.ContainsKey("inputTokens"));
        Assert.False(usage.ContainsKey("totalTokens"));
        var saved = (await client.GetFromJsonAsync<JsonObject>(Url(profile)))!;
        Assert.True(JsonNode.DeepEquals(usage, saved["lastTest"]!["usage"]));
    }

    private sealed class SwitchableStore : ISecretStore
    {
        private readonly WindowsSecretStore actual = new(); public bool Fail { get; set; }
        public byte[] Protect(string plaintext) => Fail ? throw new SecretStoreException() : actual.Protect(plaintext);
        public string Unprotect(byte[] ciphertext) => actual.Unprotect(ciphertext);
    }
}
