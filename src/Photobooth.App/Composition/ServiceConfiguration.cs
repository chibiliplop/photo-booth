using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Avalonia.Platform;
using Photobooth.Adapters.GoPro;
using Photobooth.Adapters.Printing;
using Photobooth.App.ViewModels;
using Photobooth.Core.Abstractions;
using Photobooth.Core.Diagnostics;
using Photobooth.Core.Options;
using Photobooth.Core.Workflow;
using Serilog;

namespace Photobooth.App.Composition;

internal static class ServiceConfiguration
{
    public static IServiceCollection AddPhotobooth(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<GoProOptions>(config.GetSection(GoProOptions.Section));
        services.Configure<HardwareOptions>(config.GetSection(HardwareOptions.Section));
        services.Configure<TimingOptions>(config.GetSection(TimingOptions.Section));
        services.Configure<ThemeOptions>(config.GetSection(ThemeOptions.Section));
        services.Configure<PrinterOptions>(config.GetSection(PrinterOptions.Section));

        services.AddLogging(b => b.AddSerilog(dispose: false));

        // The view-model IS the display surface.
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<IPhotoDisplay>(sp => sp.GetRequiredService<MainViewModel>());

        // GoPro client: fake (no network) or real HTTP.
        var goproMode = config.GetSection(GoProOptions.Section)["Mode"] ?? "http";
        if (string.Equals(goproMode, "fake", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IGoProClient>(CreateFakeGoPro);
        else
            services.AddSingleton<IGoProClient, HttpGoProClient>();

        // Printing: disabled by default; CUPS is the normal Linux path, file is useful for tests/export.
        var printerType = config.GetSection(PrinterOptions.Section)["Type"] ?? "disabled";
        if (string.Equals(printerType, "cups", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IPrinterAdapter, CupsPrinterAdapter>();
        else if (string.Equals(printerType, "file", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IPrinterAdapter, FilePrinterAdapter>();
        else
            services.AddSingleton<IPrinterAdapter, NoOpPrinterAdapter>();

        // Hardware (single resolution with graceful fallback).
        services.AddSingleton<HardwareBundle>(HardwareBundle.Create);
        services.AddSingleton<IButtonInput>(sp => sp.GetRequiredService<HardwareBundle>().Button);
        services.AddSingleton<ILightOutput>(sp => sp.GetRequiredService<HardwareBundle>().Light);
        services.AddSingleton<ILightSensor>(sp => sp.GetRequiredService<HardwareBundle>().Sensor);

        services.AddSingleton<BoothTelemetry>();
        services.AddSingleton<PhotoboothWorkflow>();
        return services;
    }

    public static string? ValidateOptions(IServiceProvider sp)
    {
        return sp.GetRequiredService<IOptions<GoProOptions>>().Value.Validate()
            ?? sp.GetRequiredService<IOptions<HardwareOptions>>().Value.Validate()
            ?? sp.GetRequiredService<IOptions<TimingOptions>>().Value.Validate()
            ?? sp.GetRequiredService<IOptions<ThemeOptions>>().Value.Validate()
            ?? sp.GetRequiredService<IOptions<PrinterOptions>>().Value.Validate();
    }

    private static FakeGoProClient CreateFakeGoPro(IServiceProvider sp)
    {
        var theme = sp.GetRequiredService<IOptions<ThemeOptions>>().Value;
        var log = sp.GetRequiredService<ILogger<FakeGoProClient>>();
        var samples = new List<byte[]>();
        foreach (var path in new[] { theme.FakePhotoImage, theme.BackgroundImage })
        {
            var bytes = TryLoadBytes(path);
            if (bytes is { Length: > 0 }) samples.Add(bytes);
        }
        return new FakeGoProClient(samples, log);
    }

    private static byte[]? TryLoadBytes(string path)
    {
        try
        {
            if (path.StartsWith("avares://", StringComparison.OrdinalIgnoreCase))
            {
                using var s = AssetLoader.Open(new Uri(path));
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                return ms.ToArray();
            }
            if (File.Exists(path)) return File.ReadAllBytes(path);
        }
        catch
        {
            // ignored — fake client falls back to a placeholder
        }
        return null;
    }
}
