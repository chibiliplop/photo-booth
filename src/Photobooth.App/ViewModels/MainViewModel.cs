using System;
using System.IO;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Photobooth.Core.Abstractions;
using Photobooth.Core.Options;

namespace Photobooth.App.ViewModels;

/// <summary>
/// The booth view-model and the workflow's <see cref="IPhotoDisplay"/>. Every IPhotoDisplay call comes
/// from a background thread and is marshaled onto the UI thread here — the workflow never touches UI state.
/// Reproduces the original 3-rotated-cards behaviour: each new message/photo lands on the next card and
/// that card is brought to the front.
/// </summary>
public sealed class MainViewModel : ViewModelBase, IPhotoDisplay
{
    private const int DisplayDecodeWidth = 800;
    private const string DefaultBackground = "avares://Photobooth.App/Assets/background.jpg";

    // Operator status colours (banner background + connectivity dot).
    private static readonly IBrush ReadyBrush   = new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71)); // vert
    private static readonly IBrush WarningBrush = new SolidColorBrush(Color.FromRgb(0xE6, 0x7E, 0x22)); // orange
    private static readonly IBrush ErrorBrush   = new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C)); // rouge
    private static readonly IBrush InfoBrush    = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0));   // pill sombre

    private readonly ILogger<MainViewModel> _log;
    private int _currentFrame;
    private bool _isRecording;
    private bool _isVideoCountdown;
    private string _videoCountdownText = string.Empty;
    private int _recordingTotal = 1;
    private int _recordingRemaining;
    private DispatcherTimer? _recordingTimer;
    private string? _status;
    private BoothStatusLevel _statusLevel = BoothStatusLevel.Info;
    private bool _hasConnectivity;
    private IBrush _connectivityBrush = Brushes.Transparent;
    private DispatcherTimer? _statusHideTimer;
    private string? _diagnostic;
    private bool _isIdle = true;

    public CardViewModel[] Cards { get; } = { new(), new(), new() };

    public string Names { get; }
    public string Year { get; }

    /// <summary>Design canvas the UI is laid out against; a Viewbox scales it to the real screen (see ThemeOptions.ScreenResolution).</summary>
    public double DesignWidth { get; }
    public double DesignHeight { get; }

    public Bitmap? Background { get; }
    public IImage? ArrowLeft { get; }
    public IImage? ArrowMiddle { get; }
    public IImage? ArrowRight { get; }

    public IBrush CardBrush { get; }
    public IBrush TitleBrush { get; }
    public IBrush MessageBrush { get; }
    public FontFamily TitleFont { get; }

    /// <summary>True while a take is being filmed: drives the blinking REC, the film overlay and the remaining-time countdown.</summary>
    public bool IsRecording
    {
        get => _isRecording;
        private set
        {
            if (SetField(ref _isRecording, value))
                Raise(nameof(IsVideoActive));
        }
    }

    /// <summary>True during the clapperboard count-in (3·2·1) that precedes filming.</summary>
    public bool IsVideoCountdown
    {
        get => _isVideoCountdown;
        private set
        {
            if (SetField(ref _isVideoCountdown, value))
                Raise(nameof(IsVideoActive));
        }
    }

    /// <summary>The big number on the clapperboard slate during the count-in.</summary>
    public string VideoCountdownText
    {
        get => _videoCountdownText;
        private set => SetField(ref _videoCountdownText, value);
    }

    /// <summary>Whether the full-screen cinema overlay (count-in or recording) is up. Hides the slideshow cards behind it.</summary>
    public bool IsVideoActive => _isRecording || _isVideoCountdown;

    /// <summary>Take length in seconds — the maximum value of the remaining-time bar.</summary>
    public int RecordingTotal
    {
        get => _recordingTotal;
        private set => SetField(ref _recordingTotal, value);
    }

    /// <summary>Seconds left in the current take, counting down to 0.</summary>
    public int RecordingRemaining
    {
        get => _recordingRemaining;
        private set
        {
            if (SetField(ref _recordingRemaining, value))
                Raise(nameof(RecordingRemainingText));
        }
    }

    /// <summary>The big reverse-countdown number shown while filming.</summary>
    public string RecordingRemainingText => _recordingRemaining.ToString();

    public string? Status
    {
        get => _status;
        private set
        {
            if (SetField(ref _status, value))
                Raise(nameof(HasStatus));
        }
    }

    public bool HasStatus => !string.IsNullOrEmpty(_status);

    /// <summary>Banner background colour, derived from the current status level.</summary>
    public IBrush StatusBrush => BrushFor(_statusLevel);

    /// <summary>Whether the persistent GoPro connectivity dot is shown (true once first known).</summary>
    public bool HasConnectivity
    {
        get => _hasConnectivity;
        private set => SetField(ref _hasConnectivity, value);
    }

    /// <summary>Colour of the persistent GoPro connectivity dot.</summary>
    public IBrush ConnectivityBrush
    {
        get => _connectivityBrush;
        private set => SetField(ref _connectivityBrush, value);
    }

    /// <summary>
    /// Persistent startup diagnostic (config invalide / GPIO inaccessible). Distinct from <see cref="Status"/>:
    /// it is set ONCE at boot by the App and the workflow never touches it, so GoPro connectivity messages
    /// can't overwrite it. Stays on screen until the operator fixes the problem and reboots.
    /// </summary>
    public string? Diagnostic
    {
        get => _diagnostic;
        private set
        {
            if (SetField(ref _diagnostic, value))
                Raise(nameof(HasDiagnostic));
        }
    }

    public bool HasDiagnostic => !string.IsNullOrEmpty(_diagnostic);

    /// <summary>#UI-3 — Overlay "Appuyez sur le bouton !" visible jusqu'au premier ShowMessage/ShowPhoto.</summary>
    public bool IsIdle
    {
        get => _isIdle;
        private set => SetField(ref _isIdle, value);
    }


    /// <summary>Show (or clear) the persistent startup diagnostic banner. Called by the App on the UI thread.</summary>
    public void ShowDiagnostic(string? message) =>
        Dispatcher.UIThread.Post(() => Diagnostic = message);

    public MainViewModel(IOptions<ThemeOptions> theme, ILogger<MainViewModel> log)
    {
        _log = log;
        var t = theme.Value;
        Names = t.Names;
        Year = t.Year;
        DesignWidth = t.DesignWidth;
        DesignHeight = t.DesignHeight;
        Background = LoadBitmap(t.BackgroundImage) ?? LoadBitmap(DefaultBackground);
        ArrowLeft = LoadBitmap("avares://Photobooth.App/Assets/arrow1.png");
        ArrowMiddle = LoadBitmap("avares://Photobooth.App/Assets/arrow2.png");
        ArrowRight = LoadBitmap("avares://Photobooth.App/Assets/arrow3.png");
        CardBrush = ParseBrush(t.CardColor, Brushes.AntiqueWhite);
        TitleBrush = ParseBrush(t.AccentColor, Brushes.Black);
        MessageBrush = ParseBrush(t.TextColor, Brushes.White);
        TitleFont = SafeFont(t.FontFamily);
    }

    // ---- IPhotoDisplay (always called off the UI thread) ----

    /// <summary>Fired on the UI thread when the shutter fires — code-behind wires the white flash to this.</summary>
    public event Action? FlashFired;

    public void Flash() =>
        Dispatcher.UIThread.Post(() => FlashFired?.Invoke());

    public void ShowMessage(string text) =>
        Dispatcher.UIThread.Post(() =>
        {
            ClearIdle();
            Advance(card =>
            {
                card.Message = text;
                card.IsTextVisible = true;
                card.IsImageVisible = false;
            });
        });

    public void ShowPhoto(byte[] imageData)
    {
        var bitmap = TryDecode(imageData);
        if (bitmap is null) return;
        Dispatcher.UIThread.Post(() =>
        {
            ClearIdle();
            Advance(card =>
            {
                var previous = card.Image;
                card.Image = bitmap;
                card.IsImageVisible = true;
                card.IsTextVisible = false;
                previous?.Dispose(); // release unmanaged Skia memory promptly (1 GB Pi)
            });
        });
    }

    public void ShowVideoCountdown(int seconds) =>
        Dispatcher.UIThread.Post(() =>
        {
            IsRecording = false;
            VideoCountdownText = seconds.ToString();
            IsVideoCountdown = true;
        });

    public void SetRecording(bool recording, int totalSeconds = 0) =>
        Dispatcher.UIThread.Post(() =>
        {
            if (recording) StartRecordingUi(totalSeconds);
            else StopRecordingUi();
        });

    private void StartRecordingUi(int totalSeconds)
    {
        IsVideoCountdown = false; // the count-in is over — cut to the film overlay
        RecordingTotal = Math.Max(1, totalSeconds);
        RecordingRemaining = RecordingTotal;
        IsRecording = true;

        EnsureRecordingTimer();
        _recordingTimer!.Stop();
        _recordingTimer.Start();
    }

    private void StopRecordingUi()
    {
        _recordingTimer?.Stop();
        IsRecording = false;
        IsVideoCountdown = false;
    }

    private void EnsureRecordingTimer()
    {
        if (_recordingTimer is not null) return;
        _recordingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _recordingTimer.Tick += (_, _) =>
        {
            if (RecordingRemaining > 0) RecordingRemaining--;
            if (RecordingRemaining <= 0) _recordingTimer!.Stop(); // workflow's auto-stop hides the overlay
        };
    }

    public void SetStatus(string? status, BoothStatusLevel level = BoothStatusLevel.Info) =>
        Dispatcher.UIThread.Post(() =>
        {
            _statusLevel = level;
            Raise(nameof(StatusBrush));
            Status = status;
            EnsureHideTimer();
            _statusHideTimer!.Stop();
            // A "ready" (green) banner is a confirmation, not a permanent label: auto-hide it so the
            // booth screen stays clean. Orange/red persist until the situation actually changes.
            if (!string.IsNullOrEmpty(status) && level == BoothStatusLevel.Ready)
                _statusHideTimer.Start();
        });

    public void SetConnectivity(BoothStatusLevel level) =>
        Dispatcher.UIThread.Post(() =>
        {
            ConnectivityBrush = BrushFor(level);
            HasConnectivity = true;
        });

    // ---- internals ----

    private void ClearIdle() { if (_isIdle) IsIdle = false; }

    private void EnsureHideTimer()
    {
        if (_statusHideTimer is not null) return;
        _statusHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _statusHideTimer.Tick += (_, _) =>
        {
            _statusHideTimer!.Stop();
            Status = null; // clears the green confirmation banner; the connectivity dot stays.
        };
    }

    private static IBrush BrushFor(BoothStatusLevel level) => level switch
    {
        BoothStatusLevel.Ready => ReadyBrush,
        BoothStatusLevel.Warning => WarningBrush,
        BoothStatusLevel.Error => ErrorBrush,
        _ => InfoBrush,
    };

    private void Advance(Action<CardViewModel> apply)
    {
        _currentFrame = (_currentFrame + 1) % Cards.Length;
        apply(Cards[_currentFrame]);
        // 100 = devant (nouvelle carte), 50 = milieu (précédente), 1 = derrière (plus ancienne).
        // La carte qui vient de passer devant ne disparaît plus instantanément derrière les deux autres :
        // elle reste au milieu jusqu'à la prochaine rotation.
        var prev   = (_currentFrame + Cards.Length - 1) % Cards.Length;
        var oldest = (_currentFrame + 1)                % Cards.Length;
        Cards[_currentFrame].ZIndex = 100;
        Cards[prev].ZIndex          = 50;
        Cards[oldest].ZIndex        = 1;
    }

    private Bitmap? TryDecode(byte[] data)
    {
        try
        {
            using var ms = new MemoryStream(data);
            return Bitmap.DecodeToWidth(ms, DisplayDecodeWidth);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Failed to decode image ({Bytes} bytes).", data.Length);
            return null;
        }
    }

    private Bitmap? LoadBitmap(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;
            if (path.StartsWith("avares://", StringComparison.OrdinalIgnoreCase))
                return new Bitmap(AssetLoader.Open(new Uri(path)));
            if (File.Exists(path))
                return new Bitmap(path);
            _log.LogWarning("Background/asset not found: {Path}", path);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to load image asset: {Path}", path);
        }
        return null;
    }

    private static IBrush ParseBrush(string value, IBrush fallback)
    {
        try { return Brush.Parse(value); }
        catch { return fallback; }
    }

    private FontFamily SafeFont(string family)
    {
        try { return new FontFamily(family); }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to load font {Font}; using default.", family);
            return FontFamily.Default;
        }
    }
}
