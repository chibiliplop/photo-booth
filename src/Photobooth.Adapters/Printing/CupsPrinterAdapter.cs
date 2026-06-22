using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Photobooth.Core.Abstractions;
using Photobooth.Core.Options;

namespace Photobooth.Adapters.Printing;

public sealed class CupsPrinterAdapter : IPrinterAdapter
{
    private readonly PrinterOptions _options;
    private readonly ILogger<CupsPrinterAdapter> _log;

    public CupsPrinterAdapter(IOptions<PrinterOptions> options, ILogger<CupsPrinterAdapter> log)
    {
        _options = options.Value;
        _log = log;
    }

    public bool IsEnabled => true;

    public async Task PrintAsync(byte[] imageData, CancellationToken ct = default)
    {
        var start = new ProcessStartInfo
        {
            FileName = _options.LpCommand,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        if (!string.IsNullOrWhiteSpace(_options.Name))
        {
            start.ArgumentList.Add("-d");
            start.ArgumentList.Add(_options.Name);
        }

        if (_options.Copies > 1)
        {
            start.ArgumentList.Add("-n");
            start.ArgumentList.Add(_options.Copies.ToString());
        }

        if (!string.IsNullOrWhiteSpace(_options.Media))
            AddOption(start, "media", _options.Media);

        foreach (var option in ParseOptions(_options.Options))
            AddOption(start, option.Key, option.Value);

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException($"Unable to start print command '{_options.LpCommand}'.");

        await process.StandardInput.BaseStream.WriteAsync(imageData, ct);
        process.StandardInput.Close();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"lp failed with exit code {process.ExitCode}: {stderr}");

        _log.LogInformation("CUPS print job submitted. {Output}", stdout.Trim());
    }

    private static void AddOption(ProcessStartInfo start, string key, string value)
    {
        start.ArgumentList.Add("-o");
        start.ArgumentList.Add($"{key}={value}");
    }

    private static IEnumerable<KeyValuePair<string, string>> ParseOptions(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            yield break;

        foreach (var part in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var equals = part.IndexOf('=');
            if (equals <= 0 || equals == part.Length - 1)
                continue;
            yield return new KeyValuePair<string, string>(part[..equals].Trim(), part[(equals + 1)..].Trim());
        }
    }
}
