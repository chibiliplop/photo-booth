using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Photobooth.Core.Options;

namespace Photobooth.Admin;

/// <summary>Chemin du photobooth.json éditable (FAT32 sur la borne, ./config en dev).</summary>
public sealed record AdminConfigTarget(string Path);

/// <summary>
/// Lecture / validation / écriture du photobooth.json opérateur. La validation réutilise les
/// Validate() existants des classes Options (zéro duplication, §6). L'écriture est atomique
/// (temp + rename, résiste à une coupure secteur sur FAT32, §14.1) ; si l'app (pi) n'a pas le droit
/// d'écrire (FAT32 root sur la borne), repli sur un helper root via sudo.
/// </summary>
public sealed class ConfigStore
{
    private const string Helper = "/usr/local/sbin/photobooth-write-config.sh";

    private readonly AdminConfigTarget _target;
    private readonly IProcessRunner _runner;
    private readonly ILogger<ConfigStore> _log;

    public ConfigStore(AdminConfigTarget target, IProcessRunner runner, ILogger<ConfigStore> log)
    {
        _target = target;
        _runner = runner;
        _log = log;
    }

    public async Task<string> ReadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_target.Path))
            return "{}";
        return await File.ReadAllTextAsync(_target.Path, ct);
    }

    public string? Validate(string json)
    {
        IConfiguration cfg;
        try
        {
            cfg = new ConfigurationBuilder()
                .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json)))
                .Build();
        }
        catch (Exception ex)
        {
            return $"JSON invalide : {ex.Message}";
        }

        return cfg.GetSection(GoProOptions.Section).Get<GoProOptions>()?.Validate()
            ?? cfg.GetSection(HardwareOptions.Section).Get<HardwareOptions>()?.Validate()
            ?? cfg.GetSection(TimingOptions.Section).Get<TimingOptions>()?.Validate()
            ?? cfg.GetSection(ThemeOptions.Section).Get<ThemeOptions>()?.Validate()
            ?? cfg.GetSection(PrinterOptions.Section).Get<PrinterOptions>()?.Validate()
            ?? cfg.GetSection(AdminOptions.Section).Get<AdminOptions>()?.Validate();
    }

    public async Task WriteAsync(string json, CancellationToken ct = default)
    {
        var dir = Path.GetDirectoryName(_target.Path)!;
        try
        {
            Directory.CreateDirectory(dir);
            var tmp = Path.Combine(dir, "." + Path.GetFileName(_target.Path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            await File.WriteAllTextAsync(tmp, json, ct);
            File.Move(tmp, _target.Path, overwrite: true);
        }
        catch (UnauthorizedAccessException)
        {
            _log.LogInformation("Écriture config directe refusée ; repli sur le helper root via sudo.");
            var r = await _runner.RunAsync("sudo", new[] { Helper }, stdin: json, ct: ct);
            if (r.ExitCode != 0)
                throw new IOException($"Helper d'écriture config échoué (exit {r.ExitCode}) : {r.Stderr}");
        }
    }
}
