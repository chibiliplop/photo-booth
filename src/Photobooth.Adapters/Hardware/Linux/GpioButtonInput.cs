using System;
using System.Device.Gpio;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Photobooth.Core.Abstractions;
using Photobooth.Core.Options;

namespace Photobooth.Adapters.Hardware.Linux;

/// <summary>
/// Real GPIO buttons via System.Device.Gpio. Opens with internal pull-up when supported (falling back
/// to plain Input), reacts on the falling edge, and debounces in software. The edge callback only
/// debounces + raises an event and returns immediately — it never does work on the driver's event
/// thread (which would block the GPIO subsystem and risk missed/re-entrant edges).
/// </summary>
public sealed class GpioButtonInput : IButtonInput
{
    private readonly GpioController _controller;
    private readonly HardwareOptions _opt;
    private readonly ILogger<GpioButtonInput> _log;
    private readonly Debouncer _photoDebounce;
    private readonly Debouncer _videoDebounce;
    private readonly Debouncer _printDebounce;
    private bool _started;
    private long _startedAtTicks;
    // 500 ms grace period: the Pi GPIO driver often fires a spurious falling-edge
    // on the video pin (and sometimes photo pin) when RegisterCallbackForPinValueChangedEvent
    // activates the internal pull-up. This ticks-based guard suppresses those phantom presses.
    private static readonly long GraceTicks = (long)(0.5 * Stopwatch.Frequency);

    public event Action? PhotoPressed;
    public event Action? VideoPressed;
    public event Action? PrintPressed;

    public GpioButtonInput(GpioController controller, HardwareOptions opt, ILogger<GpioButtonInput> log)
    {
        _controller = controller;
        _opt = opt;
        _log = log;
        var window = TimeSpan.FromMilliseconds(_opt.ButtonDebounceMs);
        _photoDebounce = new Debouncer(window);
        _videoDebounce = new Debouncer(window);
        _printDebounce = new Debouncer(window);
    }

    public void Start()
    {
        if (_started) return;
        _started = true;

        OpenInput(_opt.PhotoButtonPin);
        OpenInput(_opt.VideoButtonPin);
        if (_opt.PrintButtonEnabled)
            OpenInput(_opt.PrintButtonPin);

        // Timestamp BEFORE registering callbacks so any edge that fires synchronously during
        // registration (pull-up settle glitch) is covered by the grace-period check below.
        _startedAtTicks = Stopwatch.GetTimestamp();

        _controller.RegisterCallbackForPinValueChangedEvent(_opt.PhotoButtonPin, PinEventTypes.Falling, OnPhotoEdge);
        _controller.RegisterCallbackForPinValueChangedEvent(_opt.VideoButtonPin, PinEventTypes.Falling, OnVideoEdge);
        if (_opt.PrintButtonEnabled)
            _controller.RegisterCallbackForPinValueChangedEvent(_opt.PrintButtonPin, PinEventTypes.Falling, OnPrintEdge);

        _log.LogInformation("GPIO buttons armed (photo pin {Photo}, video pin {Video}, print pin {Print}).",
            _opt.PhotoButtonPin, _opt.VideoButtonPin, _opt.PrintButtonEnabled ? _opt.PrintButtonPin.ToString() : "disabled");
    }

    private void OpenInput(int pin)
    {
        var mode = _controller.IsPinModeSupported(pin, PinMode.InputPullUp)
            ? PinMode.InputPullUp
            : PinMode.Input;
        _controller.OpenPin(pin, mode);
    }

    private bool IsStartupGlitch() => Stopwatch.GetTimestamp() - _startedAtTicks < GraceTicks;

    private void OnPhotoEdge(object sender, PinValueChangedEventArgs e)
    {
        if (IsStartupGlitch()) return;
        if (_photoDebounce.Allow()) PhotoPressed?.Invoke();
    }

    private void OnVideoEdge(object sender, PinValueChangedEventArgs e)
    {
        if (IsStartupGlitch()) return;
        if (_videoDebounce.Allow()) VideoPressed?.Invoke();
    }

    private void OnPrintEdge(object sender, PinValueChangedEventArgs e)
    {
        if (IsStartupGlitch()) return;
        if (_printDebounce.Allow()) PrintPressed?.Invoke();
    }

    public void Dispose()
    {
        if (!_started) return;
        try
        {
            _controller.UnregisterCallbackForPinValueChangedEvent(_opt.PhotoButtonPin, OnPhotoEdge);
            _controller.UnregisterCallbackForPinValueChangedEvent(_opt.VideoButtonPin, OnVideoEdge);
            if (_opt.PrintButtonEnabled)
                _controller.UnregisterCallbackForPinValueChangedEvent(_opt.PrintButtonPin, OnPrintEdge);
            if (_controller.IsPinOpen(_opt.PhotoButtonPin)) _controller.ClosePin(_opt.PhotoButtonPin);
            if (_controller.IsPinOpen(_opt.VideoButtonPin)) _controller.ClosePin(_opt.VideoButtonPin);
            if (_opt.PrintButtonEnabled && _controller.IsPinOpen(_opt.PrintButtonPin)) _controller.ClosePin(_opt.PrintButtonPin);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Error disposing GPIO buttons.");
        }
    }
}
