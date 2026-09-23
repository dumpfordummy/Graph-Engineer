using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GraphEngineering.Api.Persistence;
using GraphEngineering.Core.Documents;
using GraphEngineering.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GraphEngineering.Api.Tests;

public sealed class WorkflowApiTests
{
    [Fact]
    public async Task FreshDatabaseMigratesAndReportsHealthyEmptyList()
    {
        using var data = new IsolatedData();
        await using var app = new ApiFactory(data.Path);
        using var client = app.CreateClient();
        Assert.Equal("ok", (await client.GetFromJsonAsync<JsonObject>("/api/health"))!["status"]!.GetValue<string>());
        Assert.Empty((await client.GetFromJsonAsync<JsonArray>("/api/workflows"))!);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", db.Database.ProviderName);
        Assert.Equal(System.IO.Path.Combine(data.Path, "workflows.db"), db.Database.GetDbConnection().DataSource);
        Assert.Single(await db.Database.GetAppliedMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.True(File.Exists(System.IO.Path.Combine(data.Path, "workflows.db")));
    }

    [Fact]
    public async Task ConfigurationAndLayoutSurviveUpdateAndDisposedHostRestart()
    {
        using var data = new IsolatedData();
        JsonObject saved;
        await using (var firstHost = new ApiFactory(data.Path))
        {
            using var client = firstHost.CreateClient();
            var original = DocumentFixture.Create();
            var created = await Create(client, original);
            Assert.NotEqual(original["workflow"]!["id"]!.GetValue<string>(), created["workflow"]!["id"]!.GetValue<string>());
            Assert.Equal(1, created["workflow"]!["revision"]!.GetValue<int>());
            var createdAt = created["workflow"]!["createdAt"]!.GetValue<string>();
            created["workflow"]!["name"] = "Edited and restarted";
            created["workflow"]!["createdAt"] = "2000-01-01T00:00:00Z";
            DocumentFixture.Nodes(created)[2]!["configuration"]!["prompt"] = "Preserve this edited prompt.";
            created["layout"]!["nodes"]![2]!["x"] = 811.25;
            var update = await client.PutAsJsonAsync(Url(created), new { expectedRevision = 1, document = created });
            Assert.Equal(HttpStatusCode.OK, update.StatusCode);
            saved = (await update.Content.ReadFromJsonAsync<JsonObject>())!;
            Assert.Equal(2, saved["workflow"]!["revision"]!.GetValue<int>());
            Assert.Equal(createdAt, saved["workflow"]!["createdAt"]!.GetValue<string>());
            Assert.True(JsonNode.DeepEquals(created["definition"], saved["definition"]));
            Assert.True(JsonNode.DeepEquals(created["layout"], saved["layout"]));
        }
        SqliteConnection.ClearAllPools();
        await using var restartedHost = new ApiFactory(data.Path);
        using var restarted = restartedHost.CreateClient();
        var loaded = await restarted.GetFromJsonAsync<JsonObject>(Url(saved));
        Assert.True(JsonNode.DeepEquals(saved, loaded));
        var list = (await restarted.GetFromJsonAsync<JsonArray>("/api/workflows"))!;
        Assert.Single(list);
        Assert.Equal("Edited and restarted", list[0]!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task ConcurrentUpdatesAllowExactlyOneWriterAndPreserveWinner()
    {
        using var data = new IsolatedData();
        await using var app = new ApiFactory(data.Path);
        using var client = app.CreateClient();
        var initial = await Create(client, DocumentFixture.Create());
        var left = initial.DeepClone();
        left["workflow"]!["name"] = "Left writer";
        var right = initial.DeepClone();
        right["workflow"]!["name"] = "Right writer";
        var responses = await Task.WhenAll(
            client.PutAsJsonAsync(Url(initial), new { expectedRevision = 1, document = left }),
            client.PutAsJsonAsync(Url(initial), new { expectedRevision = 1, document = right }));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        var acknowledged = (await responses.Single(response => response.IsSuccessStatusCode).Content.ReadFromJsonAsync<JsonObject>())!;
        var stored = await client.GetFromJsonAsync<JsonObject>(Url(initial));
        Assert.Equal(2, stored!["workflow"]!["revision"]!.GetValue<int>());
        Assert.True(JsonNode.DeepEquals(acknowledged, stored));
        var stale = await client.PutAsJsonAsync(Url(initial), new { expectedRevision = 1, document = initial });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.True(JsonNode.DeepEquals(stored, await client.GetFromJsonAsync<JsonObject>(Url(initial))));
    }

    [Fact]
    public async Task SemanticErrorsAreSaveableAndValidateUsesUnsavedDocument()
    {
        using var data = new IsolatedData();
        await using var app = new ApiFactory(data.Path);
        using var client = app.CreateClient();
        var document = DocumentFixture.Create();
        DocumentFixture.RemoveNode(document, "start");
        DocumentFixture.RemoveNode(document, "end");
        DocumentFixture.Nodes(document)[0]!["configuration"]!["prompt"] = "";
        var saved = await Create(client, document);
        var response = await client.PostAsJsonAsync("/api/workflows/validate", new { document });
        var report = (await response.Content.ReadFromJsonAsync<ValidationReport>(DocumentJson.Options))!;
        Assert.True(report.StructurallyValid);
        Assert.False(report.Valid);
        Assert.Contains(report.Issues, issue => issue.Code == "missing_start");
        Assert.Contains(report.Issues, issue => issue.Code == "missing_end");
        Assert.Contains(report.Issues, issue => issue.Code == "empty_prompt");
        DocumentFixture.Nodes(document)[0]!["configuration"]!["prompt"] = "Fixed only in unsaved document";
        var fresh = await client.PostAsJsonAsync("/api/workflows/validate", new { document });
        var freshReport = (await fresh.Content.ReadFromJsonAsync<ValidationReport>(DocumentJson.Options))!;
        Assert.DoesNotContain(freshReport.Issues, issue => issue.Code == "empty_prompt");
        Assert.Equal("", (await client.GetFromJsonAsync<JsonObject>(Url(saved)))!["definition"]!["nodes"]![0]!["configuration"]!["prompt"]!.GetValue<string>());
    }

    [Fact]
    public async Task StructuralFailureIs422ForSaveAndDiagnosticReportForValidate()
    {
        using var data = new IsolatedData();
        await using var app = new ApiFactory(data.Path);
        using var client = app.CreateClient();
        var document = DocumentFixture.Create();
        DocumentFixture.Edges(document)[0]!["targetPort"] = "wrong";
        var save = await client.PostAsJsonAsync("/api/workflows", new { document });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, save.StatusCode);
        Assert.Equal("application/problem+json", save.Content.Headers.ContentType!.MediaType);
        Assert.NotNull((await save.Content.ReadFromJsonAsync<JsonObject>())!["errors"]!["definition.edges[0].targetPort"]);
        var validate = await client.PostAsJsonAsync("/api/workflows/validate", new { document });
        Assert.Equal(HttpStatusCode.OK, validate.StatusCode);
        Assert.False((await validate.Content.ReadFromJsonAsync<ValidationReport>(DocumentJson.Options))!.StructurallyValid);
        Assert.Empty((await client.GetFromJsonAsync<JsonArray>("/api/workflows"))!);
    }

    [Theory]
    [InlineData("{\"document\":{},\"document\":{}}", 400)]
    [InlineData("{\"document\":", 400)]
    [InlineData("{\"document\":\"\\uD800\"}", 400)]
    [InlineData("{}", 422)]
    [InlineData("{\"document\":null}", 422)]
    [InlineData("{\"document\":{},\"runtime\":{}}", 422)]
    public async Task InvalidBodiesHaveStructuredProblemDetails(string body, int status)
    {
        using var data = new IsolatedData();
        await using var app = new ApiFactory(data.Path);
        using var client = app.CreateClient();
        var response = await client.PostAsync("/api/workflows", new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(status, (int)response.StatusCode);
        var error = (await response.Content.ReadFromJsonAsync<JsonObject>())!;
        Assert.Equal(status, error["status"]!.GetValue<int>());
        Assert.NotNull(error["errors"]);
    }

    [Fact]
    public async Task OversizedPayloadIsRejectedBeforePersistence()
    {
        using var data = new IsolatedData();
        await using var app = new ApiFactory(data.Path);
        using var client = app.CreateClient();
        var document = DocumentFixture.Create();
        DocumentFixture.Nodes(document)[1]!["configuration"]!["prompt"] = new string('x', DocumentJson.MaximumBytes);
        var response = await client.PostAsJsonAsync("/api/workflows", new { document });
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<JsonArray>("/api/workflows"))!);
    }

    [Fact]
    public async Task ImportValidationDoesNotPersistAndCreationAlwaysAssignsNewIdentity()
    {
        using var data = new IsolatedData();
        await using var app = new ApiFactory(data.Path);
        using var client = app.CreateClient();
        var first = await Create(client, DocumentFixture.Create());
        var validation = await client.PostAsJsonAsync("/api/workflows/validate", new { document = first });
        Assert.Equal(HttpStatusCode.OK, validation.StatusCode);
        Assert.Single((await client.GetFromJsonAsync<JsonArray>("/api/workflows"))!);
        var imported = await Create(client, first);
        Assert.NotEqual(first["workflow"]!["id"]!.GetValue<string>(), imported["workflow"]!["id"]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(first["definition"], imported["definition"]));
        Assert.True(JsonNode.DeepEquals(first["layout"], imported["layout"]));
        Assert.True(JsonNode.DeepEquals(first, await client.GetFromJsonAsync<JsonObject>(Url(first))));
    }

    [Fact]
    public async Task IdentityAndRevisionMismatchCannotUpdateAnotherWorkflow()
    {
        using var data = new IsolatedData();
        await using var app = new ApiFactory(data.Path);
        using var client = app.CreateClient();
        var first = await Create(client, DocumentFixture.Create());
        var second = await Create(client, DocumentFixture.Create());
        var wrongIdentity = await client.PutAsJsonAsync(Url(second), new { expectedRevision = 1, document = first });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, wrongIdentity.StatusCode);
        var wrongRevision = await client.PutAsJsonAsync(Url(first), new { expectedRevision = 2, document = first });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, wrongRevision.StatusCode);
        Assert.True(JsonNode.DeepEquals(first, await client.GetFromJsonAsync<JsonObject>(Url(first))));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/workflows/{Guid.NewGuid()}" )).StatusCode);
    }

    [Fact]
    public async Task DatabaseFailureReturnsHonestUnavailableProblem()
    {
        using var data = new IsolatedData();
        await using var app = new ApiFactory(data.Path);
        using var client = app.CreateClient();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
            await db.Database.ExecuteSqlRawAsync("DROP TABLE Workflows");
        }
        using var response = await client.PostAsJsonAsync("/api/workflows", new { document = DocumentFixture.Create() });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("Persistence unavailable", (await response.Content.ReadFromJsonAsync<JsonObject>())!["title"]!.GetValue<string>());
    }

    private static async Task<JsonObject> Create(HttpClient client, JsonObject document)
    {
        var response = await client.PostAsJsonAsync("/api/workflows", new { document });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!;
    }
    private static string Url(JsonObject document) => "/api/workflows/" + document["workflow"]!["id"]!.GetValue<string>();

    private sealed class ApiFactory(string directory) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GRAPH_ENGINEERING_DATA_DIR"] = directory,
                ["Logging:LogLevel:Default"] = "Warning"
            }));
        }
    }

    private sealed class IsolatedData : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "GraphEngineering.Api.Tests", Guid.NewGuid().ToString("N"));
        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}


