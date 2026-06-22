using System;

namespace Photobooth.Core.Options;

/// <summary>Printing configuration (bound from the "Printer" section).</summary>
public sealed class PrinterOptions
{
    public const string Section = "Printer";

    /// <summary>"disabled"/"noop", "cups" (Linux lp), or "file" (export JPEGs to a folder).</summary>
    public string Type { get; set; } = "disabled";

    /// <summary>"manual" (separate print button / P key), "auto", or "photo-button-window".</summary>
    public string TriggerMode { get; set; } = "manual";

    /// <summary>When TriggerMode="photo-button-window", the photo button prints during this many seconds after capture.</summary>
    public int PhotoButtonPrintWindowSeconds { get; set; } = 15;

    /// <summary>When TriggerMode="auto", wait this many seconds after capture before submitting the print job.</summary>
    public int AutoPrintDelaySeconds { get; set; } = 0;

    /// <summary>CUPS queue name. Empty means use the system default printer.</summary>
    public string Name { get; set; } = string.Empty;

    public int Copies { get; set; } = 1;

    /// <summary>CUPS media option, for example "Postcard" for a Canon Selphy CP1300 queue.</summary>
    public string Media { get; set; } = string.Empty;

    /// <summary>Optional extra CUPS options as "key=value;key2=value2".</summary>
    public string Options { get; set; } = string.Empty;

    /// <summary>Command used by the CUPS adapter.</summary>
    public string LpCommand { get; set; } = "lp";

    /// <summary>Destination folder for Type="file".</summary>
    public string OutputPath { get; set; } = "printed";

    public bool IsDisabled =>
        string.Equals(Type, "disabled", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Type, "noop", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Type, "none", StringComparison.OrdinalIgnoreCase);

    public bool IsCups => string.Equals(Type, "cups", StringComparison.OrdinalIgnoreCase);
    public bool IsFile => string.Equals(Type, "file", StringComparison.OrdinalIgnoreCase);
    public bool IsManualTrigger => string.Equals(TriggerMode, "manual", StringComparison.OrdinalIgnoreCase);
    public bool IsAutoTrigger => string.Equals(TriggerMode, "auto", StringComparison.OrdinalIgnoreCase);
    public bool IsPhotoButtonWindowTrigger => string.Equals(TriggerMode, "photo-button-window", StringComparison.OrdinalIgnoreCase);

    public string? Validate()
    {
        if (!IsDisabled && !IsCups && !IsFile)
            return "Printer.Type doit valoir disabled, cups ou file.";
        if (!IsManualTrigger && !IsAutoTrigger && !IsPhotoButtonWindowTrigger)
            return "Printer.TriggerMode doit valoir manual, auto ou photo-button-window.";
        if (PhotoButtonPrintWindowSeconds <= 0)
            return "Printer.PhotoButtonPrintWindowSeconds doit etre > 0.";
        if (AutoPrintDelaySeconds < 0)
            return "Printer.AutoPrintDelaySeconds doit etre >= 0.";
        if (Copies <= 0)
            return "Printer.Copies doit etre > 0.";
        if (IsFile && string.IsNullOrWhiteSpace(OutputPath))
            return "Printer.OutputPath est obligatoire quand Printer.Type=file.";
        if (IsCups && string.IsNullOrWhiteSpace(LpCommand))
            return "Printer.LpCommand est obligatoire quand Printer.Type=cups.";
        return null;
    }
}
