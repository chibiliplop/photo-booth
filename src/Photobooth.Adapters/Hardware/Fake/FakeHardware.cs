using System;
using Microsoft.Extensions.Logging;
using Photobooth.Core.Abstractions;

namespace Photobooth.Adapters.Hardware.Fake;

/// <summary>
/// No-GPIO button input for Windows/macOS dev (and the Pi without wiring). It raises no edges by
/// itself; the App routes keyboard keys straight to the workflow, and these methods exist for tests.
/// </summary>
public sealed class FakeButtonInput : IButtonInput
{
    public event Action? PhotoPressed;
    public event Action? VideoPressed;
    public event Action? PrintPressed;

    public void Start() { }

    public void PressPhoto() => PhotoPressed?.Invoke();
    public void PressVideo() => VideoPressed?.Invoke();
    public void PressPrint() => PrintPressed?.Invoke();

    public void Dispose() { }
}

/// <summary>In-memory light; logs transitions and exposes <see cref="IsOn"/> for tests.</summary>
public sealed class FakeLightOutput : ILightOutput
{
    private readonly ILogger<FakeLightOutput> _log;

    public FakeLightOutput(ILogger<FakeLightOutput> log) => _log = log;

    public bool IsOn { get; private set; }

    public void On()
    {
        IsOn = true;
        _log.LogDebug("Fake light ON");
    }

    public void Off()
    {
        IsOn = false;
        _log.LogDebug("Fake light OFF");
    }

    public void Dispose() { }
}

/// <summary>
/// Light output for a booth wired WITHOUT a light (Hardware.LightEnabled = false) while real GPIO is
/// otherwise active. Unlike <see cref="Linux.GpioLightOutput"/> it never opens a pin, so the light GPIO
/// stays free; unlike <see cref="FakeLightOutput"/> it stays silent (no per-capture log noise in production).
/// </summary>
public sealed class NoOpLightOutput : ILightOutput
{
    public void On() { }
    public void Off() { }
    public void Dispose() { }
}

/// <summary>Constant ambient reading for dev; the v1 workflow does not consume this.</summary>
public sealed class FakeLightSensor : ILightSensor
{
    public double ReadLux() => 100.0;
}
