using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Photobooth.Admin;

/// <summary>Résultat immuable d'un process externe.</summary>
public sealed record ProcessResult(int ExitCode, string Stdout, string Stderr, bool TimedOut);

/// <summary>
/// Exécute des process externes pour l'admin : argv list (jamais d'interpolation shell, sauf
/// <see cref="RunShellAsync"/> qui est réservé à la console arbitraire et l'assume), entrée stdin
/// optionnelle, timeout avec kill de l'arbre de process. Calqué sur le Process.Start sûr de
/// CupsPrinterAdapter (UseShellExecute=false, ArgumentList).
/// </summary>
public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments,
        string? stdin = null, TimeSpan? timeout = null, CancellationToken ct = default);

    Task<ProcessResult> RunShellAsync(string command,
        string? stdin = null, TimeSpan? timeout = null, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class ProcessRunner : IProcessRunner
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    public Task<ProcessResult> RunShellAsync(string command,
        string? stdin = null, TimeSpan? timeout = null, CancellationToken ct = default)
        => RunAsync("/bin/bash", new[] { "-lc", command }, stdin, timeout, ct);

    public async Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments,
        string? stdin = null, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in arguments)
            psi.ArgumentList.Add(a);

        using var process = new Process { StartInfo = psi };
        process.Start();

        if (stdin is not null)
            await process.StandardInput.WriteAsync(stdin);
        process.StandardInput.Close();

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout ?? DefaultTimeout);

        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            timedOut = !ct.IsCancellationRequested; // distingue timeout d'une annulation externe
            try { process.Kill(entireProcessTree: true); } catch { /* déjà mort */ }
        }

        var stdout = await SafeRead(stdoutTask);
        var stderr = await SafeRead(stderrTask);
        var exit = timedOut ? -1 : SafeExitCode(process);
        return new ProcessResult(exit, stdout, stderr, timedOut);
    }

    private static async Task<string> SafeRead(Task<string> t)
    {
        try { return await t; } catch { return string.Empty; }
    }

    private static int SafeExitCode(Process p)
    {
        try { return p.ExitCode; } catch { return -1; }
    }
}
