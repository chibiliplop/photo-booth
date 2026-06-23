using System;
using Photobooth.Core.Workflow;

namespace Photobooth.Core.Diagnostics;

/// <summary>
/// Snapshot thread-safe de l'état diagnostic vivant de la borne, écrit par le workflow et (à terme) lu
/// par l'hôte web d'admin. Volontairement minuscule et verrouillé : touché depuis le consommateur de
/// commandes (thread de fond) et plus tard depuis des threads de requête web.
/// </summary>
public sealed class BoothTelemetry
{
    private readonly object _lock = new();
    private PrintResult? _lastPrint;
    private BoothState _state = BoothState.Idle;
    private bool? _goProReachable;

    /// <summary>Résultat de la dernière tentative d'impression, ou null si aucune n'a eu lieu.</summary>
    public PrintResult? LastPrint
    {
        get { lock (_lock) return _lastPrint; }
    }

    /// <summary>Enregistre un échec d'impression avec sa raison réelle (auparavant avalée par le workflow).</summary>
    public void RecordPrintFailure(string reason)
    {
        lock (_lock) _lastPrint = new PrintResult(false, reason, DateTimeOffset.UtcNow);
    }

    /// <summary>Enregistre une soumission d'impression réussie.</summary>
    public void RecordPrintSuccess()
    {
        lock (_lock) _lastPrint = new PrintResult(true, null, DateTimeOffset.UtcNow);
    }

    /// <summary>État courant de la borne (écrit par le workflow à chaque transition).</summary>
    public BoothState State
    {
        get { lock (_lock) return _state; }
    }

    /// <summary>Dernière joignabilité GoPro connue, ou null si jamais sondée.</summary>
    public bool? GoProReachable
    {
        get { lock (_lock) return _goProReachable; }
    }

    /// <summary>Enregistre la transition d'état (appelé depuis le point unique SetState du workflow).</summary>
    public void RecordState(BoothState state)
    {
        lock (_lock) _state = state;
    }

    /// <summary>Enregistre le résultat de la dernière sonde de joignabilité GoPro.</summary>
    public void RecordGoProReachable(bool reachable)
    {
        lock (_lock) _goProReachable = reachable;
    }
}

/// <summary>Résultat immuable d'une tentative d'impression.</summary>
public sealed record PrintResult(bool Succeeded, string? Reason, DateTimeOffset At);
