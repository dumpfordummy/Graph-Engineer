using System.Net;
using GraphEngineering.Api.Providers;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace GraphEngineering.Api.Tests;

public sealed class ProviderPolicyTests
{
    private static ProviderDestinationPolicy Policy() => new(new ConfigurationBuilder().Build());

    [Theory]
    [InlineData("http://127.0.0.1:62001/v1", false, false)]
    [InlineData("http://127.0.0.1:62001/v1", true, false)]
    [InlineData("https://127.0.0.1:62001/v1", false, false)]
    [InlineData("https://127.0.0.1:5080/v1", true, true)]
    [InlineData("http://[::1]:5173", true, true)]
    [InlineData("http://169.254.169.254", true, true)]
    [InlineData("https://[fd00:ec2::254]", true, true)]
    [InlineData("https://100.100.100.200", true, true)]
    [InlineData("https://168.63.129.16", true, true)]
    [InlineData("http://[::ffff:169.254.169.254]", true, true)]
    [InlineData("http://0.0.0.0", true, true)]
    [InlineData("http://[::]", true, true)]
    [InlineData("http://224.1.2.3", true, true)]
    [InlineData("http://[ff02::1]", true, true)]
    [InlineData("http://[fe80::1]", true, true)]
    [InlineData("http://8.8.8.8", true, true)]
    [InlineData("https://user:secret@example.com/v1", false, false)]
    [InlineData("https://example.com/v1?key=secret", false, false)]
    [InlineData("https://example.com/v1#secret", false, false)]
    [InlineData("file:///tmp/provider", false, false)]
    [InlineData("https://example.com/v1/responses", false, false)]
    [InlineData("https://example.com/a/../v1", false, false)]
    [InlineData("https://example.com/a/%2e%2e/v1", false, false)]
    [InlineData("https://example.com/a%2fv1", false, false)]
    [InlineData("https://example.com/a/%252e%252e/v1", false, false)]
    public void UnsafeEndpointsAreRejectedWithoutNetworkCalls(string url, bool privateApproval, bool httpApproval) =>
        Assert.Throws<DestinationException>(() => Policy().Validate(url, privateApproval, httpApproval, "bearer"));

    [Fact]
    public void PrefixIsPreservedAndHttpNoAuthRequiresPrivateDestination()
    {
        Assert.Equal("https://example.com/custom/v1", Policy().Validate("https://example.com/custom/v1/", false, false, "bearer").AbsoluteUri);
        Assert.Throws<DestinationException>(() => Policy().Validate("https://8.8.8.8/v1", true, false, "none"));
        Assert.Equal("http://127.0.0.1:62001/custom/v1", Policy().Validate("http://127.0.0.1:62001/custom/v1", true, true, "none").AbsoluteUri);
    }

    [Fact]
    public async Task HostnameResolvedToPrivateAtSaveRequiresApprovalAndEachDialChecksChangedDns()
    {
        await using var fixture = await ProviderFixture.Start();
        var endpoint = new Uri(fixture.BaseUrl);
        var hostname = $"https://localhost:{endpoint.Port}/v1";
        await Assert.ThrowsAsync<DestinationException>(() => Policy().ValidateForSaveAsync(hostname, false, false, "bearer", CancellationToken.None));
        Assert.Equal(hostname, (await Policy().ValidateForSaveAsync(hostname, true, false, "bearer", CancellationToken.None)).AbsoluteUri);
        var profile = new ProviderRecord { BaseUrl = hostname, AuthMode = "bearer", AllowPrivateNetwork = false };
        // A previously public answer is allowed; a later private or metadata answer is rejected before any socket is opened.
        Policy().ValidateAddress(IPAddress.Parse("8.8.8.8"), endpoint.Port, false, false);
        await Assert.ThrowsAsync<DestinationException>(async () => await Policy().ConnectResolvedAsync([IPAddress.Loopback], endpoint.Port, profile, CancellationToken.None));
        profile.AllowPrivateNetwork = true;
        await Assert.ThrowsAsync<DestinationException>(async () => await Policy().ConnectResolvedAsync([IPAddress.Loopback, IPAddress.Parse("169.254.169.254")], endpoint.Port, profile, CancellationToken.None));
        Assert.Equal(0, fixture.Count);
    }

    [Fact]
    public void ProbeAdmissionBoundsGlobalAndPerProfileRequests()
    {
        var admission = new ProbeAdmission(); var first = Guid.NewGuid(); var second = Guid.NewGuid(); var third = Guid.NewGuid();
        Assert.Equal(0, admission.Enter(first)); Assert.Equal(409, admission.Enter(first));
        Assert.Equal(0, admission.Enter(second)); Assert.Equal(429, admission.Enter(third));
        admission.Leave(first); Assert.Equal(0, admission.Enter(third));
    }
}
