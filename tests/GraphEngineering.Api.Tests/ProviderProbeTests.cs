using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using GraphEngineering.Api.Providers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace GraphEngineering.Api.Tests;

public sealed class ProviderProbeTests
{
    private static ProviderRecord Record(string url) => new()
    {
        Id = Guid.NewGuid(), BaseUrl = url, ModelId = "user/exact-model", AuthMode = "bearer", AllowPrivateNetwork = true,
        AllowInsecureHttp = true, TimeoutSeconds = 5, MaxOutputTokens = 128, ConnectionVersion = 7
    };
    private static ResponsesProbe Probe() => new(new ProviderDestinationPolicy(new ConfigurationBuilder().Build()));

    [Fact]
    public async Task TypedResponsesWireContractPrefixesPerRequestAuthAndRedactionAreVerifiedWithoutRetries()
    {
        const string key = "SYNTHETIC_ECHO_KEY_30bf";
        await using var fixture = await ProviderFixture.Start();
        fixture.Handler = async context =>
        {
            context.Response.Headers["x-request-id"] = key + "-request";
            var response = ProviderFixture.Completed("<img src=x onerror=alert(1)> " + key + " GE_CONNECTION_OK");
            response["model"] = key + "-model";
            await context.Response.WriteAsJsonAsync(response);
        };
        var result = await Probe().TestAsync(Record(fixture.BaseUrl + "/custom/v1"), key, CancellationToken.None);
        Assert.True(result.Success); Assert.True(result.PhraseMatched); Assert.Equal(7, result.ConnectionVersion);
        Assert.Contains("[redacted]", result.Preview); Assert.DoesNotContain(key, result.Preview!);
        Assert.DoesNotContain(key, result.RequestId!); Assert.DoesNotContain(key, result.ObservedModel!);
        Assert.Contains("<img", result.Preview); // Plain text contract, rendered with Vue interpolation by browser tests.
        Assert.Equal(7, result.Usage!.InputTokens); Assert.Equal(3, result.Usage.OutputTokens); Assert.Equal(10, result.Usage.TotalTokens);
        var request = Assert.Single(fixture.Requests);
        Assert.Equal("/custom/v1/responses", request.Path); Assert.Equal("Bearer " + key, request.Authorization);
        Assert.Equal("user/exact-model", request.Body["model"]!.GetValue<string>());
        Assert.Equal(ResponsesProbe.SyntheticInput, request.Body["input"]!.GetValue<string>());
        Assert.False(request.Body["stream"]!.GetValue<bool>()); Assert.False(request.Body["store"]!.GetValue<bool>());
        Assert.Equal(128, request.Body["max_output_tokens"]!.GetValue<int>());
        Assert.Equal(5, request.Body.Count);
        var second = Record(fixture.BaseUrl + "/v1"); second.AuthMode = "none";
        Assert.True((await Probe().TestAsync(second, null, CancellationToken.None)).Success);
        Assert.Equal(2, fixture.Count); Assert.Null(fixture.Requests[1].Authorization);
        Assert.Equal("/v1/responses", fixture.Requests[1].Path);
    }

    [Theory]
    [InlineData(400, "rejected_request")]
    [InlineData(401, "auth_access")]
    [InlineData(403, "auth_access")]
    [InlineData(404, "rejected_request")]
    [InlineData(429, "rate_limit")]
    [InlineData(500, "connectivity")]
    [InlineData(503, "connectivity")]
    [InlineData(307, "rejected_request")]
    public async Task ErrorStatusClassificationIgnoresRawSecretEchoAndDoesNotRetryOrRedirect(int status, string category)
    {
        await using var fixture = await ProviderFixture.Start();
        fixture.Handler = async context =>
        {
            context.Response.StatusCode = status;
            context.Response.Headers.Location = fixture.BaseUrl + "/unapproved";
            await context.Response.WriteAsync("SYNTHETIC_WRONG_KEY raw provider detail <script>alert(1)</script>");
        };
        var result = await Probe().TestAsync(Record(fixture.BaseUrl), "SYNTHETIC_WRONG_KEY", CancellationToken.None);
        Assert.False(result.Success); Assert.Equal(category, result.Category); Assert.Equal(1, fixture.Count);
        Assert.Null(result.Preview); Assert.DoesNotContain("SYNTHETIC_WRONG_KEY", result.Message);
        Assert.DoesNotContain("script", result.Message);
    }

