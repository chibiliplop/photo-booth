using System;

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
}

/// <summary>Résultat immuable d'une tentative d'impression.</summary>
public sealed record PrintResult(bool Succeeded, string? Reason, DateTimeOffset At);
