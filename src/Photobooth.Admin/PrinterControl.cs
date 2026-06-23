using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Photobooth.Core.Options;

namespace Photobooth.Admin;

/// <summary>État imprimante exposé à l'onglet imprimante.</summary>
public sealed record PrinterDetail(string Raw, bool? Enabled, bool? Accepting);

/// <summary>
/// Commandes imprimante/CUPS de l'onglet imprimante (debug terrain §8). Lecture en user pi
/// (lpstat) ; cupsenable/cupsaccept/lpinfo + lecture error_log via sudo (root). Les modifs
/// runtime (enable/accept) sont temporaires sous l'overlay (réinitialisées au reboot, §14.3).
/// </summary>
public sealed class PrinterControl
{
    private readonly IProcessRunner _runner;
    private readonly PrinterOptions _opt;

    public PrinterControl(IProcessRunner runner, IOptions<PrinterOptions> options)
    {
        _runner = runner;
        _opt = options.Value;
    }

    private string Queue => _opt.Name;

    public async Task<PrinterDetail> StatusAsync(CancellationToken ct = default)
    {
        var r = await _runner.RunAsync("lpstat", new[] { "-p", Queue, "-a", Queue }, ct: ct);
        var text = r.Stdout + "\n" + r.Stderr;
        bool? enabled = text.Contains("disabled", StringComparison.OrdinalIgnoreCase) ? false
            : text.Contains("enabled", StringComparison.OrdinalIgnoreCase) ? true : null;
        bool? accepting = text.Contains("not accepting", StringComparison.OrdinalIgnoreCase) ? false
            : text.Contains("accepting", StringComparison.OrdinalIgnoreCase) ? true : null;
        return new PrinterDetail(r.Stdout, enabled, accepting);
    }

    public Task<ProcessResult> EnableAsync(CancellationToken ct = default) =>
        _runner.RunAsync("sudo", new[] { "cupsenable", Queue }, ct: ct);

    public Task<ProcessResult> AcceptAsync(CancellationToken ct = default) =>
        _runner.RunAsync("sudo", new[] { "cupsaccept", Queue }, ct: ct);

    public Task<ProcessResult> TestPrintAsync(CancellationToken ct = default) =>
        _runner.RunAsync(_opt.LpCommand, new[] { "-d", Queue },
            stdin: $"Photobooth — test d'impression\n{DateTimeOffset.UtcNow:u}\n", ct: ct);

    public Task<ProcessResult> PurgeAsync(CancellationToken ct = default) =>
        _runner.RunAsync("cancel", new[] { "-a", Queue }, ct: ct);

    public Task<ProcessResult> DetectUsbAsync(CancellationToken ct = default) =>
        _runner.RunAsync("sudo", new[] { "lpinfo", "-v" }, ct: ct);

    // `lpstat -o <queue>` (cups-client) plutôt que `lpq -P` : lpq vit dans cups-bsd, absent de
    // l'image (install --no-install-recommends de cups + cups-client). Un lpq introuvable faisait
    // lever Process.Start → 500 sur /api/printer/queue → l'onglet imprimante affichait « indisponible ».
    public Task<ProcessResult> QueueAsync(CancellationToken ct = default) =>
        _runner.RunAsync("lpstat", new[] { "-o", Queue }, ct: ct);

    public Task<ProcessResult> CupsLogAsync(CancellationToken ct = default) =>
        _runner.RunAsync("sudo", new[] { "tail", "-n", "200", "/var/log/cups/error_log" }, ct: ct);
}
