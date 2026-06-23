using System.Net;
using Photobooth.Admin;
using Xunit;

namespace Photobooth.Tests;

public sealed class AdminAddressTests
{
    [Fact]
    public void BuildUrls_excludes_loopback_and_formats_with_port()
    {
        var urls = AdminAddress.BuildUrls(
            new[] { IPAddress.Parse("127.0.0.1"), IPAddress.Parse("10.5.5.100") }, 8080);
        Assert.Equal(new[] { "http://10.5.5.100:8080" }, urls);
    }

    [Fact]
    public void BuildUrls_excludes_ipv6()
    {
        var urls = AdminAddress.BuildUrls(
            new[] { IPAddress.Parse("fe80::1"), IPAddress.Parse("192.168.1.50") }, 8080);
        Assert.Equal(new[] { "http://192.168.1.50:8080" }, urls);
    }

    [Fact]
    public void BuildUrls_deduplicates()
    {
        var urls = AdminAddress.BuildUrls(
            new[] { IPAddress.Parse("10.5.5.100"), IPAddress.Parse("10.5.5.100") }, 8080);
        Assert.Single(urls);
    }

    [Fact]
    public void BuildUrls_returns_empty_when_no_usable_address()
    {
        var urls = AdminAddress.BuildUrls(new[] { IPAddress.Loopback }, 8080);
        Assert.Empty(urls);
    }
}
