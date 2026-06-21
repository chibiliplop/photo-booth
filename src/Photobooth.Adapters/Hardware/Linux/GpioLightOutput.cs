using System;
using System.Device.Gpio;
using Microsoft.Extensions.Logging;
using Photobooth.Core.Abstractions;
using Photobooth.Core.Options;

namespace Photobooth.Adapters.Hardware.Linux;

/// <summary>Active-high booth light on a single output pin (matches the original SwitchPin semantics).</summary>
public sealed class GpioLightOutput : ILightOutput
{
    private readonly GpioController _controller;
    private readonly int _pin;
    private readonly ILogger<GpioLightOutput> _log;

    public GpioLightOutput(GpioController controller, HardwareOptions opt, ILogger<GpioLightOutput> log)
    {
        _controller = controller;
        _pin = opt.LightPin;
        _log = log;
        _controller.OpenPin(_pin, PinMode.Output, PinValue.Low); // start OFF
    }

    public void On() => _controller.Write(_pin, PinValue.High);

    public void Off() => _controller.Write(_pin, PinValue.Low);

    public void Dispose()
    {
        try
        {
            if (_controller.IsPinOpen(_pin))
            {
                _controller.Write(_pin, PinValue.Low); // never leave the light on
                _controller.ClosePin(_pin);
            }
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Error disposing light pin.");
        }
    }
}
