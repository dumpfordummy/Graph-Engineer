using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using GraphEngineering.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Xunit;

namespace GraphEngineering.Api.Tests;

/// <summary>Starts real isolated API processes. Termination uses only the retained handle returned by Process.Start.</summary>
public sealed class ProcessExecutionTests
{
    [Fact]
    public async Task CompletedTwoNodeHistorySurvivesRealProcessReplacementWithoutProviderReplay()
    {
        await using var provider = await ProviderFixture.Start();
        provider.Handler = context => context.Response.WriteAsJsonAsync(ProviderFixture.Completed(provider.Count == 1 ? "{\"varX\":14}" : "{\"varY\":140}"));
        await using var api = await IsolatedApi.Start();
        using var client = await api.PairedClient();
        var workflow = await CreateWorkflow(client, provider.BaseUrl, twoModels: true);
        var accepted = await Submit(client, workflow);
        var completed = await WaitForState(client, accepted, "Succeeded");
        Assert.Equal(2, provider.Count);
        Assert.Equal(140, completed["result"]!["varY"]!.GetValue<int>());
        Assert.Equal("First 7", provider.Requests[0].Body["input"]!.GetValue<string>());
        Assert.Equal("Second 14", provider.Requests[1].Body["input"]!.GetValue<string>());
        Assert.NotEqual(provider.Requests[0].Authorization, provider.Requests[1].Authorization);
        var eventsBefore = await client.GetFromJsonAsync<JsonObject>(RunUrl(accepted) + "/events");
        var snapshotBefore = completed["snapshot"]!.DeepClone();

        await api.KillOwnedProcess();
        await api.Restart();
        // The old cookie is bound to the previous launch, even though its protected key is durable.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(RunUrl(accepted))).StatusCode);
        using var pairedAgain = await api.PairedClient();
        var reopened = await pairedAgain.GetFromJsonAsync<JsonObject>(RunUrl(accepted));
        Assert.Equal("Succeeded", reopened!["state"]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(completed, reopened));
        Assert.True(JsonNode.DeepEquals(snapshotBefore, reopened["snapshot"]));
        Assert.True(JsonNode.DeepEquals(eventsBefore, await pairedAgain.GetFromJsonAsync<JsonObject>(RunUrl(accepted) + "/events")));
        foreach (var node in reopened["nodes"]!.AsArray())
            foreach (var artifactId in node!["artifactIds"]!.AsArray())
                Assert.Equal(HttpStatusCode.OK, (await pairedAgain.GetAsync(RunUrl(accepted) + "/artifacts/" + artifactId!.GetValue<string>())).StatusCode);
        Assert.Equal(2, provider.Count);
        Assert.DoesNotContain("SYNTHETIC_M3_PROCESS", reopened.ToJsonString());
    }