    [Theory]
    [InlineData("html", "unexpected_payload")]
    [InlineData("malformed", "unexpected_payload")]
    [InlineData("failed", "incomplete_output")]
    [InlineData("incomplete", "incomplete_output")]
    [InlineData("missingtext", "incomplete_output")]
    [InlineData("wrongobject", "unexpected_payload")]
    [InlineData("oversize", "unexpected_payload")]
    [InlineData("duplicate", "unexpected_payload")]
    [InlineData("messagestatus", "incomplete_output")]
    public async Task Http200IsNotEnoughForTextVerification(string responseKind, string category)
    {
        await using var fixture = await ProviderFixture.Start();
        fixture.Handler = async context =>
        {
            context.Response.ContentType = responseKind == "html" ? "text/html" : "application/json";
            if (responseKind == "html") { await context.Response.WriteAsync("<h1>Proxy login required</h1>"); return; }
            if (responseKind == "malformed") { await context.Response.WriteAsync("{invalid json"); return; }
            if (responseKind == "duplicate") { await context.Response.WriteAsync("{\"object\":\"response\",\"status\":\"failed\",\"status\":\"completed\",\"output\":[]}"); return; }
            if (responseKind == "oversize") { await context.Response.WriteAsync(new string('x', ResponsesProbe.MaximumResponseBytes + 1)); return; }
            var response = ProviderFixture.Completed();
            if (responseKind is "failed" or "incomplete") response["status"] = responseKind;
            if (responseKind == "missingtext") response["output"] = new JsonArray(new JsonObject { ["type"] = "reasoning" });
            if (responseKind == "wrongobject") response["object"] = "chat.completion";
            if (responseKind == "messagestatus") response["output"]![1]!["status"] = "in_progress";
            await context.Response.WriteAsJsonAsync(response);
        };
        var result = await Probe().TestAsync(Record(fixture.BaseUrl), "SYNTHETIC_KEY", CancellationToken.None);
        Assert.False(result.Success); Assert.Equal(category, result.Category); Assert.Equal(1, fixture.Count);
    }

    [Fact]
    public async Task CompletionWithDifferentWordingIsSuccessAndAbsentUsageIsNotInvented()
    {
        await using var fixture = await ProviderFixture.Start();
        fixture.Handler = async context =>
        {
            var response = ProviderFixture.Completed("I received your message."); response.Remove("usage");
            await context.Response.WriteAsJsonAsync(response);
        };
        var result = await Probe().TestAsync(Record(fixture.BaseUrl), "SYNTHETIC_KEY", CancellationToken.None);
        Assert.True(result.Success); Assert.False(result.PhraseMatched); Assert.Null(result.Usage);
    }

    [Fact]
    public async Task ActualSocketTlsCertificateVerificationRejectsUntrustedFixture()
    {
        await using var fixture = await ProviderFixture.Start(tls: true);
        var result = await Probe().TestAsync(Record(fixture.BaseUrl), "SYNTHETIC_KEY", CancellationToken.None);
        Assert.Equal("tls", result.Category); Assert.False(result.Success); Assert.Equal(0, fixture.Count);
    }

    [Fact]
    public async Task TimeoutAndCancellationAreBoundedAndNeverRetried()
    {
        await using var fixture = await ProviderFixture.Start();
        fixture.Handler = async context => { try { await Task.Delay(TimeSpan.FromSeconds(20), context.RequestAborted); } catch (OperationCanceledException) { } };
        var timedOut = await Probe().TestAsync(Record(fixture.BaseUrl), "SYNTHETIC_KEY", CancellationToken.None);
        Assert.Equal("timeout", timedOut.Category); Assert.InRange(timedOut.DurationMs, 4000, 10000);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var cancelled = await Probe().TestAsync(Record(fixture.BaseUrl), "SYNTHETIC_KEY", cancel.Token);
        Assert.Equal("cancelled", cancelled.Category); Assert.Equal(2, fixture.Count);
    }

    [Fact]
    public async Task DelayedProbeCannotVerifyChangedConnectionAndConcurrentDuplicateIsRejected()
    {
        await using var fixture = await ProviderFixture.Start();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Handler = async context => { entered.SetResult(); await release.Task; await context.Response.WriteAsJsonAsync(ProviderFixture.Completed()); };
        await using var factory = new LocalApiFactory(new ProviderTestData().Path);
        using var client = await factory.CreatePairedClientAsync();
        var input = ProviderApiTests.Write(fixture.BaseUrl);
        var profile = await ProviderApiTests.Create(client, input);
        Assert.Equal(0, fixture.Count); // Save never performs generation.
        var pending = ProviderApiTests.Test(client, profile);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(HttpStatusCode.Conflict, (await ProviderApiTests.Test(client, profile)).StatusCode);
        input["expectedRevision"] = 1; input["modelId"] = "new-model"; input["credential"] = new JsonObject { ["action"] = "keep" };
        using var update = await client.PutAsJsonAsync(ProviderApiTests.Url(profile), input); Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        release.SetResult();
        using var completed = await pending;
        Assert.Equal(1, (await completed.Content.ReadFromJsonAsync<JsonObject>())!["connectionVersion"]!.GetValue<int>());
        var current = await client.GetFromJsonAsync<JsonObject>(ProviderApiTests.Url(profile));
        Assert.Equal(2, current!["connectionVersion"]!.GetValue<int>()); Assert.Null(current["lastTest"]);
        Assert.Equal(1, fixture.Count);
        Assert.Equal(HttpStatusCode.Conflict, (await ProviderApiTests.Test(client, profile)).StatusCode);
        Assert.Equal(1, fixture.Count);
    }
}
