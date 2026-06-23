using System;
using Photobooth.Core.Diagnostics;

namespace Photobooth.Admin;

/// <summary>Snapshot lecture seule de l'état borne, sérialisé par GET /api/status.</summary>
public sealed record AdminStatus(
    string State,
    bool? GoProReachable,
    AdminPrinterInfo Printer,
    PrintResult? LastPrint,
    string Version,
    DateTimeOffset ServerTimeUtc);

/// <summary>État imprimante exposé au dashboard.</summary>
public sealed record AdminPrinterInfo(bool Enabled, string Type);
