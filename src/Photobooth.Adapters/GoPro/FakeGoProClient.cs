using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Photobooth.Core.Abstractions;
using Photobooth.Core.GoPro;

namespace Photobooth.Adapters.GoPro;

/// <summary>
/// No-network GoPro stand-in for development without a camera (and as an operational fallback when
/// Gopro.Mode = fake). Maintains a growing media list so the workflow's "wait for a NEW photo" logic
/// is exercised: TriggerAsync appends a fresh JPG, which is exactly what ListMediaAsync then surfaces.
/// </summary>
public sealed class FakeGoProClient : IGoProClient
{
    // 1x1 transparent PNG, used only if no sample images were supplied.
    private static readonly byte[] PlaceholderImage = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    private const string DirectoryName = "100GOPRO";

    private readonly object _lock = new();
    private readonly List<string> _files = new() { "GOPR0001.JPG", "GOPR0002.JPG", "GOPR0003.JPG" };
    private readonly byte[][] _samples;
    private readonly ILogger<FakeGoProClient> _log;
    private int _counter = 3;
    private bool _videoMode;
    private int _sampleCursor;

    public FakeGoProClient(IReadOnlyList<byte[]> sampleImages, ILogger<FakeGoProClient> log)
    {
        _log = log;
        _samples = sampleImages is { Count: > 0 }
            ? sampleImages.ToArray()
            : new[] { PlaceholderImage };
    }

    public Task SetSinglePhotoModeAsync(CancellationToken ct = default)
    {
        _videoMode = false;
        return Task.CompletedTask;
    }

    public Task SetVideoModeAsync(CancellationToken ct = default)
    {
        _videoMode = true;
        return Task.CompletedTask;
    }

    public Task TriggerAsync(CancellationToken ct = default)
    {
        if (!_videoMode)
        {
            lock (_lock)
            {
                _counter++;
                _files.Add($"GOPR{_counter:D4}.JPG");
            }
            _log.LogInformation("Fake GoPro captured a photo (#{Count}).", _counter);
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<GoProMedia> ListMediaAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            var media = new GoProMedia { Id = "fake-gopro" };
            media.Media.Add(new GoProMediaDirectory
            {
                Directory = DirectoryName,
                FileSystem = _files
                    .Select(n => new GoProMediaFile { FileName = n, Mode = "photo", Ls = "0", Size = "0" })
                    .ToList()
            });
            return Task.FromResult(media);
        }
    }

    public Task<byte[]> DownloadMediaAsync(string directory, string fileName, CancellationToken ct = default)
    {
        var idx = Interlocked.Increment(ref _sampleCursor) - 1;
        return Task.FromResult(_samples[idx % _samples.Length]);
    }

    public Task SendKeepAliveAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task<bool> IsReachableAsync(CancellationToken ct = default) => Task.FromResult(true);
}
