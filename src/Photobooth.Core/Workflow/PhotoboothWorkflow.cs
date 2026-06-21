using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Photobooth.Core.Abstractions;
using Photobooth.Core.GoPro;
using Photobooth.Core.Options;
using Photobooth.Core.Resilience;

namespace Photobooth.Core.Workflow;

/// <summary>
/// The photobooth orchestrator, implemented as an actor: a single consumer drains a command
/// channel and is the ONLY writer of <see cref="State"/>. This structurally removes every race the
/// original UWP code had. The workflow NEVER runs on the UI thread; it only pushes render requests to
/// <see cref="IPhotoDisplay"/> (which marshals to the UI thread itself).
///
/// Two ticks run on independent loops so a slow capture can't starve them:
///  - the GoPro UDP keepalive (must never pause, or the camera sleeps);
///  - the idle slideshow.
/// A single <see cref="SemaphoreSlim"/> serializes GoPro media access between the slideshow and a capture.
/// </summary>
public sealed class PhotoboothWorkflow : IAsyncDisposable
{
    private readonly IGoProClient _gopro;
    private readonly ILightOutput _light;
    private readonly IPhotoDisplay _display;
    private readonly TimingOptions _timings;
    private readonly GoProOptions _goproOpt;
    private readonly ILogger<PhotoboothWorkflow> _log;

    private readonly Channel<BoothCommand> _channel;
    private readonly SemaphoreSlim _goproGate = new(1, 1);
    private readonly Random _rand = new();

    private int _stateValue = (int)BoothState.Idle;
    private long _recordingEpoch;
    private bool _started;
    private CancellationTokenSource? _lifetimeCts;
    private Task? _consumerTask;
    private Task? _keepAliveTask;
    private Task? _slideshowTask;
    private Task? _connectivityTask;

    public PhotoboothWorkflow(
        IGoProClient gopro,
        ILightOutput light,
        IPhotoDisplay display,
        IOptions<TimingOptions> timings,
        IOptions<GoProOptions> goproOptions,
        ILogger<PhotoboothWorkflow> log)
    {
        _gopro = gopro;
        _light = light;
        _display = display;
        _timings = timings.Value;
        _goproOpt = goproOptions.Value;
        _log = log;
        _channel = Channel.CreateUnbounded<BoothCommand>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    }

    /// <summary>Current booth state. Single-writer (consumer loop); safe to read from any thread.</summary>
    public BoothState State => (BoothState)Volatile.Read(ref _stateValue);

    private void SetState(BoothState s)
    {
        Volatile.Write(ref _stateValue, (int)s);
        _log.LogDebug("State -> {State}", s);
    }

    /// <summary>Enqueue a command for the consumer. Thread-safe; never blocks.</summary>
    public void Submit(BoothCommand command) => _channel.Writer.TryWrite(command);

    public Task StartAsync(CancellationToken externalCt = default)
    {
        if (_started) return Task.CompletedTask;
        _started = true;
        _lifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
        var ct = _lifetimeCts.Token;
        _consumerTask = Task.Run(() => ConsumeLoopAsync(ct), CancellationToken.None);
        _keepAliveTask = Task.Run(() => KeepAliveLoopAsync(ct), CancellationToken.None);
        _slideshowTask = Task.Run(() => SlideshowLoopAsync(ct), CancellationToken.None);
        _connectivityTask = Task.Run(() => ConnectivityLoopAsync(ct), CancellationToken.None);
        _log.LogInformation("Photobooth workflow started.");
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (!_started) return;
        _log.LogInformation("Photobooth workflow stopping.");
        _channel.Writer.TryComplete();
        _lifetimeCts?.Cancel();
        var tasks = new[] { _consumerTask, _keepAliveTask, _slideshowTask, _connectivityTask }.Where(t => t is not null).Cast<Task>().ToArray();
        try { await Task.WhenAll(tasks); }
        catch (OperationCanceledException) { /* expected on shutdown */ }
        catch (Exception ex) { _log.LogWarning(ex, "Error awaiting loops during shutdown."); }
        try { _light.Off(); } catch (Exception ex) { _log.LogWarning(ex, "Turning light off on shutdown failed."); }
        SetState(BoothState.ShuttingDown);
        _started = false;
    }

    // ---- Consumer ---------------------------------------------------------

