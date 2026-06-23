using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Photobooth.Admin;
using Photobooth.App.Composition;
using Photobooth.App.ViewModels;
using Photobooth.App.Views;
using Photobooth.Core.Abstractions;
using Photobooth.Core.Workflow;
using Serilog;

namespace Photobooth.App;

public partial class App : Application
{
    private PhotoboothWorkflow? _workflow;
    private readonly StartupNoticeGate _noticeGate = new();

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // ==========================================
        // DESIGN TIME: Prevent previewer crashes
        // ==========================================
        if (Design.IsDesignMode)
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime designDesktop)
            {
                designDesktop.MainWindow = new MainWindow(); 
            }
            else if (ApplicationLifetime is ISingleViewApplicationLifetime designSingleView)
            {
                designSingleView.MainView = new MainView();
            }
            
            base.OnFrameworkInitializationCompleted();
            return; // Exit early! Do not run the DI or hardware logic below.
        }

        // ==========================================
        // RUNTIME: Normal application execution
        // ==========================================
        var sp = Program.Services;
        var vm = sp.GetRequiredService<MainViewModel>();
        var workflow = sp.GetRequiredService<PhotoboothWorkflow>();
        var hardware = sp.GetRequiredService<HardwareBundle>();
        var buttons = hardware.Button;
        _workflow = workflow;

        // Decision admin lue une fois : sert a l'ecran d'accueil (ci-dessous) et au demarrage de l'hote.
        var adminOpt = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Photobooth.Core.Options.AdminOptions>>().Value;

        // 1re action si l'admin est active : afficher l'URL a ouvrir (D5). Arme AVANT le demarrage des
        // boutons pour qu'aucun appui ne soit capture avant la fermeture de l'ecran.
        if (adminOpt.Enabled && adminOpt.ShowAddressOnStartup)
        {
            var urls = Photobooth.Admin.AdminAddress.LocalUrls(adminOpt.Port);
            vm.ShowAdminAddress(urls.Count > 0
                ? string.Join("\n", urls)
                : "Adresse introuvable — vérifiez le réseau.");
            _noticeGate.Arm();
        }

        Control? root = null;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow { DataContext = vm };
            if (Program.Fullscreen)
            {
                window.WindowState = WindowState.FullScreen;
                window.SystemDecorations = SystemDecorations.None;
            }
            window.KeyDown += (_, e) => OnKey(e, vm, workflow);
            desktop.MainWindow = window;
            desktop.ShutdownRequested += (_, _) => ShutdownWorkflow();
            root = window;
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            var view = new MainView { DataContext = vm };
            view.KeyDown += (_, e) => OnKey(e, vm, workflow);
            singleView.MainView = view;
            root = view;
        }

        // Critical startup problems the operator must see on the kiosk screen (their only output):
        // an invalid edited config (photobooth.json), or GPIO that was expected but couldn't initialise.
        var diagnostic = ServiceConfiguration.ValidateOptions(sp) ?? hardware.StartupWarning;

        // Route hardware buttons (and keyboard) to the workflow's command channel.
        // Tant que l'ecran d'accueil admin est affiche (_noticeGate), le 1er appui photo le ferme
        // sans capturer, et les appuis video/impression sont ignores.
        buttons.PhotoPressed += () => SubmitPhoto(vm, workflow);
        buttons.VideoPressed += () => { if (!_noticeGate.Pending) workflow.Submit(new BoothCommand.VideoToggleRequested()); };
        buttons.PrintPressed += () => { if (!_noticeGate.Pending) workflow.Submit(new BoothCommand.PrintRequested()); };
        try
        {
            buttons.Start();
        }
        catch (Exception ex)
        {
            // Real GPIO can fail at Start() (callback registration) even after Create() succeeded — keep the
            // booth alive on keyboard and tell the operator rather than crash.
            Log.Error(ex, "Démarrage des boutons GPIO échoué ; le clavier reste utilisable.");
            diagnostic ??= "Boutons GPIO inaccessibles : la borne fonctionne au clavier uniquement. " +
                           "Vérifiez le câblage et les droits d'accès (groupes gpio/i2c).";
        }
        _ = workflow.StartAsync();

        // Hôte d'admin/debug optionnel (off par défaut). Tout échec est dégradé, jamais fatal :
        // la borne ne doit jamais tomber à cause du mode debug. Le conteneur (IAsyncDisposable)
        // arrête l'hôte à l'extinction, comme le workflow.
        try
        {
            if (adminOpt.Enabled)
                _ = sp.GetRequiredService<AdminWebHost>().StartAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Démarrage de l'hôte admin ignoré (mode dégradé).");
        }

        if (diagnostic is not null)
        {
            Log.Error("Diagnostic de démarrage affiché à l'écran : {Diagnostic}", diagnostic);
            vm.ShowDiagnostic(diagnostic);
        }

        if (Program.ScreenshotPath is { } shot && root is not null)
            ScheduleVerificationScreenshots(root, workflow, shot);

        if (Program.ScreenshotVideoPath is { } vshot && root is not null)
            ScheduleVideoVerificationScreenshots(root, workflow, vshot);

        base.OnFrameworkInitializationCompleted();
    }

    private void OnKey(KeyEventArgs e, MainViewModel vm, PhotoboothWorkflow workflow)
    {
        switch (e.Key)
        {
            case Key.Space:
            case Key.Enter:
                SubmitPhoto(vm, workflow);
                break;
            case Key.V:
                if (!_noticeGate.Pending) workflow.Submit(new BoothCommand.VideoToggleRequested());
                break;
            case Key.P:
                if (!_noticeGate.Pending) workflow.Submit(new BoothCommand.PrintRequested());
                break;
        }
    }

    // 1er appui pendant l'ecran d'accueil admin = fermeture (pas de capture) ; sinon capture normale.
    private void SubmitPhoto(MainViewModel vm, PhotoboothWorkflow workflow)
    {
        if (_noticeGate.ConsumePress())
        {
            vm.ClearAdminAddress();
            return;
        }
        workflow.Submit(new BoothCommand.PhotoRequested());
    }

    private void ShutdownWorkflow()
    {
        try { _workflow?.StopAsync().GetAwaiter().GetResult(); }
        catch (Exception ex) { Log.Warning(ex, "Error stopping workflow on shutdown."); }
    }

    // Dev/CI only: trigger a capture, then snapshot the countdown and the displayed photo, then exit.
    private void ScheduleVerificationScreenshots(Control root, PhotoboothWorkflow workflow, string path)
    {
        DispatcherTimer.RunOnce(() => workflow.Submit(new BoothCommand.PhotoRequested()), TimeSpan.FromSeconds(0.5));
        DispatcherTimer.RunOnce(() => Capture(root, Suffix(path, "-countdown")), TimeSpan.FromSeconds(2.5));
        DispatcherTimer.RunOnce(() =>
        {
            Capture(root, path);
            (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        }, TimeSpan.FromSeconds(8.0));
    }

    // Dev/CI only: toggle video, then snapshot the clapperboard count-in and the recording film overlay, then exit.
    // Assumes the default timings (count-in 3×1s, then recording): capture mid count-in, then mid take.
    private void ScheduleVideoVerificationScreenshots(Control root, PhotoboothWorkflow workflow, string path)
    {
        DispatcherTimer.RunOnce(() => workflow.Submit(new BoothCommand.VideoToggleRequested()), TimeSpan.FromSeconds(0.5));
        DispatcherTimer.RunOnce(() => Capture(root, Suffix(path, "-countin")), TimeSpan.FromSeconds(2.0));
        DispatcherTimer.RunOnce(() =>
        {
            Capture(root, Suffix(path, "-recording"));
            (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        }, TimeSpan.FromSeconds(5.0));
    }

    private static void Capture(Control root, string path)
    {
        try
        {
            var w = Math.Max(1, (int)root.Bounds.Width);
            var h = Math.Max(1, (int)root.Bounds.Height);
            using var rtb = new RenderTargetBitmap(new PixelSize(w, h), new Vector(96, 96));
            rtb.Render(root);
            var full = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            using (var fs = File.Create(full))
                rtb.Save(fs);
            Log.Information("Screenshot saved: {Path} ({W}x{H}).", full, w, h);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Screenshot capture failed.");
        }
    }

    private static string Suffix(string path, string suffix)
    {
        var dir = Path.GetDirectoryName(path) ?? ".";
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        return Path.Combine(dir, name + suffix + ext);
    }
}
