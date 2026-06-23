using Photobooth.Core.Options;
using Xunit;

namespace Photobooth.Tests;

public sealed class AdminOptionsTests
{
    [Fact]
    public void Defaults_are_disabled_and_valid()
    {
        var o = new AdminOptions();
        Assert.False(o.Enabled);
        Assert.Equal("0.0.0.0", o.ListenAddress);
        Assert.Equal(8080, o.Port);
        Assert.Equal("", o.Pin);
        Assert.Null(o.Validate());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(70000)]
    public void Port_out_of_range_is_rejected(int port)
    {
        var o = new AdminOptions { Port = port };
        Assert.NotNull(o.Validate());
    }

    [Fact]
    public void Empty_listen_address_is_rejected()
    {
        var o = new AdminOptions { ListenAddress = "  " };
        Assert.NotNull(o.Validate());
    }
}
