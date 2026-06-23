using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Photobooth.Admin;

/// <summary>Corps JSON du POST /api/console.</summary>
public sealed record ConsoleRequest(string Command);

/// <summary>
/// Exécute une commande arbitraire one-shot en user pi (sudo NOPASSWD disponible, dérogation D8).
/// Chaque commande est audit-loggée avant exécution. Timeout 30 s + kill via IProcessRunner.
/// </summary>
public sealed class ConsoleService
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly IProcessRunner _runner;
    private readonly ILogger<ConsoleService> _log;

    public ConsoleService(IProcessRunner runner, ILogger<ConsoleService> log)
    {
        _runner = runner;
        _log = log;
    }

    public Task<ProcessResult> RunAsync(string command, CancellationToken ct = default)
    {
        _log.LogInformation("Console admin — exécution : {Command}", command);
        return _runner.RunShellAsync(command, timeout: Timeout, ct: ct);
    }
}
