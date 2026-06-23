using System;
using System.Collections.Generic;
using System.Linq;
using Serilog.Core;
using Serilog.Events;

namespace Photobooth.Admin;

/// <summary>
/// Sink Serilog qui conserve les <see cref="Capacity"/> dernières lignes de log dans un ring buffer en
/// RAM, pour que l'hôte web d'admin serve les logs récents sans dépendre de journald (volatil sous
/// l'overlay read-only de toute façon). Thread-safe : Serilog émet depuis des threads arbitraires.
/// </summary>
public sealed class InMemoryLogSink : ILogEventSink
{
    public const int Capacity = 500;

    private readonly object _lock = new();
    private readonly LinkedList<LogLine> _lines = new();

    /// <summary>Levé après l'ajout d'une ligne au buffer (alimente le flux SSE). Thread-arbitraire.</summary>
    public event System.Action<LogLine>? Emitted;

    public void Emit(LogEvent logEvent)
    {
        var line = new LogLine(
            logEvent.Timestamp,
            logEvent.Level.ToString(),
            logEvent.RenderMessage(),
            logEvent.Exception?.ToString());

        lock (_lock)
        {
            _lines.AddLast(line);
            if (_lines.Count > Capacity)
                _lines.RemoveFirst();
        }
        Emitted?.Invoke(line);
    }

    /// <summary>Copie des lignes en tampon, de la plus ancienne à la plus récente.</summary>
    public IReadOnlyList<LogLine> Snapshot()
    {
        lock (_lock) return _lines.ToList();
    }
}

/// <summary>Une entrée de log en tampon.</summary>
public sealed record LogLine(
    DateTimeOffset Timestamp,
    string Level,
    string Message,
    string? Exception);
