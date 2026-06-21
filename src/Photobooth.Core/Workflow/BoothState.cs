namespace Photobooth.Core.Workflow;

/// <summary>
/// The single source of truth for what the booth is doing. Written only by the workflow's
/// consumer loop (single writer); read from the slideshow/keepalive loops via volatile reads.
/// "Recording-ness" is <see cref="Recording"/> — there is no separate boolean flag to drift.
/// </summary>
public enum BoothState
{
    Idle,
    Capturing,
    Recording,
    Degraded,
    ShuttingDown
}
