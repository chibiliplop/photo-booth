using System;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
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
    private const string CookieName = "padmin";

    /// <summary>
    /// Installe la garde PIN. Si opt.Pin est vide : no-op (aucune auth). Sinon : un middleware
    /// exige le cookie d'accès (== authToken) sur toutes les routes hors /login, et /login vérifie le PIN.
    /// authToken est un jeton aléatoire opaque généré par l'hôte (le PIN n'est jamais stocké côté client).
    /// </summary>
    public static void UseAuth(WebApplication app, AdminOptions opt, string authToken)
    {
        if (string.IsNullOrEmpty(opt.Pin))
            return;

        app.Use(async (ctx, next) =>
        {
            var path = ctx.Request.Path;
            if (path.StartsWithSegments("/login"))
            {
                await next();
                return;
            }
            if (ctx.Request.Cookies.TryGetValue(CookieName, out var c) && FixedTimeEquals(c, authToken))
            {
                await next();
                return;
            }
            if (path.StartsWithSegments("/api"))
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            else
                ctx.Response.Redirect("/login");
        });

        app.MapGet("/login", () => Results.Content(LoginHtml(error: false), "text/html"));

        app.MapPost("/login", async (HttpContext ctx) =>
        {
            var form = await ctx.Request.ReadFormAsync();
            var pin = form["pin"].ToString();
            if (FixedTimeEquals(pin, opt.Pin))
            {
                ctx.Response.Cookies.Append(CookieName, authToken, new CookieOptions { HttpOnly = true });
                return Results.Redirect("/");
            }
            return Results.Content(LoginHtml(error: true), "text/html");
        });
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        var ba = Encoding.UTF8.GetBytes(a);
        var bb = Encoding.UTF8.GetBytes(b);
        if (ba.Length != bb.Length) return false;
        return CryptographicOperations.FixedTimeEquals(ba, bb);
    }

    private static string LoginHtml(bool error) =>
        "<!doctype html><meta name=viewport content='width=device-width,initial-scale=1'>" +
        "<title>Admin — connexion</title>" +
        "<body style='font-family:sans-serif;max-width:20rem;margin:3rem auto'>" +
        "<h1>Borne photo — admin</h1>" +
        (error ? "<p style='color:#c00'>PIN incorrect.</p>" : "") +
        "<form method=post action=/login>" +
        "<input name=pin type=password placeholder=PIN autofocus " +
        "style='font-size:1.2rem;padding:.5rem;width:100%'>" +
        "<button style='font-size:1.2rem;padding:.5rem;margin-top:.5rem;width:100%'>Entrer</button>" +
        "</form></body>";

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
