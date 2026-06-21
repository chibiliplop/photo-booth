using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Photobooth.Core.Abstractions;
using Photobooth.Core.GoPro;
using Photobooth.Core.Options;
using Photobooth.Core.Resilience;

namespace Photobooth.Adapters.GoPro;

/// <summary>
/// Talks to a real GoPro (or the local Python simulator) over HTTP + UDP. Differences from the old
/// UWP code, all stability-driven:
///  - ONE shared <see cref="HttpClient"/> and ONE reused <see cref="UdpClient"/> (no per-call allocation);
///  - bounded retry with a per-attempt timeout + overall deadline + cancellation (no infinite do/while);
///  - returns <c>byte[]</c> (no BitmapImage / IRandomAccessStream);
///  - on exhaustion throws <see cref="GoProUnavailableException"/> instead of returning null.
/// </summary>
public sealed class HttpGoProClient : IGoProClient, IDisposable
{
    private sealed class RetryableException : Exception { }

    private static readonly byte[] KeepAlivePayload = Encoding.UTF8.GetBytes("_GPHD_:0:0:2:0.000000\n");
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly UdpClient _udp;
    private readonly GoProOptions _opt;
    private readonly ILogger<HttpGoProClient> _log;

    public HttpGoProClient(IOptions<GoProOptions> opt, ILogger<HttpGoProClient> log)
    {
        _opt = opt.Value;
        _log = log;
        // We control timeouts per-attempt via linked CTS, so disable HttpClient's own timeout.
        _http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        _udp = new UdpClient(AddressFamily.InterNetwork);
    }

    public Task SetSinglePhotoModeAsync(CancellationToken ct = default) =>
        SendCommandAsync($"{_opt.ControlBaseUrl}/gp/gpControl/command/sub_mode?mode=1&sub_mode=0", ct);

    public Task SetVideoModeAsync(CancellationToken ct = default) =>
        SendCommandAsync($"{_opt.ControlBaseUrl}/gp/gpControl/command/sub_mode?mode=0&sub_mode=0", ct);

    public Task TriggerAsync(CancellationToken ct = default) =>
        SendCommandAsync($"{_opt.ControlBaseUrl}/gp/gpControl/command/shutter?p=1", ct);

    public Task StopAsync(CancellationToken ct = default) =>
        SendCommandAsync($"{_opt.ControlBaseUrl}/gp/gpControl/command/shutter?p=0", ct);

    public Task<GoProMedia> ListMediaAsync(CancellationToken ct = default) =>
        ExecuteWithRetryAsync(async attemptCt =>
        {
            using var resp = await _http.GetAsync($"{_opt.MediaBaseUrl}/gp/gpMediaList", attemptCt);
            if (resp.StatusCode == HttpStatusCode.ServiceUnavailable) throw new RetryableException();
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync(attemptCt);
            return JsonSerializer.Deserialize<GoProMedia>(json, JsonOpts) ?? new GoProMedia();
        }, ct);

    public Task<byte[]> DownloadMediaAsync(string directory, string fileName, CancellationToken ct = default) =>
        ExecuteWithRetryAsync(async attemptCt =>
        {
            var url = $"{_opt.MediaBaseUrl}/videos/DCIM/{directory}/{fileName}";
            using var resp = await _http.GetAsync(url, attemptCt);
            if (resp.StatusCode == HttpStatusCode.ServiceUnavailable) throw new RetryableException();
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadAsByteArrayAsync(attemptCt);
        }, ct);

    public async Task SendKeepAliveAsync(CancellationToken ct = default)
    {
        try
        {
            await _udp.SendAsync(KeepAlivePayload, KeepAlivePayload.Length, _opt.KeepAliveHost, _opt.KeepAlivePort);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Keepalive UDP send failed.");
        }
    }

    public async Task<bool> IsReachableAsync(CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(_opt.RequestTimeoutSeconds));
            using var resp = await _http.GetAsync($"{_opt.MediaBaseUrl}/gp/gpMediaList", cts.Token);
            return resp.IsSuccessStatusCode || resp.StatusCode == HttpStatusCode.ServiceUnavailable;
        }
        catch
        {
            return false;
        }
    }

    private Task SendCommandAsync(string url, CancellationToken ct) =>
        ExecuteWithRetryAsync(async attemptCt =>
        {
            using var resp = await _http.GetAsync(url, attemptCt);
            if (resp.StatusCode == HttpStatusCode.ServiceUnavailable) throw new RetryableException();
            resp.EnsureSuccessStatusCode();
            return true;
        }, ct);

    private async Task<T> ExecuteWithRetryAsync<T>(Func<CancellationToken, Task<T>> attempt, CancellationToken overallCt)
    {
        Exception? last = null;
        for (var i = 0; i < _opt.MaxRetries; i++)
        {
            overallCt.ThrowIfCancellationRequested();
            using (var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(overallCt))
            {
                attemptCts.CancelAfter(TimeSpan.FromSeconds(_opt.RequestTimeoutSeconds));
                try
                {
                    return await attempt(attemptCts.Token);
                }
                catch (RetryableException)
                {
                    last = new GoProUnavailableException("GoPro returned 503 (busy).");
                }
                catch (OperationCanceledException) when (overallCt.IsCancellationRequested)
                {
                    throw; // overall deadline / shutdown -> bubble out
                }
                catch (OperationCanceledException)
                {
                    last = new GoProUnavailableException("GoPro request timed out.");
                }
                catch (HttpRequestException ex)
                {
                    last = new GoProUnavailableException("GoPro not reachable.", ex);
                }
            }

            try { await Task.Delay(TimeSpan.FromMilliseconds(_opt.RetryBackoffMs), overallCt); }
            catch (OperationCanceledException) when (overallCt.IsCancellationRequested) { throw; }
        }
        throw last ?? new GoProUnavailableException("GoPro unavailable.");
    }

    public void Dispose()
    {
        _http.Dispose();
        _udp.Dispose();
    }
}
