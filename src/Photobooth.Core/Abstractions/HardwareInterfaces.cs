using System;

namespace Photobooth.Core.Abstractions;

/// <summary>Physical (or simulated) photo/video buttons. Implementations debounce internally.</summary>
public interface IButtonInput : IDisposable
{
    event Action? PhotoPressed;
    event Action? VideoPressed;

    /// <summary>Begin listening for edges. Safe to call once.</summary>
    void Start();
}

/// <summary>The booth light, active-high on the real hardware.</summary>
public interface ILightOutput : IDisposable
{
    void On();
    void Off();
}

/// <summary>Ambient light sensor (MAX44009). Not used by the v1 workflow; kept for future auto-exposure.</summary>
public interface ILightSensor
{
    double ReadLux();
}
