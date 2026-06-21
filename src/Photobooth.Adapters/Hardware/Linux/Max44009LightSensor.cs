using System;
using System.Device.I2c;
using System.Globalization;
using Photobooth.Core.Abstractions;
using Photobooth.Core.Options;

namespace Photobooth.Adapters.Hardware.Linux;

/// <summary>
/// MAX44009 ambient light sensor over I2C, ported from the original RasberryPiLib implementation
/// (System.Device.I2c only — no extra package). Not used by the v1 workflow; gated by
/// Hardware.LightSensorEnabled so a missing/miswired sensor never blocks startup.
/// </summary>
public sealed class Max44009LightSensor : ILightSensor, IDisposable
{
    private const byte ConfigRegister = 0x02;
    private const byte LuxHighRegister = 0x03;

    public const int DefaultAddress = 0x4A;

    private readonly I2cDevice _device;

    public Max44009LightSensor(HardwareOptions opt)
    {
        var address = ParseAddress(opt.LightSensorAddress);
        _device = I2cDevice.Create(new I2cConnectionSettings(opt.I2cBus, address));

        // Continuous mode, 100 ms integration: 0b1100_0000 | 0b011 (see datasheet p8).
        Span<byte> config = stackalloc byte[2] { ConfigRegister, 0b1100_0011 };
        _device.Write(config);
    }

    public double ReadLux()
    {
        Span<byte> buffer = stackalloc byte[2];
        _device.WriteByte(LuxHighRegister);
        _device.Read(buffer);

        // Datasheet p9-10.
        int exponent = (buffer[0] & 0b1111_0000) >> 4;
        int mantissa = ((buffer[0] & 0b0000_1111) << 4) | (buffer[1] & 0b0000_1111);
        return Math.Round(Math.Pow(2, exponent) * mantissa * 0.045, 3);
    }

    private static int ParseAddress(string s)
    {
        s = s.Trim();
        return s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? int.Parse(s.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : int.Parse(s, CultureInfo.InvariantCulture);
    }

    public void Dispose() => _device.Dispose();
}
