using System.Net;
using Ardea.Platform.Http;

namespace Ardea.Platform.Tests.Http;

/// <summary>Which addresses outbound calls may reach (master plan 9.5, 16.9).</summary>
public sealed class OutboundAddressTests
{
    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("169.254.169.254", true)]
    [InlineData("0.0.0.0", true)]
    [InlineData("fe80::1", true)]
    [InlineData("::ffff:127.0.0.1", true)]
    [InlineData("224.0.0.1", true)]
    [InlineData("10.1.2.3", false)]
    [InlineData("140.82.112.6", false)]
    public void Loopback_link_local_and_metadata_are_never_reached(string address, bool forbidden) =>
        Assert.Equal(forbidden, OutboundAddresses.IsForbidden(IPAddress.Parse(address)));

    [Theory]
    [InlineData("10.0.0.1", true)]
    [InlineData("172.16.5.4", true)]
    [InlineData("172.32.0.1", false)]
    [InlineData("192.168.1.1", true)]
    [InlineData("100.64.0.1", true)]
    [InlineData("fd00::1", true)]
    [InlineData("::ffff:192.168.0.1", true)]
    [InlineData("140.82.112.6", false)]
    [InlineData("2606:50c0:8000::153", false)]
    public void Private_ranges_need_an_administrators_decision(string address, bool internalRange) =>
        Assert.Equal(internalRange, OutboundAddresses.IsInternal(IPAddress.Parse(address)));

    [Fact]
    public async Task The_handler_refuses_a_host_that_resolves_to_loopback()
    {
        using var client = new HttpClient(OutboundAddresses.CreateHandler());
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(new Uri("http://127.0.0.1:9/"), TestContext.Current.CancellationToken));
        Assert.Contains("no address Ardea may connect to", error.Message, StringComparison.Ordinal);
    }
}
