namespace Photobooth.Core.Options;

/// <summary>UI performance knobs, mainly for Raspberry Pi rendering and image preparation.</summary>
public sealed class UiPerformanceOptions
{
    public const string Section = "UiPerformance";

    public string Mode { get; set; } = "pi";
    public bool EnableCardShadows { get; set; }
    public bool EnableSlideAnimation { get; set; } = true;
    public int SlideDurationMs { get; set; } = 250;
    public int DecodeWidth { get; set; } = 640;
    public bool CacheSlideshowImages { get; set; }
    public int SlideshowCacheSize { get; set; }
    public bool PreloadNextSlide { get; set; } = true;

    public int EffectiveDecodeWidth => DecodeWidth > 0
        ? DecodeWidth
        : IsPiMode ? 640 : 800;

    public bool EffectivePreloadNextSlide => PreloadNextSlide;
    public bool EffectiveEnableSlideAnimation => EnableSlideAnimation;
    public bool EffectiveCacheSlideshowImages => CacheSlideshowImages && SlideshowCacheSize > 0;
    public int EffectiveSlideshowCacheSize => CacheSlideshowImages ? SlideshowCacheSize : 0;
    public int EffectiveSlideDurationMs => SlideDurationMs > 0 ? SlideDurationMs : 250;

    public bool IsPiMode => string.Equals(Mode, "pi", System.StringComparison.OrdinalIgnoreCase);

    public string? Validate()
    {
        if (!string.Equals(Mode, "balanced", System.StringComparison.OrdinalIgnoreCase)
            && !string.Equals(Mode, "pi", System.StringComparison.OrdinalIgnoreCase))
            return "UiPerformance.Mode doit valoir balanced ou pi.";
        if (DecodeWidth < 0) return "UiPerformance.DecodeWidth doit etre >= 0.";
        if (DecodeWidth is > 0 and < 320) return "UiPerformance.DecodeWidth doit etre >= 320, ou 0 pour le mode automatique.";
        if (SlideDurationMs < 0) return "UiPerformance.SlideDurationMs doit etre >= 0.";
        if (SlideshowCacheSize < 0) return "UiPerformance.SlideshowCacheSize doit etre >= 0.";
        return null;
    }
}
