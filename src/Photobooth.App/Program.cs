using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.LinuxFramebuffer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Photobooth.Admin;
using Photobooth.App.Composition;
using Serilog;

namespace Photobooth.App;

internal static class Program
{
    public static IServiceProvider Services { get; private set; } = null!;
    public static bool Fullscreen { get; private set; }
    public static string? ScreenshotPath { get; private set; }
    public static string? ScreenshotVideoPath { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        Fullscreen = args.Contains("--fullscreen") || args.Contains("--drm") || args.Contains("--fbdev");
        ScreenshotPath = ParseValue(args, "--screenshot");
        ScreenshotVideoPath = ParseValue(args, "--screenshot-video");

        var baseDir = AppContext.BaseDirectory;

        // Surcharge par événement, éditée par un opérateur NON-TECH sur la partition FAT32
        // du Pi (visible dans l'explorateur Windows/Mac). Empilée APRÈS l'appsettings.json
        // embarqué et AVANT les variables d'env. On n'ajoute le fichier que s'il EXISTE :
        // un dossier/fichier absent ne lève jamais d'exception (identique Pi / dev Windows).
        // PHOTOBOOTH_CONFIG_DIR permet de pointer ailleurs (tests). Repli dev: ./config.
        var externalConfigDir = Environment.GetEnvironmentVariable("PHOTOBOOTH_CONFIG_DIR")
            ?? "/boot/firmware/photobooth";

        var configBuilder = new ConfigurationBuilder()
            .SetBasePath(baseDir)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);

        foreach (var dir in new[] { externalConfigDir, Path.Combine(baseDir, "config") })
        {
            var overridePath = Path.Combine(dir, "photobooth.json");
            if (File.Exists(overridePath))
                configBuilder.AddJsonFile(overridePath, optional: true, reloadOnChange: false);
        }

        var config = configBuilder
            .AddEnvironmentVariables(prefix: "PHOTOBOOTH_") // e.g. PHOTOBOOTH_Gopro__Mode=http
            .Build();

        var logBuffer = new InMemoryLogSink();
        ConfigureSerilog(config, baseDir, logBuffer);

        try
        {
            var sc = new ServiceCollection();
            sc.AddPhotobooth(config);
            sc.AddSingleton(logBuffer);
            Services = sc.BuildServiceProvider();

            var error = ServiceConfiguration.ValidateOptions(Services);
            if (error is not null)
            {
                // Un opérateur non-tech peut saisir une valeur invalide dans photobooth.json.
                // Ne JAMAIS renvoyer un code non nul ici : avec systemd Restart=always, cela
                // crée un crash-loop écran noir impossible à diagnostiquer sur site. On dégrade :
                // log + on continue (GoPro injoignable et GPIO absent s'auto-réparent déjà).
                Log.Error("Configuration invalide, démarrage en mode dégradé : {Error}", error);
            }

            var builder = BuildAvaloniaApp();
            if (args.Contains("--fbdev"))
                // Fallback logiciel si DRM/KMS affiche un écran noir sur un matériel donné.
                return builder.StartLinuxFbDev(args);

            if (args.Contains("--drm"))
                // Kiosk Pi validé : DRM/KMS est accéléré GPU et nettement plus fluide.
                return builder.StartLinuxDrm(args, null, 1.0);

            return builder.StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Photobooth terminated unexpectedly.");
            return 1;
        }
        finally
        {
            // The workflow is IAsyncDisposable, so the container must be disposed asynchronously
            // (a synchronous Dispose() throws). DisposeAsync also runs StopAsync -> light off.
            if (Services is IAsyncDisposable asyncProvider)
                asyncProvider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            else
                (Services as IDisposable)?.Dispose();
            Log.CloseAndFlush();
        }
    }

    // Avalonia design-time / tooling entry point.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();

    private static void ConfigureSerilog(IConfiguration config, string baseDir, InMemoryLogSink logBuffer)
    {
        var configured = config["Logging:File:Path"] ?? "logs/booth-.log";
        var logPath = Path.IsPathRooted(configured) ? configured : Path.Combine(baseDir, configured);
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.Sink(logBuffer)
            .WriteTo.File(
                logPath,
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: 5 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: 3,
                shared: true)
            .CreateLogger();
    }

    private static string? ParseValue(string[] args, string name)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == name && i + 1 < args.Length) return args[i + 1];
            if (args[i].StartsWith(name + "=", StringComparison.Ordinal)) return args[i][(name.Length + 1)..];
        }
        return null;
    }
}
