using System;

namespace Photobooth.Core.Options;

/// <summary>Per-event visual theme. Defaults reproduce the current wedding booth (bound from "Theme").</summary>
public sealed class ThemeOptions
{
    public const string Section = "Theme";

    /// <summary>Design canvas used when no (or a malformed) <see cref="ScreenResolution"/> is provided.</summary>
    private const int DefaultWidth = 1280;
    private const int DefaultHeight = 720;

    /// <summary>Sanity bounds for an operator-entered resolution (small VGA panel up to 8K).</summary>
    private const int MinDimension = 320;
    private const int MaxDimension = 7680;

    public string Names { get; set; } = "Camille & Yann";
    public string Year { get; set; } = "2020";

    /// <summary>avares:// asset path or absolute file path of the full-screen background.</summary>
    public string BackgroundImage { get; set; } = "avares://Photobooth.App/Assets/801x410_rick_et_morty_saison_4.jpg";

    /// <summary>Image used as the "captured photo" when Gopro.Mode = fake.</summary>
    public string FakePhotoImage { get; set; } = "avares://Photobooth.App/Assets/background.jpg";

    public string FontFamily { get; set; } = "avares://Photobooth.App/Assets/Wedding.ttf#Wedding Script";

    public string CardColor { get; set; } = "#f4ecdf";
    public string TextColor { get; set; } = "#FFFFFF";
    public string AccentColor { get; set; } = "#000000";

    /// <summary>
    /// Target screen as a "WIDTHxHEIGHT" string (e.g. "1920x1080"). This is the design canvas the whole
    /// booth UI is laid out against; at runtime that canvas is scaled uniformly (Viewbox) to fill the actual
    /// HDMI screen, so any resolution works without code changes. The default 16:9 design fits any 16:9 panel
    /// edge-to-edge; other aspect ratios are letterboxed (the UI is centered and the background shows through).
    /// A malformed value falls back to the 1280×720 default and is reported by <see cref="Validate"/>.
    /// </summary>
    public string ScreenResolution { get; set; } = "1280x720";

    /// <summary>Parsed design width in device-independent pixels (falls back to the default on a malformed value).</summary>
    public int DesignWidth => TryParseResolution(ScreenResolution, out var w, out _) ? w : DefaultWidth;

    /// <summary>Parsed design height in device-independent pixels (falls back to the default on a malformed value).</summary>
    public int DesignHeight => TryParseResolution(ScreenResolution, out _, out var h) ? h : DefaultHeight;

    public string? Validate()
    {
        if (!TryParseResolution(ScreenResolution, out var w, out var h))
            return $"Theme.ScreenResolution=\"{ScreenResolution}\" invalide : utilisez le format LARGEURxHAUTEUR, par ex. \"1280x720\".";
        if (w is < MinDimension or > MaxDimension || h is < MinDimension or > MaxDimension)
            return $"Theme.ScreenResolution={w}x{h} hors limites : chaque dimension doit être comprise entre {MinDimension} et {MaxDimension} pixels.";
        return null;
    }

    /// <summary>Parse "WIDTHxHEIGHT" (case-insensitive separator, surrounding spaces tolerated). Both parts must be positive.</summary>
    private static bool TryParseResolution(string? value, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var parts = value.Split(new[] { 'x', 'X' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) return false;
        if (!int.TryParse(parts[0].Trim(), out width) || !int.TryParse(parts[1].Trim(), out height)) return false;
        return width > 0 && height > 0;
    }
}
