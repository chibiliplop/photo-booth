using System;
using System.Diagnostics;
using System.Threading;

namespace Photobooth.Adapters.Hardware;

/// <summary>
/// Monotonic-clock software debounce, replacing UWP's <c>GpioPin.DebounceTimeout</c> (which
/// System.Device.Gpio does not provide). Uses <see cref="Stopwatch"/> so NTP/clock changes can't
/// corrupt the window. Lock-free; safe to call from the GPIO event thread.
/// </summary>
internal sealed class Debouncer
{
    private readonly long _windowTicks;
    private long _last;

    public Debouncer(TimeSpan window)
        => _windowTicks = (long)(window.TotalSeconds * Stopwatch.Frequency);

    /// <returns>true if enough time has elapsed since the last accepted edge.</returns>
    public bool Allow()
    {
        var now = Stopwatch.GetTimestamp();
        var prev = Interlocked.Read(ref _last);
        if (prev != 0 && now - prev < _windowTicks)
            return false;
        Interlocked.Exchange(ref _last, now);
        return true;
    }
}
