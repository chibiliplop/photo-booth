using System;
using System.Device.Gpio;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Photobooth.Adapters.Hardware.Fake;
using Photobooth.Adapters.Hardware.Linux;
using Photobooth.Core.Abstractions;
using Photobooth.Core.Options;

namespace Photobooth.App.Composition;

/// <summary>
/// Resolves the hardware layer once, with a graceful fallback: if "linux"/"auto" is selected but GPIO
/// can't be initialized (wrong board, missing permissions, no wiring), it logs loudly and falls back to
/// fake devices so the booth still boots in keyboard mode rather than crash-looping.
/// </summary>
internal sealed class HardwareBundle : IDisposable
{
    public IButtonInput Button { get; }
    public ILightOutput Light { get; }
    public ILightSensor Sensor { get; }

    /// <summary>
    /// Non-null when hardware init degraded in a way the operator should see (e.g. GPIO was expected but
    /// failed, so the booth fell back to keyboard mode). Surfaced on the kiosk screen at startup. Null on a
    /// normal Pi boot AND on dev/non-GPIO machines (where fake hardware is expected, not an error).
    /// </summary>
    public string? StartupWarning { get; }

    private readonly GpioController? _controller;

    private HardwareBundle(IButtonInput button, ILightOutput light, ILightSensor sensor, GpioController? controller, string? startupWarning)
    {
        Button = button;
        Light = light;
        Sensor = sensor;
        _controller = controller;
        StartupWarning = startupWarning;
    }

    public static HardwareBundle Create(IServiceProvider sp)
    {
        var opt = sp.GetRequiredService<IOptions<HardwareOptions>>().Value;
        var lf = sp.GetRequiredService<ILoggerFactory>();
        var log = lf.CreateLogger<HardwareBundle>();

        if (ShouldUseLinux(opt))
        {
            try
            {
                var controller = new GpioController();
                var button = new GpioButtonInput(controller, opt, lf.CreateLogger<GpioButtonInput>());
                // No light wired (LightEnabled = false): never open the light pin; the workflow's
                // On()/Off() calls become silent no-ops so a DIY box without a light works unchanged.
                ILightOutput light = opt.LightEnabled
                    ? new GpioLightOutput(controller, opt, lf.CreateLogger<GpioLightOutput>())
                    : new NoOpLightOutput();
                ILightSensor sensor = opt.LightSensorEnabled
                    ? new Max44009LightSensor(opt)
                    : new FakeLightSensor();
                log.LogInformation("Using real Raspberry Pi GPIO hardware (photo={Photo}, video={Video}, print={Print}, light={Light}).",
                    opt.PhotoButtonPin,
                    opt.VideoButtonPin,
                    opt.PrintButtonEnabled ? opt.PrintButtonPin.ToString() : "disabled",
                    opt.LightEnabled ? opt.LightPin.ToString() : "disabled");
                return new HardwareBundle(button, light, sensor, controller, startupWarning: null);
            }
            catch (Exception ex)
            {
                // GPIO was expected (real Pi) but failed: wrong wiring, missing gpio/i2c group, pin in use.
                // Don't crash-loop — fall back to keyboard mode, but tell the operator on screen (they only
                // have the HDMI display) instead of hiding it in a log file they can't reach.
                log.LogError(ex, "GPIO initialization failed; falling back to fake hardware (keyboard mode).");
                return new HardwareBundle(
                    new FakeButtonInput(),
                    new FakeLightOutput(lf.CreateLogger<FakeLightOutput>()),
                    new FakeLightSensor(),
                    controller: null,
                    startupWarning: "Boutons GPIO inaccessibles : la borne fonctionne au clavier uniquement. " +
                                    "Vérifiez le câblage et les droits d'accès (groupes gpio/i2c).");
            }
        }

        log.LogInformation("Using fake hardware (keyboard-driven; no GPIO).");
        return new HardwareBundle(
            new FakeButtonInput(),
            new FakeLightOutput(lf.CreateLogger<FakeLightOutput>()),
            new FakeLightSensor(),
            controller: null,
            startupWarning: null); // expected on dev / non-GPIO machines — not an error to surface
    }

    private static bool ShouldUseLinux(HardwareOptions opt)
    {
        if (opt.IsFake) return false;
        if (!OperatingSystem.IsLinux()) return false; // Windows/macOS dev -> fake
        if (opt.IsForcedLinux) return true;
        return File.Exists("/dev/gpiochip0"); // "auto": only if a GPIO chip is present
    }

    public void Dispose()
    {
        Button.Dispose();
        Light.Dispose();
        (Sensor as IDisposable)?.Dispose();
        _controller?.Dispose();
    }
}
