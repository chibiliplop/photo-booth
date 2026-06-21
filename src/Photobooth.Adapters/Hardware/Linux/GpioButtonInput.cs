using System;
using System.Device.Gpio;
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
    private bool _started;

    public event Action? PhotoPressed;
    public event Action? VideoPressed;

    public GpioButtonInput(GpioController controller, HardwareOptions opt, ILogger<GpioButtonInput> log)
    {
        _controller = controller;
        _opt = opt;
        _log = log;
        var window = TimeSpan.FromMilliseconds(_opt.ButtonDebounceMs);
        _photoDebounce = new Debouncer(window);
        _videoDebounce = new Debouncer(window);
    }

    public void Start()
    {
        if (_started) return;
        _started = true;

        OpenInput(_opt.PhotoButtonPin);
        OpenInput(_opt.VideoButtonPin);

        _controller.RegisterCallbackForPinValueChangedEvent(_opt.PhotoButtonPin, PinEventTypes.Falling, OnPhotoEdge);
        _controller.RegisterCallbackForPinValueChangedEvent(_opt.VideoButtonPin, PinEventTypes.Falling, OnVideoEdge);

        _log.LogInformation("GPIO buttons armed (photo pin {Photo}, video pin {Video}).",
            _opt.PhotoButtonPin, _opt.VideoButtonPin);
    }

    private void OpenInput(int pin)
    {
        var mode = _controller.IsPinModeSupported(pin, PinMode.InputPullUp)
            ? PinMode.InputPullUp
            : PinMode.Input;
        _controller.OpenPin(pin, mode);
    }

    private void OnPhotoEdge(object sender, PinValueChangedEventArgs e)
    {
        if (_photoDebounce.Allow()) PhotoPressed?.Invoke();
    }

    private void OnVideoEdge(object sender, PinValueChangedEventArgs e)
    {
        if (_videoDebounce.Allow()) VideoPressed?.Invoke();
    }

    public void Dispose()
    {
        if (!_started) return;
        try
        {
            _controller.UnregisterCallbackForPinValueChangedEvent(_opt.PhotoButtonPin, OnPhotoEdge);
            _controller.UnregisterCallbackForPinValueChangedEvent(_opt.VideoButtonPin, OnVideoEdge);
            if (_controller.IsPinOpen(_opt.PhotoButtonPin)) _controller.ClosePin(_opt.PhotoButtonPin);
            if (_controller.IsPinOpen(_opt.VideoButtonPin)) _controller.ClosePin(_opt.VideoButtonPin);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Error disposing GPIO buttons.");
        }
    }
}
