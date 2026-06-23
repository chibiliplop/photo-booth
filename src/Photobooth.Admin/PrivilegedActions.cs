using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Photobooth.Admin;

/// <summary>
/// Actions système privilégiées via sudo (NOPASSWD: ALL, dérogation D9). Chaque action est
/// audit-loggée avant exécution. Restart/reboot tuent le process ; l'appel peut ne pas retourner
/// sur la borne réelle (réponse HTTP non flushée) — l'UI gère la perte de connexion.
/// </summary>
public sealed class PrivilegedActions
{
    private readonly IProcessRunner _runner;
    private readonly ILogger<PrivilegedActions> _log;

    public PrivilegedActions(IProcessRunner runner, ILogger<PrivilegedActions> log)
    {
        _runner = runner;
        _log = log;
    }

    public Task<ProcessResult> RestartAppAsync(CancellationToken ct = default)
    {
        _log.LogInformation("Action privilégiée : restart du service photobooth.");
        return _runner.RunAsync("sudo", new[] { "systemctl", "restart", "photobooth" }, ct: ct);
    }

    public Task<ProcessResult> RebootAsync(CancellationToken ct = default)
    {
        _log.LogWarning("Action privilégiée : reboot de la borne.");
        return _runner.RunAsync("sudo", new[] { "systemctl", "reboot" }, ct: ct);
    }
}
