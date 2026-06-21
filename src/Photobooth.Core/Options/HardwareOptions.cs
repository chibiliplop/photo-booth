using System.Collections.Generic;

namespace Photobooth.Core.Options;

/// <summary>GPIO/I2C pin mapping and hardware selection (bound from the "Hardware" section).</summary>
public sealed class HardwareOptions
{
    public const string Section = "Hardware";

    /// <summary>Highest BCM pin number on the Raspberry Pi 40-pin header (used to validate operator input).</summary>
    private const int MaxBcmPin = 27;

    /// <summary>"auto" (Linux+GPIO present -> real, else fake), "linux" (force real), or "fake".</summary>
    public string Mode { get; set; } = "auto";

    public int PhotoButtonPin { get; set; } = 18;
    public int VideoButtonPin { get; set; } = 20;
    public int LightPin { get; set; } = 17;

    /// <summary>
    /// Set false when the booth has NO light wired. When disabled the light GPIO is never opened and the
    /// workflow's light commands become harmless no-ops, so a DIY box without a light/relay works as-is.
    /// </summary>
    public bool LightEnabled { get; set; } = true;

    public int ButtonDebounceMs { get; set; } = 80;

    public int I2cBus { get; set; } = 1;
    public string LightSensorAddress { get; set; } = "0x4A";
    public bool LightSensorEnabled { get; set; } = false;

    public bool IsFake => string.Equals(Mode, "fake", System.StringComparison.OrdinalIgnoreCase);
    public bool IsForcedLinux => string.Equals(Mode, "linux", System.StringComparison.OrdinalIgnoreCase);

    public string? Validate()
    {
        if (ButtonDebounceMs < 0) return "Hardware.ButtonDebounceMs doit être >= 0.";
        if (I2cBus < 0) return "Hardware.I2cBus doit être >= 0.";

        // Only the pins actually in use are validated, so a booth without a light isn't blocked by LightPin.
        var pins = new List<(string Name, int Pin)>
        {
            ("PhotoButtonPin", PhotoButtonPin),
            ("VideoButtonPin", VideoButtonPin),
        };
        if (LightEnabled) pins.Add(("LightPin", LightPin));

        foreach (var (name, pin) in pins)
        {
            if (pin is < 0 or > MaxBcmPin)
                return $"Hardware.{name}={pin} hors limites : utilisez un numéro de broche BCM de 0 à {MaxBcmPin}.";
        }

        for (var i = 0; i < pins.Count; i++)
        {
            for (var j = i + 1; j < pins.Count; j++)
            {
                if (pins[i].Pin == pins[j].Pin)
                    return $"Hardware.{pins[i].Name} et Hardware.{pins[j].Name} utilisent la même broche {pins[i].Pin} : chaque fonction doit avoir sa propre broche GPIO.";
            }
        }

        return null;
    }
}
