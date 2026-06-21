namespace Photobooth.Core.Options;

/// <summary>All booth timings, tunable at the venue without a rebuild (bound from "Timings").</summary>
public sealed class TimingOptions
{
    public const string Section = "Timings";

    public int PoseMs { get; set; } = 2000;
    public int CountdownStepMs { get; set; } = 1000;
    public int LightSettleMs { get; set; } = 1000;
    public int PhotoDisplayMs { get; set; } = 5000;
    public int VideoMaxSeconds { get; set; } = 10;
    public int SlideshowIntervalSeconds { get; set; } = 5;

    /// <summary>How often the connectivity monitor probes the GoPro to drive the operator status (green/orange/red).</summary>
    public int StatusPollSeconds { get; set; } = 3;

    /// <summary>Hard ceiling on any single photo/video action; on overrun the workflow force-resets and turns the light off.</summary>
    public int WatchdogSeconds { get; set; } = 30;

    public string? Validate()
    {
        if (CountdownStepMs <= 0) return "Timings.CountdownStepMs must be > 0.";
        if (VideoMaxSeconds <= 0) return "Timings.VideoMaxSeconds must be > 0.";
        if (SlideshowIntervalSeconds <= 0) return "Timings.SlideshowIntervalSeconds must be > 0.";
        if (StatusPollSeconds <= 0) return "Timings.StatusPollSeconds must be > 0.";
        if (WatchdogSeconds <= 0) return "Timings.WatchdogSeconds must be > 0.";
        return null;
    }
}
