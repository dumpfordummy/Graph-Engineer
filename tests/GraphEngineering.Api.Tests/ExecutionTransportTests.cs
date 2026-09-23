using System.Text.Json;
using GraphEngineering.Api.Providers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace GraphEngineering.Api.Tests;

public sealed class ExecutionTransportTests
{
    private const string Key = "SYNTHETIC_M3_TRANSPORT_KEY";
    private static ProviderRecord Profile(string url) => new()
    {
        Id = Guid.NewGuid(), BaseUrl = url, ModelId = "exact-fixture-model", AuthMode = "bearer",
        AllowPrivateNetwork = true, AllowInsecureHttp = true, TimeoutSeconds = 5, MaxOutputTokens = 128, ConnectionVersion = 1
    };
    private static ResponsesProbe Adapter() => new(new ProviderDestinationPolicy(new ConfigurationBuilder().Build()));

    [Fact]
    public async Task ExecutionRetainsFullTextAndSendsOnlyExplicitPromptWhileProbeKeepsItsFixedPreviewContract()
    {
        await using var provider = await ProviderFixture.Start();
        var answer = new string('x', 4096) + "<script>untrusted()</script>";
        provider.Handler = context => context.Response.WriteAsJsonAsync(ProviderFixture.Completed(answer));
        var result = await Adapter().ExecuteAsync(Profile(provider.BaseUrl), Key, "literal {{inputs.untouched}}", [Key], CancellationToken.None);
        Assert.True(result.Success); Assert.Equal(answer, result.Text); Assert.Equal("ResponseReceived", result.ExternalOutcome);
        var call = Assert.Single(provider.Requests);
        Assert.Equal(5, call.Body.Count);
        Assert.Equal("literal {{inputs.untouched}}", call.Body["input"]!.GetValue<string>());
        var probe = await Adapter().TestAsync(Profile(provider.BaseUrl), Key, CancellationToken.None);
        Assert.True(probe.Success); Assert.Equal(500, probe.Preview!.Length);
        Assert.Equal(ResponsesProbe.SyntheticInput, provider.Requests[1].Body["input"]!.GetValue<string>());
        Assert.Equal(2, provider.Count);
    }

    [Theory]
    [InlineData("text")]
    [InlineData("normalized_text")]
    [InlineData("model")]
    [InlineData("header")]
    [InlineData("previous_key")]
    public async Task SensitiveContentFailsWithoutReturningOrSilentlyRedactingAnOutput(string location)
    {
        const string previous = "SYNTHETIC_PREVIOUS_NODE_KEY";
        await using var provider = await ProviderFixture.Start();
        provider.Handler = async context =>
        {
            var text = location switch { "text" => Key, "normalized_text" => Key.Insert(9, "\0"), "previous_key" => previous, _ => "safe" };
            var body = ProviderFixture.Completed(text);
            if (location == "model") body["model"] = Key.Insert(9, "\0");
            if (location == "header") context.Response.Headers["x-request-id"] = Key;
            await context.Response.WriteAsJsonAsync(body);
        };
        var result = await Adapter().ExecuteAsync(Profile(provider.BaseUrl), Key, "safe", [Key, previous], CancellationToken.None);
        Assert.False(result.Success); Assert.Equal("sensitive_output", result.Category); Assert.Null(result.Text);
        Assert.Null(result.ObservedModel); Assert.Null(result.RequestId);
        var serialized = JsonSerializer.Serialize(result);
        Assert.DoesNotContain(Key, serialized); Assert.DoesNotContain(previous, serialized); Assert.Equal(1, provider.Count);
    }

    [Fact]
    public async Task ResolvedPromptByteCapRejectsBeforeAnyHttpRequest()
    {
        await using var provider = await ProviderFixture.Start();
        var result = await Adapter().ExecuteAsync(Profile(provider.BaseUrl), Key, new string('界', 22000), [Key], CancellationToken.None);
        Assert.Equal("prompt_too_large", result.Category); Assert.Equal("NotStarted", result.ExternalOutcome); Assert.Equal(0, provider.Count);
    }

    [Theory]
    [InlineData(131072, true)]
    [InlineData(131073, false)]
    public async Task FullTextLimitNeverTruncatesAReportedSuccess(int length, bool success)
    {
        await using var provider = await ProviderFixture.Start();
        provider.Handler = context => context.Response.WriteAsJsonAsync(ProviderFixture.Completed(new string('x', length)));
        var result = await Adapter().ExecuteAsync(Profile(provider.BaseUrl), Key, "safe", [Key], CancellationToken.None);
        Assert.Equal(success, result.Success);
        if (success) Assert.Equal(length, result.Text!.Length);
        else { Assert.Equal("output_too_large", result.Category); Assert.Null(result.Text); }
        Assert.Equal(1, provider.Count);
    }
}
