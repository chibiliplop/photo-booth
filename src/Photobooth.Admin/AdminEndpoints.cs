using System;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Photobooth.Core.Abstractions;
using Photobooth.Core.Diagnostics;
using Photobooth.Core.Options;

namespace Photobooth.Admin;

/// <summary>
/// Mapping minimal-API de l'interface d'admin. Lecture seule dans ce plan (status + logs).
/// Les handlers résolvent leurs dépendances depuis le conteneur de l'hôte (injection minimal-API).
/// </summary>
public static class AdminEndpoints
{
    public static void MapApi(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/status", (BoothTelemetry tel, IPrinterAdapter printer, IOptions<PrinterOptions> popt) =>
        {
            var status = new AdminStatus(
                State: tel.State.ToString(),
                GoProReachable: tel.GoProReachable,
                Printer: new AdminPrinterInfo(printer.IsEnabled, popt.Value.Type),
                LastPrint: tel.LastPrint,
                Version: Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown",
                ServerTimeUtc: DateTimeOffset.UtcNow);
            return Results.Json(status);
        });

        app.MapGet("/api/logs", (InMemoryLogSink sink) => Results.Json(sink.Snapshot()));
    }
}
