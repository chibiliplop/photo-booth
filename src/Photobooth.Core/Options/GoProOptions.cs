namespace Photobooth.Core.Options;

/// <summary>GoPro connectivity + resilience configuration (bound from the "Gopro" section).</summary>
public sealed class GoProOptions
{
    public const string Section = "Gopro";

    /// <summary>"http" (real camera or simulator) or "fake" (no network).</summary>
    public string Mode { get; set; } = "http";

    public string ControlBaseUrl { get; set; } = "http://10.5.5.9";
    public string MediaBaseUrl { get; set; } = "http://10.5.5.9:8080";
    public string KeepAliveHost { get; set; } = "10.5.5.9";
    public int KeepAlivePort { get; set; } = 8554;

    public int KeepAliveIntervalSeconds { get; set; } = 5;

    /// <summary>Timeout for a single HTTP attempt (we control this, not HttpClient.Timeout).</summary>
    public int RequestTimeoutSeconds { get; set; } = 3;

    /// <summary>Overall budget to obtain the freshly captured photo before degrading.</summary>
    public int CaptureDeadlineSeconds { get; set; } = 15;

    /// <summary>Max attempts per individual GoPro call (503/timeout retries).</summary>
    public int MaxRetries { get; set; } = 6;

    public int RetryBackoffMs { get; set; } = 500;

    public bool IsFake => string.Equals(Mode, "fake", System.StringComparison.OrdinalIgnoreCase);

    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(ControlBaseUrl)) return "Gopro.ControlBaseUrl is required.";
        if (string.IsNullOrWhiteSpace(MediaBaseUrl)) return "Gopro.MediaBaseUrl is required.";
        if (KeepAlivePort is <= 0 or > 65535) return "Gopro.KeepAlivePort must be 1..65535.";
        if (RequestTimeoutSeconds <= 0) return "Gopro.RequestTimeoutSeconds must be > 0.";
        if (CaptureDeadlineSeconds <= 0) return "Gopro.CaptureDeadlineSeconds must be > 0.";
        if (MaxRetries <= 0) return "Gopro.MaxRetries must be > 0.";
        return null;
    }
}