    [Fact]
    public async Task CrashAfterProviderObservedRequestInterruptsAndNeverRepeatsOrCallsDownstream()
    {
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var provider = await ProviderFixture.Start();
        provider.Handler = async context =>
        {
            observed.TrySetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(60));
            try { await context.Response.WriteAsJsonAsync(ProviderFixture.Completed("{\"varX\":14}")); }
            catch (OperationCanceledException) { }
        };
        await using var api = await IsolatedApi.Start();
        try
        {
            using var client = await api.PairedClient();
            var workflow = await CreateWorkflow(client, provider.BaseUrl, twoModels: true);
            var accepted = await Submit(client, workflow);
            await observed.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Equal(1, provider.Count);
            var inFlight = await client.GetFromJsonAsync<JsonObject>(RunUrl(accepted));
            Assert.Equal("Running", inFlight!["state"]!.GetValue<string>());
            Assert.NotNull(Node(inFlight, "model-1")["attempt"]!["dispatchIntentAt"]);
            Assert.Null(Node(inFlight, "model-1")["attempt"]!["outputText"]);
            var initialSequence = inFlight["lastSequence"]!.GetValue<long>();

            await api.KillOwnedProcess();
            release.TrySetResult();
            await api.Restart();
            using var pairedAgain = await api.PairedClient();
            var interrupted = await WaitForState(pairedAgain, accepted, "Interrupted");
            Assert.Equal("backend_restarted", interrupted["failureCode"]!.GetValue<string>());
            Assert.True(interrupted["lastSequence"]!.GetValue<long>() > initialSequence);
            Assert.Equal("Succeeded", Node(interrupted, "start")["state"]!.GetValue<string>());
            Assert.Equal("Interrupted", Node(interrupted, "model-1")["state"]!.GetValue<string>());
            Assert.Equal("Unknown", Node(interrupted, "model-1")["attempt"]!["externalOutcome"]!.GetValue<string>());
            Assert.Equal("Skipped", Node(interrupted, "model-2")["state"]!.GetValue<string>());
            Assert.Null(Node(interrupted, "model-2")["attempt"]);
            Assert.Null(interrupted["result"]);
            Assert.Equal(1, provider.Count);
            var history = await pairedAgain.GetFromJsonAsync<JsonObject>("/api/runs");
            Assert.Single(history!["items"]!.AsArray());
            Assert.Equal(1, provider.Count);
        }
        finally { release.TrySetResult(); }
    }

    [Fact]
    public async Task CrashAfterProviderCompletionButBeforeLocalCommitRetainsIntentAndNeverReplays()
    {
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var responseCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var provider = await ProviderFixture.Start();
        provider.Handler = async context =>
        {
            observed.TrySetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(60));
            await context.Response.WriteAsJsonAsync(ProviderFixture.Completed("{\"varX\":14}"));
            await context.Response.CompleteAsync();
            responseCompleted.TrySetResult();
        };
        await using var api = await IsolatedApi.Start();
        try
        {
            using var client = await api.PairedClient();
            var workflow = await CreateWorkflow(client, provider.BaseUrl, twoModels: true);
            var accepted = await Submit(client, workflow);
            await observed.Task.WaitAsync(TimeSpan.FromSeconds(20));
            // SQLite fault injection exists only in this fixture. It blocks both the success transaction
            // and the worker's conservative failure write after dispatch intent is already durable.
            await ExecuteSql(api.DataDirectory, """
                CREATE TRIGGER synthetic_fail_run_persistence BEFORE UPDATE ON Runs
                BEGIN SELECT RAISE(ABORT, 'synthetic post-response persistence failure'); END;
                """);
            release.TrySetResult();
            await responseCompleted.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await api.WaitForLog("Startup recovery will reconcile the retained dispatch intent.");
            var retained = await client.GetFromJsonAsync<JsonObject>(RunUrl(accepted));
            Assert.Equal("Running", retained!["state"]!.GetValue<string>());
            Assert.NotNull(Node(retained, "model-1")["attempt"]!["dispatchIntentAt"]);
            Assert.Null(Node(retained, "model-1")["attempt"]!["outputText"]);
            Assert.Equal(1, provider.Count);
            Assert.Equal(0L, await Scalar(api.DataDirectory, "SELECT COUNT(*) FROM RunArtifacts WHERE Kind = 'output_text';"));

            await api.KillOwnedProcess();
            await ExecuteSql(api.DataDirectory, "DROP TRIGGER synthetic_fail_run_persistence;");
            await api.Restart();
            using var pairedAgain = await api.PairedClient();
            var recovered = await WaitForState(pairedAgain, accepted, "Interrupted");
            Assert.Equal("backend_restarted", recovered["failureCode"]!.GetValue<string>());
            Assert.Equal("Unknown", Node(recovered, "model-1")["attempt"]!["externalOutcome"]!.GetValue<string>());
            Assert.Equal("Skipped", Node(recovered, "model-2")["state"]!.GetValue<string>());
            Assert.Null(Node(recovered, "model-1")["attempt"]!["outputText"]);
            Assert.Equal(0L, await Scalar(api.DataDirectory, "SELECT COUNT(*) FROM RunArtifacts WHERE Kind = 'output_text';"));
            Assert.Equal(1, provider.Count);
        }
        finally { release.TrySetResult(); }
    }

    [Fact]
    public async Task SecondProcessCannotRotatePairingOrReconcileOwnersRunAndDifferentDirectoryIsIndependent()
    {
        var observed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var provider = await ProviderFixture.Start();
        provider.Handler = async context =>
        {
            observed.TrySetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(60));
            await context.Response.WriteAsJsonAsync(ProviderFixture.Completed("Synthetic ownership result"));
        };
        await using var owner = await IsolatedApi.Start();
        try
        {
            using var client = await owner.PairedClient();
            var workflow = await CreateWorkflow(client, provider.BaseUrl, twoModels: false);
            var accepted = await Submit(client, workflow);
            await observed.Task.WaitAsync(TimeSpan.FromSeconds(20));
            var tokenBefore = await File.ReadAllTextAsync(owner.TokenPath);
            var stateBefore = await client.GetFromJsonAsync<JsonObject>(RunUrl(accepted));

            await using var competing = await IsolatedApi.Start(owner.DataDirectory, expectStartup: false);
            await competing.WaitForExit();
            Assert.NotEqual(0, competing.ExitCode);
            Assert.Contains("owns this data directory", competing.Logs, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(tokenBefore, await File.ReadAllTextAsync(owner.TokenPath));
            var stillOwned = await client.GetFromJsonAsync<JsonObject>(RunUrl(accepted));
            Assert.Equal("Running", stillOwned!["state"]!.GetValue<string>());
            Assert.Equal(stateBefore!["lastSequence"]!.GetValue<long>(), stillOwned["lastSequence"]!.GetValue<long>());
            Assert.Equal(1, provider.Count);

            await using var independent = await IsolatedApi.Start();
            using var independentClient = await independent.PairedClient();
            Assert.Empty((await independentClient.GetFromJsonAsync<JsonObject>("/api/runs"))!["items"]!.AsArray());
            Assert.Equal(HttpStatusCode.NotFound, (await independentClient.GetAsync(RunUrl(accepted))).StatusCode);
            Assert.Equal("Running", (await client.GetFromJsonAsync<JsonObject>(RunUrl(accepted)))!["state"]!.GetValue<string>());
            release.TrySetResult();
            var completed = await WaitForState(client, accepted, "Succeeded");
            Assert.Equal("Synthetic ownership result", completed["result"]!.GetValue<string>());
            Assert.Equal(1, provider.Count);
        }
        finally { release.TrySetResult(); }
    }

    private static async Task<JsonObject> CreateWorkflow(HttpClient client, string url, bool twoModels)
    {
        var profileInput = ProviderApiTests.Write(url, "SYNTHETIC_M3_PROCESS_FIRST_KEY");
        profileInput["timeoutSeconds"] = 60;
        var first = await ProviderApiTests.Create(client, profileInput);
        var second = first;
        if (twoModels)
        {
            profileInput = ProviderApiTests.Write(url, "SYNTHETIC_M3_PROCESS_SECOND_KEY");
            profileInput["timeoutSeconds"] = 60;
            profileInput["name"] = "Second synthetic process provider";
            second = await ProviderApiTests.Create(client, profileInput);
        }
        var graph = DocumentFixture.Create();
        if (!twoModels)
        {
            DocumentFixture.RemoveNode(graph, "model-2");
            DocumentFixture.AddEdge(graph, "to-end", "model-1", "end");
        }
        foreach (var node in DocumentFixture.Nodes(graph))
        {
            var type = node!["type"]!.GetValue<string>();
            if (type == "start") continue;
            node["typeVersion"] = 2;
            var configuration = node["configuration"]!.AsObject();
            if (type == "end")
            {
                configuration["resultBinding"] = new JsonObject { ["kind"] = twoModels ? "nodeJson" : "nodeText", ["nodeId"] = twoModels ? "model-2" : "model-1" };
                if (twoModels) configuration["resultBinding"]!["pointer"] = "";
                continue;
            }
            var isFirst = node["id"]!.GetValue<string>() == "model-1";
            configuration["providerProfileId"] = (isFirst ? first : second)["id"]!.GetValue<string>();
            configuration["promptMode"] = twoModels ? "bindings" : "literal";
            configuration["outputMode"] = twoModels ? "jsonObject" : "text";
            configuration["prompt"] = twoModels ? isFirst ? "First {{inputs.x}}" : "Second {{inputs.value}}" : "Return the synthetic fixture text.";
            configuration["inputBindings"] = new JsonArray();
            if (twoModels)
            {
                var source = isFirst
                    ? new JsonObject { ["kind"] = "runInput", ["pointer"] = "/x" }
                    : new JsonObject { ["kind"] = "nodeJson", ["nodeId"] = "model-1", ["pointer"] = "/varX" };
                configuration["inputBindings"]!.AsArray().Add(new JsonObject { ["alias"] = isFirst ? "x" : "value", ["source"] = source });
            }
        }
        using var saved = await client.PostAsJsonAsync("/api/workflows", new { document = graph });
        Assert.Equal(HttpStatusCode.Created, saved.StatusCode);
        return (await saved.Content.ReadFromJsonAsync<JsonObject>())!;
    }

    private static async Task<JsonObject> Submit(HttpClient client, JsonObject workflow)
    {
        using var response = await client.PostAsJsonAsync("/api/workflows/" + workflow["workflow"]!["id"]!.GetValue<string>() + "/runs", new
        { submissionId = Guid.NewGuid(), expectedRevision = workflow["workflow"]!["revision"]!.GetValue<long>(), input = new { x = 7, unrelated = "must not be forwarded" } });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!;
    }
    private static string RunUrl(JsonObject run) => "/api/runs/" + run["id"]!.GetValue<string>();
    private static JsonObject Node(JsonObject run, string id) => run["nodes"]!.AsArray().Single(node => node!["nodeId"]!.GetValue<string>() == id)!.AsObject();
    private static async Task<JsonObject> WaitForState(HttpClient client, JsonObject accepted, string state)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        JsonObject? last = null;
        while (!timeout.IsCancellationRequested)
        {
            last = await client.GetFromJsonAsync<JsonObject>(RunUrl(accepted), timeout.Token);
            if (last!["state"]!.GetValue<string>() == state) return last;
            Assert.DoesNotContain(last["state"]!.GetValue<string>(), new[] { "Succeeded", "Failed", "Cancelled", "Interrupted" });
            await Task.Delay(40, timeout.Token);
        }
        throw new TimeoutException($"Expected {state}; last state was {last?["state"]}.");
    }
    private static async Task ExecuteSql(string directory, string sql)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, "workflows.db"), Pooling = false }.ToString());
        await connection.OpenAsync();
        using var command = connection.CreateCommand(); command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
    private static async Task<long> Scalar(string directory, string sql)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, "workflows.db"), Pooling = false }.ToString());
        await connection.OpenAsync();
        using var command = connection.CreateCommand(); command.CommandText = sql;
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private sealed class IsolatedApi : IAsyncDisposable
    {
        private readonly string deployment;
        private readonly int port;
        private readonly ConcurrentQueue<string> logs = new();
        private Process? owned;
        private readonly string logFile;
        public string DataDirectory { get; }
        public string TokenPath => Path.Combine(DataDirectory, "runtime", "pairing-token.txt");
        public string Logs => string.Join(Environment.NewLine, logs);
        public int ExitCode => owned?.ExitCode ?? throw new InvalidOperationException("No owned process.");
        private string Url => "http://127.0.0.1:" + port;
        private IsolatedApi(string? directory)
        {
            var root = FindRoot();
            var fixture = Path.Combine(root, ".artifacts", "m3", "process", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(fixture);
            DataDirectory = directory ?? Path.Combine(fixture, "data");
            deployment = Path.Combine(fixture, "host");
            logFile = Path.Combine(fixture, "backend.log");
            // Copies permit the integration owner to rebuild while this test-owned host runs.
            var source = Path.GetDirectoryName(typeof(Program).Assembly.Location)!;
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var destination = Path.Combine(deployment, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination);
            }
            using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            port = ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        public static async Task<IsolatedApi> Start(string? directory = null, bool expectStartup = true)
        {
            var api = new IsolatedApi(directory);
            try { await api.Launch(expectStartup); return api; }
            catch { await api.DisposeAsync(); throw; }
        }
        public async Task Restart()
        {
            Assert.True(owned is null || owned.HasExited);
            owned?.Dispose(); owned = null;
            await Launch(expectStartup: true);
        }
        private async Task Launch(bool expectStartup)
        {
            var start = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = deployment
            };
            start.ArgumentList.Add(Path.Combine(deployment, "GraphEngineering.Api.dll"));
            start.ArgumentList.Add("--urls"); start.ArgumentList.Add(Url);
            start.Environment["GRAPH_ENGINEERING_DATA_DIR"] = DataDirectory;
            start.Environment["GRAPH_ENGINEERING_BROWSER_ORIGIN"] = Url;
            start.Environment["ASPNETCORE_ENVIRONMENT"] = "Testing";
            start.Environment["Logging__LogLevel__Default"] = "Warning";
            owned = Process.Start(start) ?? throw new InvalidOperationException("Could not start the isolated API.");
            // Force acquisition while still referring to the exact Process.Start result.
            _ = owned.SafeHandle;
            owned.OutputDataReceived += (_, args) => { if (args.Data is { } line) logs.Enqueue(line); };
            owned.ErrorDataReceived += (_, args) => { if (args.Data is { } line) logs.Enqueue(line); };
            owned.BeginOutputReadLine(); owned.BeginErrorReadLine();
            if (!expectStartup) return;
            using var client = new HttpClient { BaseAddress = new Uri(Url), Timeout = TimeSpan.FromSeconds(2) };
            var deadline = Stopwatch.StartNew();
            while (deadline.Elapsed < TimeSpan.FromSeconds(25))
            {
                Assert.False(owned.HasExited, "The test-owned API exited before health was ready: " + Logs);
                try { if ((await client.GetAsync("/api/health")).IsSuccessStatusCode) return; }
                catch (HttpRequestException) { }
                catch (TaskCanceledException) { }
                await Task.Delay(50);
            }
            throw new TimeoutException("The isolated backend never became ready: " + Logs);
        }
        public async Task<HttpClient> PairedClient()
        {
            var handler = new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false };
            var client = new HttpClient(handler) { BaseAddress = new Uri(Url), Timeout = TimeSpan.FromSeconds(20) };
            client.DefaultRequestHeaders.Add("Origin", Url);
            var token = await File.ReadAllTextAsync(TokenPath);
            using var paired = await client.PostAsJsonAsync("/api/session/pair", new { token });
            Assert.Equal(HttpStatusCode.OK, paired.StatusCode);
            var session = await client.GetFromJsonAsync<JsonObject>("/api/session");
            client.DefaultRequestHeaders.Add("X-GE-CSRF", session!["csrfToken"]!.GetValue<string>());
            return client;
        }
        public async Task WaitForLog(string text)
        {
            var deadline = Stopwatch.StartNew();
            while (!logs.Any(line => line.Contains(text, StringComparison.Ordinal)))
            {
                Assert.False(owned!.HasExited, "Owned process unexpectedly exited: " + Logs);
                if (deadline.Elapsed > TimeSpan.FromSeconds(20)) throw new TimeoutException("Expected safe diagnostic not observed: " + Logs);
                await Task.Delay(40);
            }
        }
        public async Task WaitForExit()
        {
            await owned!.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            owned.WaitForExit();
        }
        public async Task KillOwnedProcess()
        {
            if (owned is null || owned.HasExited) return;
            // No name lookup, process-tree traversal, or ancestry inference. API runtime spawns no child processes.
            owned.Kill(entireProcessTree: false);
            await owned.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            owned.WaitForExit();
        }
        public async ValueTask DisposeAsync()
        {
            await KillOwnedProcess();
            owned?.Dispose(); owned = null;
            await File.WriteAllTextAsync(logFile, Logs);
        }
        private static string FindRoot()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "src", "GraphEngineering.Api", "GraphEngineering.Api.csproj"))) return directory.FullName;
            throw new InvalidOperationException("Repository root not found from test assembly.");
        }
    }
}
