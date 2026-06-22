namespace Photobooth.Core.Workflow;

/// <summary>
/// Discrete events fed to the workflow's single consumer. Slideshow and keepalive ticks are NOT
/// commands — they run on independent loops so they can never be starved by a long capture.
/// </summary>
public abstract record BoothCommand
{
    /// <summary>A photo button press or keyboard trigger.</summary>
    public sealed record PhotoRequested : BoothCommand;

    /// <summary>Print the last captured photo, if printing is enabled and a capture is available.</summary>
    public sealed record PrintRequested : BoothCommand;

    /// <summary>A video button press (starts or stops recording depending on current state).</summary>
    public sealed record VideoToggleRequested : BoothCommand;

    /// <summary>Fired by the auto-stop timer; only stops if still recording the same take (epoch match).</summary>
    public sealed record VideoAutoStop(long Epoch) : BoothCommand;

    /// <summary>The GoPro answered again while Degraded; transition back to Idle and clear the status.</summary>
    public sealed record Recovered : BoothCommand;

    /// <summary>Graceful shutdown: light off, timers disposed.</summary>
    public sealed record Shutdown : BoothCommand;
}
