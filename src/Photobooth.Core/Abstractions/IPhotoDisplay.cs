namespace Photobooth.Core.Abstractions;

/// <summary>Severity of an operator status message, mapped to a colour by the UI (green/orange/red).</summary>
public enum BoothStatusLevel
{
    /// <summary>Neutral/transient hint (dark pill).</summary>
    Info,
    /// <summary>All good — e.g. "GoPro connectée" (green). The UI auto-hides this banner after a few seconds.</summary>
    Ready,
    /// <summary>Trying / not ready yet — e.g. "Connexion à la GoPro…" (orange). Persists.</summary>
    Warning,
    /// <summary>Problem the operator must act on — e.g. "GoPro indisponible" (red). Persists.</summary>
    Error
}

/// <summary>
/// The view surface the workflow drives. The implementation (UI ViewModel) is responsible for
/// marshaling every call onto the UI thread; the workflow always calls these from a background thread.
/// </summary>
public interface IPhotoDisplay
{
    /// <summary>Show a text message (countdown, "Prenez la pose", ...) on the next photo card.</summary>
    void ShowMessage(string text);

    /// <summary>Show a captured/slideshow photo (raw encoded image bytes) on the next photo card.</summary>
    void ShowPhoto(byte[] imageData);

    /// <summary>Tell the UI whether the last captured photo can currently be printed.</summary>
    void SetPrintAvailable(bool available);

    /// <summary>
    /// Trigger a brief white-flash shutter feedback. Called once per capture, just before
    /// <see cref="ShowPhoto"/>. The slideshow never calls this — flash is a capture-only event.
    /// </summary>
    void Flash();

    /// <summary>
    /// Cinematic count-in before filming begins: shows the clapperboard with <paramref name="seconds"/>
    /// on the slate (3, 2, 1). Called once per beat by the workflow; recording has NOT started yet.
    /// </summary>
    void ShowVideoCountdown(int seconds);

    /// <summary>
    /// Toggle the recording overlay (blinking "REC", reverse time-remaining countdown, vintage film look).
    /// <paramref name="totalSeconds"/> is the full take length so the UI can drive the remaining-time
    /// countdown/progress; it is ignored when <paramref name="recording"/> is false.
    /// </summary>
    void SetRecording(bool recording, int totalSeconds = 0);

    /// <summary>
    /// Show a discreet operator status banner; null clears it. <paramref name="level"/> drives the colour;
    /// a <see cref="BoothStatusLevel.Ready"/> banner auto-hides after a few seconds (the booth stays clean).
    /// </summary>
    void SetStatus(string? status, BoothStatusLevel level = BoothStatusLevel.Info);

    /// <summary>
    /// Update the persistent GoPro connectivity dot (always visible once known). Owned exclusively by the
    /// connectivity monitor so transient banner messages never corrupt the at-a-glance indicator.
    /// </summary>
    void SetConnectivity(BoothStatusLevel level);
}

/// <summary>A photo decoded/resized by the UI layer and ready to be shown later.</summary>
public interface IPreparedPhoto : System.IDisposable
{
}

/// <summary>
/// Optional fast path for slideshows: the workflow can ask the UI to decode/resize a photo before the
/// visible transition, then show that prepared photo at the next tick.
/// </summary>
public interface IPreparedPhotoDisplay : IPhotoDisplay
{
    IPreparedPhoto? PreparePhoto(byte[] imageData);
    void ShowPreparedPhoto(IPreparedPhoto preparedPhoto);
}
