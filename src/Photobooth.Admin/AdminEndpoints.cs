using System;
using System.IO;
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
                ctx.Response.Cookies.Append(CookieName, authToken,
                    new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict });
                return Results.Redirect("/");
            }
            return Results.Content(LoginHtml(error: true), "text/html");
        });
    }

    private const string CsrfHeader = "X-Admin-CSRF";

    /// <summary>
    /// Garde CSRF : toute requête mutante (POST/PUT/DELETE/PATCH) doit porter l'en-tête
    /// <c>X-Admin-CSRF</c> égal au jeton de session. /login en est exempté (formulaire d'entrée).
    /// Le jeton est lisible via GET /api/csrf (derrière l'auth) ; une page tierce ne peut ni le lire
    /// (même origine) ni poser un en-tête custom sans CORS, ce qui bloque le CSRF.
    /// </summary>
    public static void UseCsrf(WebApplication app, string csrfToken)
    {
        app.Use(async (ctx, next) =>
        {
            var m = ctx.Request.Method;
            var mutating = HttpMethods.IsPost(m) || HttpMethods.IsPut(m)
                        || HttpMethods.IsDelete(m) || HttpMethods.IsPatch(m);
            if (mutating && !ctx.Request.Path.StartsWithSegments("/login"))
            {
                var header = ctx.Request.Headers[CsrfHeader].ToString();
                if (!FixedTimeEquals(header, csrfToken))
                {
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }
            }
            await next();
        });
    }

    /// <summary>Expose le jeton CSRF à la page (derrière l'auth si un PIN est défini).</summary>
    public static void MapCsrf(IEndpointRouteBuilder app, string csrfToken)
    {
        app.MapGet("/api/csrf", () => Results.Json(new { token = csrfToken }));
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

    private static readonly string PageHtml = LoadPage();

    private static string LoadPage()
    {
        var asm = typeof(AdminEndpoints).Assembly;
        using var s = asm.GetManifestResourceStream("Photobooth.Admin.admin.html")
                      ?? throw new InvalidOperationException("Ressource admin.html introuvable.");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    /// <summary>Mappe GET / (page HTML embarquée). Soumis au middleware d'auth s'il est installé.</summary>
    public static void MapPage(IEndpointRouteBuilder app)
    {
        app.MapGet("/", () => Results.Content(PageHtml, "text/html"));
    }

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

    /// <summary>Onglet actions (§7.5). Mutations soumises au middleware CSRF.</summary>
    public static void MapActions(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/actions/restart", async (PrivilegedActions pa) => Results.Json(await pa.RestartAppAsync()));
        app.MapPost("/api/actions/reboot", async (PrivilegedActions pa) => Results.Json(await pa.RebootAsync()));
        app.MapPost("/api/actions/recover-gopro", (Photobooth.Core.Workflow.IBoothCommandSink sink) =>
        {
            sink.Submit(new Photobooth.Core.Workflow.BoothCommand.Recovered());
            return Results.Json(new { ok = true });
        });
    }

    /// <summary>Onglet imprimante (§8) : état + actions CUPS. Actions soumises au middleware CSRF.</summary>
    public static void MapPrinter(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/printer", async (PrinterControl pc) => Results.Json(await pc.StatusAsync()));
        app.MapGet("/api/printer/usb", async (PrinterControl pc) => Results.Json(await pc.DetectUsbAsync()));
        app.MapGet("/api/printer/queue", async (PrinterControl pc) => Results.Json(await pc.QueueAsync()));
        app.MapGet("/api/printer/cups-log", async (PrinterControl pc) => Results.Json(await pc.CupsLogAsync()));

        app.MapPost("/api/printer/enable", async (PrinterControl pc) => Results.Json(await pc.EnableAsync()));
        app.MapPost("/api/printer/accept", async (PrinterControl pc) => Results.Json(await pc.AcceptAsync()));
        app.MapPost("/api/printer/test", async (PrinterControl pc) => Results.Json(await pc.TestPrintAsync()));
        app.MapPost("/api/printer/purge", async (PrinterControl pc) => Results.Json(await pc.PurgeAsync()));
    }
}