    private async Task ConsumeLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var cmd in _channel.Reader.ReadAllAsync(ct))
            {
                try
                {
                    await HandleAsync(cmd, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Unhandled error processing {Command}; forcing safe reset.", cmd.GetType().Name);
                    SafeReset();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }

    private async Task HandleAsync(BoothCommand cmd, CancellationToken lifetime)
    {
        switch (cmd)
        {
            case BoothCommand.PhotoRequested:
                if (State is BoothState.Idle or BoothState.Degraded)
                    await RunPhotoSequenceAsync(lifetime);
                else
                    _log.LogDebug("Photo press ignored in state {State}.", State);
                break;

            case BoothCommand.VideoToggleRequested:
                await ToggleVideoAsync(lifetime);
                break;

            case BoothCommand.VideoAutoStop autoStop:
                if (State == BoothState.Recording && autoStop.Epoch == _recordingEpoch)
                    await StopRecordingAsync(lifetime);
                break;

            case BoothCommand.Recovered:
                if (State == BoothState.Degraded)
                {
                    SetState(BoothState.Idle);
                    _display.SetStatus(null);
                    _display.SetConnectivity(BoothStatusLevel.Ready); // dot back to green immediately
                    _log.LogInformation("GoPro recovered; back to Idle.");
                }
                break;

            case BoothCommand.Shutdown:
                SetState(BoothState.ShuttingDown);
                _light.Off();
                break;
        }
    }

    private void SafeReset()
    {
        try { _light.Off(); } catch { /* best effort */ }
        if (State is not BoothState.ShuttingDown) SetState(BoothState.Idle);
    }

    /// <summary>
    /// Discard photo/video button presses that piled up while a sequence was running, so a guest
    /// mashing the button gets ONE photo, not a queue of them. Non-button commands (auto-stop,
    /// recovery, shutdown) are preserved. Safe: the consumer is the only reader.
    /// </summary>
    private void DrainButtonCommands()
    {
        List<BoothCommand>? keep = null;
        while (_channel.Reader.TryRead(out var c))
        {
            if (c is BoothCommand.PhotoRequested or BoothCommand.VideoToggleRequested)
                continue; // drop
            (keep ??= new List<BoothCommand>()).Add(c);
        }
        if (keep is null) return;
        foreach (var c in keep) _channel.Writer.TryWrite(c);
    }

    // ---- Photo sequence ---------------------------------------------------

    private async Task RunPhotoSequenceAsync(CancellationToken lifetime)
    {
        SetState(BoothState.Capturing);
        using var watchdog = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        watchdog.CancelAfter(TimeSpan.FromSeconds(_timings.WatchdogSeconds));
        var ct = watchdog.Token;
        try
        {
            _display.SetStatus(null);
            _display.ShowMessage("Prenez la pose");
            await Task.Delay(_timings.PoseMs, ct);
            _display.ShowMessage("3");
            await Task.Delay(_timings.CountdownStepMs, ct);
            _display.ShowMessage("2");
            await Task.Delay(_timings.CountdownStepMs, ct);
            _display.ShowMessage("1");
            await Task.Delay(_timings.CountdownStepMs, ct);
            _display.ShowMessage("Souriez");

            byte[] photo;
            await _goproGate.WaitAsync(ct);
            try
            {
                // Snapshot BEFORE the light/trigger so we can reliably tell the new photo from old ones.
                var before = await SnapshotFileNamesAsync(ct);
                _light.On();
                await _gopro.SetSinglePhotoModeAsync(ct);
                await _gopro.TriggerAsync(ct);
                await Task.Delay(_timings.LightSettleMs, ct);
                _light.Off();
                _display.ShowMessage("La photo\narrive...");
                var (dir, file) = await WaitForNewPhotoAsync(before, ct);
                photo = await _gopro.DownloadMediaAsync(dir, file, ct);
            }
            finally
            {
                _light.Off();
                _goproGate.Release();
            }

            _display.ShowPhoto(photo);
            await Task.Delay(_timings.PhotoDisplayMs, ct);
            SetState(BoothState.Idle);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            _light.Off();
            throw; // shutting down — let the consumer loop exit
        }
        catch (OperationCanceledException)
        {
            _log.LogError("Photo sequence exceeded the {Watchdog}s watchdog; forcing reset.", _timings.WatchdogSeconds);
            _light.Off();
            _display.SetStatus("Réessayez", BoothStatusLevel.Warning);
            SetState(BoothState.Idle);
        }
        catch (GoProUnavailableException ex)
        {
            _log.LogWarning(ex, "GoPro unavailable during capture; entering Degraded.");
            _light.Off();
            _display.SetStatus("GoPro indisponible", BoothStatusLevel.Error);
            SetState(BoothState.Degraded);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Photo sequence failed unexpectedly.");
            _light.Off();
            SetState(BoothState.Idle);
        }

        DrainButtonCommands(); // ignore presses that arrived during this sequence
    }

    private async Task<HashSet<string>> SnapshotFileNamesAsync(CancellationToken ct)
    {
        var media = await _gopro.ListMediaAsync(ct);
        return new HashSet<string>(media.AllFileNames(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Poll the media list until a NEW non-video file appears (vs the pre-trigger snapshot), within the
    /// capture deadline. On timeout we throw <see cref="GoProUnavailableException"/> rather than ever
    /// returning a stale (previous guest's) photo.
    /// </summary>
    private async Task<(string Directory, string FileName)> WaitForNewPhotoAsync(HashSet<string> before, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(_goproOpt.CaptureDeadlineSeconds));
        var dct = deadline.Token;
        try
        {
            while (true)
            {
                var media = await _gopro.ListMediaAsync(dct);
                foreach (var dir in media.Media)
                {
                    foreach (var file in dir.FileSystem)
                    {
                        if (!file.IsVideo && !before.Contains(file.FileName))
                            return (dir.Directory, file.FileName);
                    }
                }
                await Task.Delay(_goproOpt.RetryBackoffMs, dct);
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new GoProUnavailableException("No new photo became available within the capture deadline.");
        }
    }

    // ---- Video ------------------------------------------------------------

    private async Task ToggleVideoAsync(CancellationToken lifetime)
    {
        if (State == BoothState.Recording)
        {
            await StopRecordingAsync(lifetime);
            return;
        }
        if (State is not (BoothState.Idle or BoothState.Degraded))
            return;

        using var watchdog = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        watchdog.CancelAfter(TimeSpan.FromSeconds(_timings.WatchdogSeconds));
        var ct = watchdog.Token;
        try
        {
            // Cinematic count-in so guests know EXACTLY when filming starts (the clapperboard claps on
            // each beat). We hold the booth in Capturing during the count-in: this blocks the slideshow
            // and makes extra button presses no-ops, exactly like the photo sequence.
            SetState(BoothState.Capturing);
            _display.SetStatus(null);
            for (var n = _timings.VideoCountdownSeconds; n >= 1; n--)
            {
                _display.ShowVideoCountdown(n);
                await Task.Delay(_timings.CountdownStepMs, ct);
            }
            // Drop button presses that piled up during the count-in so a double-press doesn't immediately
            // stop the take we're about to start (presses AFTER recording begins still stop it normally).
            DrainButtonCommands();

            await _goproGate.WaitAsync(ct);
            try
            {
                await _gopro.SetVideoModeAsync(ct);
                await _gopro.TriggerAsync(ct);
            }
            finally
            {
                _goproGate.Release();
            }

            SetState(BoothState.Recording);
            _display.SetStatus(null);
            _display.SetRecording(true, _timings.VideoMaxSeconds);
            var epoch = ++_recordingEpoch;
            _ = AutoStopAsync(epoch, lifetime);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            _display.SetRecording(false);
            throw;
        }
        catch (GoProUnavailableException ex)
        {
            _log.LogWarning(ex, "GoPro unavailable starting video; entering Degraded.");
            _display.SetRecording(false);
            _display.SetStatus("GoPro indisponible", BoothStatusLevel.Error);
            SetState(BoothState.Degraded);
        }
        catch (OperationCanceledException)
        {
            _log.LogError("Start video exceeded the {Watchdog}s watchdog; forcing reset.", _timings.WatchdogSeconds);
            _display.SetRecording(false);
            _display.SetStatus("Réessayez", BoothStatusLevel.Warning);
            SetState(BoothState.Idle);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Start video failed.");
            _display.SetRecording(false);
            SetState(BoothState.Idle);
        }
    }

    private async Task AutoStopAsync(long epoch, CancellationToken lifetime)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(_timings.VideoMaxSeconds), lifetime); }
        catch (OperationCanceledException) { return; }
        Submit(new BoothCommand.VideoAutoStop(epoch));
    }

    private async Task StopRecordingAsync(CancellationToken lifetime)
    {
        _recordingEpoch++; // invalidate any pending auto-stop for this take
        using var watchdog = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        watchdog.CancelAfter(TimeSpan.FromSeconds(_timings.WatchdogSeconds));
        var ct = watchdog.Token;
        try
        {
            await _goproGate.WaitAsync(ct);
            try { await _gopro.StopAsync(ct); }
            finally { _goproGate.Release(); }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Stop video failed (continuing).");
        }
        _display.SetRecording(false);
        if (State == BoothState.Recording)
            SetState(BoothState.Idle);

        DrainButtonCommands(); // ignore presses that arrived during recording
    }

    // ---- Independent loops ------------------------------------------------

    private async Task KeepAliveLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_goproOpt.KeepAliveIntervalSeconds));
        while (true)
        {
            try { if (!await timer.WaitForNextTickAsync(ct)) break; }
            catch (OperationCanceledException) { break; }
            try { await _gopro.SendKeepAliveAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { _log.LogDebug(ex, "Keepalive failed (ignored)."); }
        }
    }

    /// <summary>
    /// Ambient GoPro connectivity monitor. Cheap reachability probe on a slow timer; the SOLE writer of
    /// the persistent connectivity dot. Acts only on TRANSITIONS (no banner flicker) and only while
    /// Idle/Degraded (never fights a running capture/recording, which owns the screen). On a fresh
    /// connection it nudges the workflow out of Degraded faster than the slideshow would.
    /// </summary>
    private async Task ConnectivityLoopAsync(CancellationToken ct)
    {
        var everConnected = false;
        bool? last = null;
        var interval = TimeSpan.FromSeconds(Math.Max(1, _timings.StatusPollSeconds));
        while (!ct.IsCancellationRequested)
        {
            // Probe immediately on the first iteration so the operator sees a status right after boot.
            if (State is BoothState.Idle or BoothState.Degraded)
            {
                bool reachable;
                try { reachable = await _gopro.IsReachableAsync(ct); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                catch { reachable = false; }

                if (reachable != last)
                {
                    last = reachable;
                    if (reachable)
                    {
                        everConnected = true;
                        _display.SetConnectivity(BoothStatusLevel.Ready);
                        _display.SetStatus("GoPro connectée", BoothStatusLevel.Ready); // green auto-hides
                        if (State == BoothState.Degraded) Submit(new BoothCommand.Recovered());
                    }
                    else
                    {
                        // Orange before we've ever connected ("still trying"); red once a known camera drops.
                        var level = everConnected ? BoothStatusLevel.Error : BoothStatusLevel.Warning;
                        _display.SetConnectivity(level);
                        _display.SetStatus(everConnected ? "GoPro indisponible" : "Connexion à la GoPro…", level);
                    }
                }
            }

            try { await Task.Delay(interval, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task SlideshowLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_timings.SlideshowIntervalSeconds));
        while (true)
        {
            try { if (!await timer.WaitForNextTickAsync(ct)) break; }
            catch (OperationCanceledException) { break; }

            // Run while Idle or Degraded (a successful fetch while Degraded is how we auto-recover).
            if (State is not (BoothState.Idle or BoothState.Degraded)) continue;
            if (!_goproGate.Wait(0)) continue; // capture is using the camera — skip this tick
            try
            {
                await ShowRandomSlideAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { _log.LogDebug(ex, "Slideshow tick failed (ignored)."); }
            finally { _goproGate.Release(); }
        }
    }

    private async Task ShowRandomSlideAsync(CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_goproOpt.RequestTimeoutSeconds + 1));

        GoProMedia media;
        try { media = await _gopro.ListMediaAsync(cts.Token); }
        catch (GoProUnavailableException) { return; } // stay quiet during the slideshow

        var dir = media.Media.FirstOrDefault();
        if (dir is null) return;
        var images = dir.FileSystem.Where(f => !f.IsVideo).ToList();
        if (images.Count == 0) return;

        var pick = images[_rand.Next(images.Count)];
        byte[] data;
        try { data = await _gopro.DownloadMediaAsync(dir.Directory, pick.FileName, cts.Token); }
        catch (GoProUnavailableException) { return; }

        var state = State;
        if (state is BoothState.Idle or BoothState.Degraded)
            _display.ShowPhoto(data);
        if (state == BoothState.Degraded)
            Submit(new BoothCommand.Recovered());
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _lifetimeCts?.Dispose();
        _goproGate.Dispose();
    }
}
