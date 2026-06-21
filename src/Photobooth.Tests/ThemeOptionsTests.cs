using Photobooth.Core.Options;
using Xunit;

namespace Photobooth.Tests;

public class ThemeOptionsTests
{
    [Theory]
    [InlineData("1280x720", 1280, 720)]
    [InlineData("1920x1080", 1920, 1080)]
    [InlineData("1080X1920", 1080, 1920)]   // case-insensitive separator, portrait
    [InlineData(" 1024 x 768 ", 1024, 768)] // surrounding/inner spaces tolerated
    public void Valid_resolution_is_parsed(string value, int expectedWidth, int expectedHeight)
    {
        var theme = new ThemeOptions { ScreenResolution = value };

        Assert.Null(theme.Validate());
        Assert.Equal(expectedWidth, theme.DesignWidth);
        Assert.Equal(expectedHeight, theme.DesignHeight);
    }

    [Fact]
    public void Default_resolution_is_720p_and_valid()
    {
        var theme = new ThemeOptions();

        Assert.Null(theme.Validate());
        Assert.Equal(1280, theme.DesignWidth);
        Assert.Equal(720, theme.DesignHeight);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1280")]
    [InlineData("1280*720")]
    [InlineData("1280x720x30")]
    [InlineData("abcxdef")]
    [InlineData("0x720")]
    [InlineData("-1280x720")]
    public void Malformed_resolution_is_reported_and_falls_back_to_default(string value)
    {
        var theme = new ThemeOptions { ScreenResolution = value };

        Assert.NotNull(theme.Validate());
        // The UI must still render at a sane size even when the operator's value is rejected.
        Assert.Equal(1280, theme.DesignWidth);
        Assert.Equal(720, theme.DesignHeight);
    }

    [Theory]
    [InlineData("100x100")]    // below the minimum dimension
    [InlineData("8000x4000")]  // above the maximum dimension
    public void Out_of_range_resolution_is_reported(string value)
    {
        var theme = new ThemeOptions { ScreenResolution = value };

        Assert.NotNull(theme.Validate());
    }
}
