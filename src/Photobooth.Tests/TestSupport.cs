using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Photobooth.Adapters.Hardware.Fake;
using Photobooth.Core.Abstractions;
using Photobooth.Core.GoPro;
using Photobooth.Core.Options;
using Photobooth.Core.Resilience;
using Photobooth.Core.Workflow;

namespace Photobooth.Tests;

/// <summary>Captures what the workflow asked the UI to render. Thread-safe (workflow calls from a background thread).</summary>
internal sealed class RecordingDisplay : IPhotoDisplay
{
    private readonly object _lock = new();
    private readonly List<string> _messages = new();
    private int _photoCount;

    private readonly List<int> _videoCountdowns = new();

    public bool? Recording { get; private set; }
    public int? RecordingTotalSeconds { get; private set; }
    public string? Status { get; private set; }
    public BoothStatusLevel? Connectivity { get; private set; }
    public int PhotoCount => Volatile.Read(ref _photoCount);

    public void ShowMessage(string text) { lock (_lock) _messages.Add(text); }
    public void ShowPhoto(byte[] imageData) => Interlocked.Increment(ref _photoCount);
    public void ShowVideoCountdown(int seconds) { lock (_lock) _videoCountdowns.Add(seconds); }
    public void SetRecording(bool recording, int totalSeconds = 0)
    {
        Recording = recording;
        if (recording) RecordingTotalSeconds = totalSeconds;
    }
    public void SetStatus(string? status, BoothStatusLevel level = BoothStatusLevel.Info) => Status = status;
    public void SetConnectivity(BoothStatusLevel level) => Connectivity = level;

    public bool SawMessage(string m) { lock (_lock) return _messages.Contains(m); }
    public IReadOnlyList<string> Messages { get { lock (_lock) return _messages.ToList(); } }
    public IReadOnlyList<int> VideoCountdowns { get { lock (_lock) return _videoCountdowns.ToList(); } }
}

/// <summary>
/// Programmable GoPro double for failure-path tests. ListMedia returns a base set plus one
/// "NEW{n}.JPG" per photo trigger, so the workflow's "wait for a new file" logic is exercised.
/// </summary>
internal sealed class ScriptedGoProClient : IGoProClient
{
    private readonly List<string> _baseFiles = new() { "OLD0001.JPG", "OLD0002.MP4" };
    private int _triggerCount;
    private bool _videoMode;

    public bool ThrowOnList { get; set; }
    public bool NeverProduceNewMedia { get; set; }
    public string? LastDownloadedFile { get; private set; }
    public string? LastDownloadedDir { get; private set; }
    public int TriggerCount => Volatile.Read(ref _triggerCount);

    public Task SetSinglePhotoModeAsync(CancellationToken ct = default) { _videoMode = false; return Task.CompletedTask; }
    public Task SetVideoModeAsync(CancellationToken ct = default) { _videoMode = true; return Task.CompletedTask; }

    public Task TriggerAsync(CancellationToken ct = default)
    {
        if (!_videoMode) Interlocked.Increment(ref _triggerCount);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<GoProMedia> ListMediaAsync(CancellationToken ct = default)
    {
        if (ThrowOnList) throw new GoProUnavailableException("scripted absent");
        var files = new List<string>(_baseFiles);
        if (!NeverProduceNewMedia)
        {
            for (var i = 1; i <= TriggerCount; i++)
                files.Add($"NEW{i:D4}.JPG");
        }
        var media = new GoProMedia { Id = "scripted" };
        media.Media.Add(new GoProMediaDirectory
        {
            Directory = "100GOPRO",
            FileSystem = files.Select(n => new GoProMediaFile { FileName = n }).ToList()
        });
        return Task.FromResult(media);
    }

    public Task<byte[]> DownloadMediaAsync(string directory, string fileName, CancellationToken ct = default)
    {
        LastDownloadedDir = directory;
        LastDownloadedFile = fileName;
        return Task.FromResult(new byte[] { 1, 2, 3 });
    }

    public Task SendKeepAliveAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task<bool> IsReachableAsync(CancellationToken ct = default) => Task.FromResult(!ThrowOnList);
}

internal static class TestHarness
{
    public sealed record Rig(PhotoboothWorkflow Workflow, RecordingDisplay Display, FakeLightOutput Light, IGoProClient GoPro);

    /// <summary>Build a workflow wired to fakes with fast, test-friendly timings.</summary>
    public static Rig Build(
        IGoProClient gopro,
        Action<TimingOptions>? tuneTimings = null,
        Action<GoProOptions>? tuneGoPro = null)
    {
        var timings = new TimingOptions
        {
            PoseMs = 10,
            CountdownStepMs = 5,
            LightSettleMs = 5,
            PhotoDisplayMs = 20,
            VideoMaxSeconds = 60,
            SlideshowIntervalSeconds = 3600, // effectively off unless a test lowers it
            StatusPollSeconds = 3600,        // connectivity monitor off in tests (single probe at start only)
            WatchdogSeconds = 5
        };
        tuneTimings?.Invoke(timings);

        var gopt = new GoProOptions
        {
            Mode = "fake",
            RequestTimeoutSeconds = 1,
            CaptureDeadlineSeconds = 1,
            MaxRetries = 2,
            RetryBackoffMs = 10,
            KeepAliveIntervalSeconds = 3600
        };
        tuneGoPro?.Invoke(gopt);

        var display = new RecordingDisplay();
        var light = new FakeLightOutput(NullLogger<FakeLightOutput>.Instance);
        var wf = new PhotoboothWorkflow(
            gopro, light, display,
            Options.Create(timings), Options.Create(gopt),
            NullLogger<PhotoboothWorkflow>.Instance);
        return new Rig(wf, display, light, gopro);
    }

    public static async Task<bool> WaitForAsync(Func<bool> condition, int timeoutMs = 3000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (condition()) return true;
            await Task.Delay(15);
        }
        return condition();
    }
}
