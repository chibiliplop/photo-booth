# Admin/Debug — Phase 1, Plan 3/3 : Read-write (actions, config, console root) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Compléter la Phase 1 de l'admin web embarquée en ajoutant les fonctions **write** laissées hors périmètre par le Plan 2/3 — actions imprimante, logs CUPS, édition de config, actions privilégiées, console root, flux de logs live (SSE) — avec le durcissement de sécurité qu'impose le passage en read-write.

**Architecture:** On garde l'hôte Kestrel embarqué `AdminWebHost` du Plan 2/3 et on l'étend. Toutes les commandes système passent par un unique `IProcessRunner` (argv list, pas de shell sauf console, timeout+kill) — calqué sur le `Process.Start` déjà sûr de `CupsPrinterAdapter`. Les opérations root passent par `sudo` (NOPASSWD: ALL, voir dérogation §3/D9 du design). Le PIN devient l'unique frontière réseau↔root : on ajoute donc échappement de sortie (`textContent`), CSRF sur les mutations, cookie `SameSite=Strict`, audit-log de chaque action/commande, et un warning bruyant si `Enabled && Pin==""`. La validation de config réutilise les `Validate()` existants des classes Options (zéro duplication). Le fichier sudoers + un helper d'écriture config atomique + leur câblage dans l'image (`image-builder/scripts/00-photobooth.sh`) sont **remontés de la Phase 2 vers ce plan** (décision actée le 2026-06-23).

**Tech Stack:** .NET 8, ASP.NET Core minimal API (Kestrel, `WebApplication`), `System.Diagnostics.Process`, `System.Threading.Channels` (SSE), Serilog 4.2.0, xUnit 2.9.2 + `Microsoft.AspNetCore.TestHost`, Microsoft.Extensions.Configuration(.Json/.Binder)/Options 8.x (fournis par `Microsoft.AspNetCore.App`).

## Global Constraints

- **TargetFramework** : `net8.0` (hérité de `src/Directory.Build.props` — ne jamais le redéclarer).
- **Publication cible** : `--self-contained` linux-arm64. ASP.NET est embarqué via `<FrameworkReference Include="Microsoft.AspNetCore.App" />` (déjà présent sur `Photobooth.Admin` et `Photobooth.Tests`).
- **Solution** : `./Photobooth.sln` — **aucun nouveau projet** (tout va dans des projets existants).
- **Aucune régression** : `dotnet test` (suite existante **52 tests**) reste vert après chaque tâche. `dotnet build Photobooth.sln` : **0 warning, 0 erreur**.
- **Dégradé jamais fatal** : tout échec de l'hôte admin / d'une action / de la console / d'une écriture config est capturé et remonté à l'UI ; la borne ne tombe jamais à cause du mode debug.
- **Sérialisation** : `Results.Json(...)` utilise les défauts web (**camelCase**) — les tests et le JS de la page assertent en camelCase (`exitCode`, `timedOut`, `goProReachable`, …).
- **Modèle de menace read-write (dérogation 2026-06-23 — voir design §3/§10, D8/D9)** — le PIN (+ clé WiFi GoPro) est l'**unique frontière réseau↔root**. Le plan applique ces contrôles compensatoires **non négociables** :
  - **Échappement de sortie** : `admin.html` rend les logs/sorties via `textContent` (jamais `innerHTML` de contenu dynamique). Fin du compromis XSS read-only du 2/3.
  - **CSRF** : tout endpoint mutant (POST/PUT/DELETE) exige l'en-tête `X-Admin-CSRF` égal au jeton de session (distinct du cookie `HttpOnly`) ; sinon **403**.
  - **Cookie** : le cookie de login est `HttpOnly` **et** `SameSite=Strict`.
  - **Warning no-PIN** : si `Enabled && Pin==""`, l'hôte logge un `LogWarning` bruyant au démarrage **mais expose tout** (risque accepté).
  - **Moindre exécution** : commandes non-root en user `pi` via `IProcessRunner` (argv list, jamais d'interpolation shell sauf la console qui l'assume) ; `sudo` seulement où root est requis.
  - **Audit** : chaque action privilégiée et chaque commande console est loggée en `Information` (donc visible dans `InMemoryLogSink`/journald) **avant** exécution.
- **Privilèges (dérogation D9)** : `/etc/sudoers.d/photobooth` accorde `pi ALL=(ALL) NOPASSWD: ALL`. La liste blanche est **abandonnée**. Le helper d'écriture config atomique reste utilisé (sécurité coupure secteur sur FAT32, §14.1), pas pour le confinement.
- **Style** : `sealed` par défaut ; conventions du repo (commentaires FR/EN tolérés) ; Conventional Commits.
- **Commits** : terminer chaque message par `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`.
- **Hors périmètre (reste Phase 2)** : AP/hostapd/dnsmasq, avahi/mDNS, overlay boot + dismiss, `Exposure=ap`/`both`, persistance des logs sur FAT32 (`Admin.PersistLogsToFat`).

---

## File Structure

**Créés — `Photobooth.Admin` :**
- `src/Photobooth.Admin/IProcessRunner.cs` — `IProcessRunner` + `ProcessResult` record + `ProcessRunner` impl (exec sûr, timeout+kill).
- `src/Photobooth.Admin/PrinterControl.cs` — commandes imprimante/CUPS (lpstat, cupsenable/accept, test print, lpinfo, lpq, cancel, tail error_log) + `PrinterDetail` record.
- `src/Photobooth.Admin/PrivilegedActions.cs` — restart app / reboot via `sudo`.
- `src/Photobooth.Admin/ConfigStore.cs` — lecture/validation/écriture atomique de `photobooth.json` + `AdminConfigTarget` record.
- `src/Photobooth.Admin/ConsoleService.cs` — exécution d'une commande arbitraire (shell, audit-loggée).

**Créés — `Photobooth.Core` :**
- `src/Photobooth.Core/Workflow/IBoothCommandSink.cs` — interface `Submit(BoothCommand)` (implémentée par le workflow).

**Modifiés — `Photobooth.Admin` :**
- `src/Photobooth.Admin/AdminEndpoints.cs` — CSRF + `GET /api/csrf` + cookie `SameSite=Strict` ; nouvelles méthodes `MapPrinter`/`MapActions`/`MapConfig`/`MapConsole`/`MapLogStream`.
- `src/Photobooth.Admin/AdminWebHost.cs` — ctor basé sur `IServiceProvider` (forwarding) ; CSRF/auth ; warning no-PIN ; mapping des endpoints write.
- `src/Photobooth.Admin/InMemoryLogSink.cs` — `event Action<LogLine>? Emitted` levé à chaque `Emit`.
- `src/Photobooth.Admin/admin.html` — réécriture : onglets, `textContent`, panneaux write, helper fetch CSRF, logs en SSE (repli polling).

**Modifiés — `Photobooth.App` :**
- `src/Photobooth.App/Composition/ServiceConfiguration.cs` — DI des nouveaux services + `IBoothCommandSink` + `AdminConfigTarget`.
- `src/Photobooth.Core/Workflow/PhotoboothWorkflow.cs` — la classe implémente `IBoothCommandSink` (déclaration seule ; `Submit` existe déjà).

**Créés/Modifiés — déploiement :**
- `deploy/sudoers.d/photobooth` — **créé** : `pi ALL=(ALL) NOPASSWD: ALL`.
- `deploy/photobooth-write-config.sh` — **créé** : helper root d'écriture atomique de `photobooth.json` (stdin → temp + rename).
- `image-builder/scripts/00-photobooth.sh` — **modifié** : install du sudoers (0440, `visudo -c`) + du helper (0755).

**Tests créés :** `IProcessRunnerTests.cs`, `FakeProcessRunner.cs` (helper), `PrinterControlTests.cs`, `PrivilegedActionsTests.cs`, `ConfigStoreTests.cs`, `ConsoleServiceTests.cs`, `AdminWriteEndpointsTests.cs`, `AdminCsrfTests.cs`, `LogStreamTests.cs`.
**Tests modifiés :** `AdminWebHostTests.cs` (nouveau ctor), `AdminEndpointsTests.cs` (assertion `SameSite`).

---

## Task 1: IProcessRunner (exécution de process sûre, timeout + kill)

**Files:**
- Create: `src/Photobooth.Admin/IProcessRunner.cs`
- Create: `src/Photobooth.Tests/IProcessRunnerTests.cs`

**Interfaces:**
- Consumes: rien.
- Produces:
  - `Photobooth.Admin.ProcessResult(int ExitCode, string Stdout, string Stderr, bool TimedOut)` (record).
  - `Photobooth.Admin.IProcessRunner` :
    - `Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string? stdin = null, TimeSpan? timeout = null, CancellationToken ct = default)`
    - `Task<ProcessResult> RunShellAsync(string command, string? stdin = null, TimeSpan? timeout = null, CancellationToken ct = default)`
  - `Photobooth.Admin.ProcessRunner : IProcessRunner` (impl).

- [ ] **Step 1 : Écrire les tests qui échouent**

Créer `src/Photobooth.Tests/IProcessRunnerTests.cs` (chemins absolus `/bin/...` pour être indépendant du PATH ; valables sur macOS et Linux CI) :

```csharp
using System;
using System.Threading.Tasks;
using Photobooth.Admin;
using Xunit;

namespace Photobooth.Tests;

public sealed class IProcessRunnerTests
{
    private static readonly IProcessRunner Runner = new ProcessRunner();

    [Fact]
    public async Task Captures_stdout_and_zero_exit()
    {
        var r = await Runner.RunAsync("/bin/echo", new[] { "hello" });
        Assert.Equal(0, r.ExitCode);
        Assert.False(r.TimedOut);
        Assert.Contains("hello", r.Stdout);
    }

    [Fact]
    public async Task Propagates_nonzero_exit_code()
    {
        var r = await Runner.RunAsync("/bin/sh", new[] { "-c", "exit 3" });
        Assert.Equal(3, r.ExitCode);
        Assert.False(r.TimedOut);
    }

    [Fact]
    public async Task Writes_stdin_to_the_process()
    {
        var r = await Runner.RunAsync("/bin/cat", Array.Empty<string>(), stdin: "abc");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("abc", r.Stdout);
    }

    [Fact]
    public async Task Kills_on_timeout_and_flags_it()
    {
        var r = await Runner.RunAsync("/bin/sleep", new[] { "5" }, timeout: TimeSpan.FromMilliseconds(200));
        Assert.True(r.TimedOut);
    }

    [Fact]
    public async Task RunShell_executes_a_command_line()
    {
        var r = await Runner.RunShellAsync("echo from-shell");
        Assert.Equal(0, r.ExitCode);
        Assert.Contains("from-shell", r.Stdout);
    }
}
```

- [ ] **Step 2 : Lancer les tests pour vérifier qu'ils échouent**

Run: `dotnet test --filter "FullyQualifiedName~IProcessRunnerTests"`
Expected: ÉCHEC de compilation — `IProcessRunner`/`ProcessRunner`/`ProcessResult` introuvables.

- [ ] **Step 3 : Écrire l'implémentation**

Créer `src/Photobooth.Admin/IProcessRunner.cs` :

```csharp
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
```

- [ ] **Step 4 : Lancer les tests pour vérifier qu'ils passent**

Run: `dotnet test --filter "FullyQualifiedName~IProcessRunnerTests"`
Expected: PASS (5 tests).

- [ ] **Step 5 : Commit**

```bash
git add src/Photobooth.Admin/IProcessRunner.cs src/Photobooth.Tests/IProcessRunnerTests.cs
git commit -m "$(printf 'feat(admin): IProcessRunner (exec sur, timeout + kill) pour les actions admin\n\nCo-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>')"
```

---

## Task 2: Durcissement sécurité + refactor de l'hôte (CSRF, SameSite, warning no-PIN, ctor IServiceProvider)

**Files:**
- Modify: `src/Photobooth.Admin/AdminEndpoints.cs` (cookie `SameSite=Strict` ; `UseCsrf` + `GET /api/csrf`)
- Modify: `src/Photobooth.Admin/AdminWebHost.cs` (ctor `IServiceProvider`, forwarding, CSRF, warning no-PIN)
- Modify: `src/Photobooth.Tests/AdminWebHostTests.cs` (nouveau ctor)
- Modify: `src/Photobooth.Tests/AdminEndpointsTests.cs` (assertion `SameSite`)
- Test: `src/Photobooth.Tests/AdminCsrfTests.cs` (créé)

**Interfaces:**
- Consumes: `AdminOptions`, `BoothTelemetry`, `InMemoryLogSink`, `IPrinterAdapter`, `IOptions<PrinterOptions>` (Plan 2/3).
- Produces:
  - `AdminEndpoints.UseCsrf(WebApplication app, string csrfToken)` — middleware : rejette (403) toute requête mutante (POST/PUT/DELETE/PATCH) sans en-tête `X-Admin-CSRF` == `csrfToken` ; exempte `/login`.
  - `AdminEndpoints.MapCsrf(IEndpointRouteBuilder app, string csrfToken)` — mappe `GET /api/csrf` → `{ "token": "<csrfToken>" }`.
  - `AdminWebHost(IServiceProvider services, IOptions<AdminOptions> options, ILogger<AdminWebHost> log)` — **nouveau ctor** (remplace les 6 paramètres explicites).

- [ ] **Step 1 : Écrire le test CSRF qui échoue**

Créer `src/Photobooth.Tests/AdminCsrfTests.cs` :

```csharp
using System.Net;
using System.Net.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Photobooth.Admin;
using Xunit;

namespace Photobooth.Tests;

public sealed class AdminCsrfTests
{
    private static WebApplication BuildApp(string csrf)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        var app = builder.Build();
        AdminEndpoints.UseCsrf(app, csrf);
        AdminEndpoints.MapCsrf(app, csrf);
        app.MapPost("/api/echo", () => Results.Json(new { ok = true }));
        return app;
    }

    [Fact]
    public async Task Mutating_request_without_csrf_header_is_rejected()
    {
        await using var app = BuildApp("CSRFTOKEN");
        await app.StartAsync();
        var client = app.GetTestClient();

        var res = await client.PostAsync("/api/echo", new StringContent(""));
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Mutating_request_with_correct_csrf_header_passes()
    {
        await using var app = BuildApp("CSRFTOKEN");
        await app.StartAsync();
        var client = app.GetTestClient();

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/echo") { Content = new StringContent("") };
        req.Headers.Add("X-Admin-CSRF", "CSRFTOKEN");
        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Csrf_endpoint_returns_the_token()
    {
        await using var app = BuildApp("CSRFTOKEN");
        await app.StartAsync();
        var client = app.GetTestClient();

        var json = await client.GetStringAsync("/api/csrf");
        Assert.Contains("CSRFTOKEN", json);
    }
}
```

- [ ] **Step 2 : Lancer les tests pour vérifier qu'ils échouent**

Run: `dotnet test --filter "FullyQualifiedName~AdminCsrfTests"`
Expected: ÉCHEC de compilation — `AdminEndpoints.UseCsrf`/`MapCsrf` introuvables.

- [ ] **Step 3 : Ajouter CSRF + SameSite dans AdminEndpoints.cs**

Dans `src/Photobooth.Admin/AdminEndpoints.cs`, remplacer la ligne du cookie (dans le `POST /login`) :

```csharp
                ctx.Response.Cookies.Append(CookieName, authToken, new CookieOptions { HttpOnly = true });
```

par :

```csharp
                ctx.Response.Cookies.Append(CookieName, authToken,
                    new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict });
```

Puis ajouter, dans la classe `AdminEndpoints` (après la méthode `UseAuth`), le middleware CSRF et son endpoint :

```csharp
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
```

> `FixedTimeEquals` (privé) et `using Microsoft.AspNetCore.Http;` existent déjà dans ce fichier (Plan 2/3). `HttpMethods`/`IEndpointRouteBuilder` viennent de `Microsoft.AspNetCore.Http`/`Microsoft.AspNetCore.Routing` (déjà importés).

- [ ] **Step 4 : Lancer les tests CSRF pour vérifier qu'ils passent**

Run: `dotnet test --filter "FullyQualifiedName~AdminCsrfTests"`
Expected: PASS (3 tests).

- [ ] **Step 5 : Refactor AdminWebHost (ctor IServiceProvider + CSRF + warning no-PIN)**

Remplacer **tout le contenu** de `src/Photobooth.Admin/AdminWebHost.cs` par :

```csharp
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Photobooth.Core.Abstractions;
using Photobooth.Core.Diagnostics;
using Photobooth.Core.Options;

namespace Photobooth.Admin;

/// <summary>
/// Hôte web Kestrel embarqué. Démarré seulement si Admin.Enabled. Tout échec de démarrage (bind, port
/// occupé, adresse invalide) est loggé puis avalé : la borne n'est jamais dégradée par le mode debug.
/// Récupère les singletons partagés depuis le conteneur racine de l'app et les re-déclare dans le
/// conteneur interne du WebApplication (injection minimal-API). Read-write : auth PIN + CSRF.
/// </summary>
public sealed class AdminWebHost : IAsyncDisposable
{
    private readonly IServiceProvider _services;
    private readonly AdminOptions _opt;
    private readonly ILogger<AdminWebHost> _log;
    private readonly string _authToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
    private readonly string _csrfToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
    private WebApplication? _app;

    public AdminWebHost(
        IServiceProvider services,
        IOptions<AdminOptions> options,
        ILogger<AdminWebHost> log)
    {
        _services = services;
        _opt = options.Value;
        _log = log;
    }

    /// <summary>URL effectivement écoutée après StartAsync, ou null si désactivé/échec.</summary>
    public string? BoundUrl { get; private set; }

    public async Task StartAsync()
    {
        if (!_opt.Enabled)
            return;

        if (string.IsNullOrEmpty(_opt.Pin))
            _log.LogWarning(
                "ADMIN ACTIVÉ SANS PIN : surface complète (console root via sudo) exposée sur {Addr}:{Port} " +
                "sans authentification. Définissez Admin.Pin pour fermer cette porte.",
                _opt.ListenAddress, _opt.Port);

        try
        {
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders(); // Serilog gère déjà les logs applicatifs.

            // Forward des singletons partagés (lecture) vers le conteneur de l'hôte.
            Forward<BoothTelemetry>(builder.Services);
            Forward<InMemoryLogSink>(builder.Services);
            Forward<IPrinterAdapter>(builder.Services);
            Forward<IOptions<PrinterOptions>>(builder.Services);
            // [PLAN 3/3 — Task 11] forwards write ajoutés ici.

            builder.WebHost.UseUrls($"http://{_opt.ListenAddress}:{_opt.Port}");

            var app = builder.Build();
            AdminEndpoints.UseAuth(app, _opt, _authToken);
            AdminEndpoints.UseCsrf(app, _csrfToken);
            AdminEndpoints.MapApi(app);
            AdminEndpoints.MapCsrf(app, _csrfToken);
            // [PLAN 3/3 — Task 11] MapPrinter/MapActions/MapConfig/MapConsole/MapLogStream ajoutés ici.
            AdminEndpoints.MapPage(app);
            await app.StartAsync();

            var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses;
            BoundUrl = addresses?.FirstOrDefault();
            _app = app;
            _log.LogInformation("Hôte admin à l'écoute sur {Url}.", BoundUrl);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Démarrage de l'hôte admin échoué ; la borne continue sans lui.");
            BoundUrl = null;
            _app = null;
        }
    }

    // Récupère un singleton du conteneur racine et le re-déclare dans l'hôte (no-op si absent).
    private void Forward<T>(IServiceCollection dst) where T : class
    {
        var instance = _services.GetService<T>();
        if (instance is not null)
            dst.AddSingleton(instance);
    }

    public async Task StopAsync()
    {
        if (_app is null) return;
        var app = _app;
        _app = null;
        BoundUrl = null;
        try
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
        catch (Exception ex) { _log.LogWarning(ex, "Arrêt de l'hôte admin échoué (ignoré)."); }
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
```

- [ ] **Step 6 : Adapter AdminWebHostTests au nouveau ctor**

Dans `src/Photobooth.Tests/AdminWebHostTests.cs`, ajouter en tête `using Microsoft.Extensions.DependencyInjection;` (avec les autres `using`) et remplacer la méthode `Build` par :

```csharp
    private static AdminWebHost Build(AdminOptions opt)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new BoothTelemetry());
        services.AddSingleton(new InMemoryLogSink());
        services.AddSingleton<IPrinterAdapter>(new StubPrinter());
        services.AddSingleton(Options.Create(new PrinterOptions()));
        return new AdminWebHost(
            services.BuildServiceProvider(),
            Options.Create(opt),
            NullLogger<AdminWebHost>.Instance);
    }
```

> Les 3 `[Fact]` existants (`Disabled_does_not_listen`, `Enabled_serves_logs_endpoint_on_loopback`, `Bind_failure_is_degraded_not_fatal`) restent inchangés ; ils n'utilisaient que `/api/logs` (read).

- [ ] **Step 7 : Renforcer l'assertion SameSite dans AdminEndpointsTests**

Dans `src/Photobooth.Tests/AdminEndpointsTests.cs`, méthode `Login_sets_cookie_on_correct_pin_only`, après l'assertion existante sur `padmin=TESTTOKEN`, ajouter :

```csharp
        Assert.Contains(ok.Headers.GetValues("Set-Cookie"),
            v => v.Contains("SameSite=Strict", System.StringComparison.OrdinalIgnoreCase));
```

- [ ] **Step 8 : Build + non-régression**

Run: `dotnet build Photobooth.sln`
Expected: Build succeeded, 0 erreur, 0 warning.

Run: `dotnet test`
Expected: PASS — suite complète verte (52 existants + nouveaux CSRF).

- [ ] **Step 9 : Commit**

```bash
git add src/Photobooth.Admin/AdminEndpoints.cs src/Photobooth.Admin/AdminWebHost.cs src/Photobooth.Tests/AdminWebHostTests.cs src/Photobooth.Tests/AdminEndpointsTests.cs src/Photobooth.Tests/AdminCsrfTests.cs
git commit -m "$(printf 'feat(admin): durcissement read-write (CSRF, SameSite=Strict, warning no-PIN) + hote sur IServiceProvider\n\nCo-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>')"
```

---

## Task 3: Réécriture de admin.html (onglets, textContent, panneaux write, CSRF, SSE)

**Files:**
- Modify (réécriture complète) : `src/Photobooth.Admin/admin.html`
- Test: `src/Photobooth.Tests/AdminEndpointsTests.cs` (ajout d'assertions sur la page)

**Interfaces:**
- Consumes (côté navigateur) : `GET /api/status`, `GET /api/logs`, `GET /api/logs/stream` (SSE), `GET /api/csrf`, `GET /api/printer`, `GET /api/printer/usb|cups-log|queue`, `POST /api/printer/enable|accept|test|purge`, `GET/PUT /api/config`, `POST /api/actions/restart|reboot|recover-gopro`, `POST /api/console`.
- Produces: page autonome (CSS/JS inline, zéro ressource externe). Les panneaux dont l'endpoint n'existe pas encore (tâches ultérieures) se dégradent proprement (« indisponible »).

> **Sécurité (Global Constraints)** : toute donnée dynamique (logs, sorties de commande, statut imprimante) est posée via `textContent`/création de nœuds, **jamais** via `innerHTML`. Les mutations passent par `post()`/`put()` qui ajoutent l'en-tête `X-Admin-CSRF`.

- [ ] **Step 1 : Écrire le test qui échoue**

Dans `src/Photobooth.Tests/AdminEndpointsTests.cs`, ajouter ce `[Fact]` à la classe :

```csharp
    [Fact]
    public async Task Root_page_has_tabs_and_no_unsafe_log_innerhtml()
    {
        await using var app = BuildApp(new BoothTelemetry(), new InMemoryLogSink(), printerEnabled: false);
        AdminEndpoints.MapPage(app);
        await app.StartAsync();
        var client = app.GetTestClient();

        var html = await client.GetStringAsync("/");
        Assert.Contains("Borne photo", html);
        Assert.Contains("data-tab=\"printer\"", html);
        Assert.Contains("data-tab=\"console\"", html);
        Assert.Contains("data-tab=\"config\"", html);
        // Le compromis XSS read-only du 2/3 ne doit plus exister : pas d'innerHTML des logs.
        Assert.DoesNotContain("box.innerHTML = rows", html);
        Assert.Contains("textContent", html);
    }
```

- [ ] **Step 2 : Lancer le test pour vérifier qu'il échoue**

Run: `dotnet test --filter "FullyQualifiedName~AdminEndpointsTests.Root_page_has_tabs_and_no_unsafe_log_innerhtml"`
Expected: ÉCHEC — `data-tab="printer"` absent de la page actuelle.

- [ ] **Step 3 : Réécrire la page**

Remplacer **tout le contenu** de `src/Photobooth.Admin/admin.html` par :

```html
<!doctype html>
<html lang="fr">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Borne photo — admin</title>
<style>
  :root { font-family: system-ui, sans-serif; }
  body { margin: 0; background: #111; color: #eee; }
  header { padding: .8rem 1rem; background: #1c1c1c; display: flex; gap: 1rem; align-items: center; flex-wrap: wrap; }
  .badge { padding: .2rem .6rem; border-radius: .4rem; background: #333; font-size: .9rem; }
  .dot { width: .8rem; height: .8rem; border-radius: 50%; display: inline-block; }
  .ok { background: #2e9b4f; } .ko { background: #c0392b; } .unknown { background: #888; }
  nav { display: flex; gap: .3rem; padding: .4rem 1rem; background: #181818; flex-wrap: wrap; }
  nav button { background: #2a2a2a; color: #ddd; border: 0; padding: .5rem .9rem; border-radius: .4rem; cursor: pointer; }
  nav button.active { background: #3a6ea5; color: #fff; }
  main { padding: 1rem; }
  .tab { display: none; } .tab.active { display: block; }
  .card { background: #1c1c1c; border-radius: .5rem; padding: 1rem; margin-bottom: 1rem; }
  .fail { border-left: 4px solid #c0392b; } .ok-print { border-left: 4px solid #2e9b4f; }
  button.act { background: #3a6ea5; color: #fff; border: 0; padding: .5rem .9rem; border-radius: .4rem; cursor: pointer; margin: .2rem .3rem .2rem 0; }
  button.danger { background: #8e2b22; }
  pre, #logs { font-family: ui-monospace, monospace; font-size: .8rem; white-space: pre-wrap;
          max-height: 50vh; overflow: auto; background: #000; padding: .6rem; border-radius: .4rem; }
  textarea { width: 100%; min-height: 16rem; background: #000; color: #cfe; font-family: ui-monospace, monospace; font-size: .8rem; }
  input[type=text] { width: 100%; padding: .5rem; font-family: ui-monospace, monospace; }
  .lvl { display: inline-block; width: 5rem; color: #9cf; }
  .Warning { color: #f1c40f; } .Error, .Fatal { color: #e74c3c; }
  .muted { color: #888; font-size: .85rem; }
  .filters button { margin-right: .3rem; }
  footer { padding: .6rem 1rem; color: #888; font-size: .8rem; }
</style>
</head>
<body>
<header>
  <strong>Borne photo</strong>
  <span class="badge">État : <span id="state">…</span></span>
  <span class="badge">GoPro <span id="gopro" class="dot unknown"></span></span>
  <span class="badge">Imprimante : <span id="printer">…</span></span>
  <span class="badge" id="freshness">MAJ…</span>
</header>
<nav>
  <button data-tab="dashboard" class="active">Dashboard</button>
  <button data-tab="logs">Logs</button>
  <button data-tab="printer">Imprimante</button>
  <button data-tab="config">Config</button>
  <button data-tab="actions">Actions</button>
  <button data-tab="console">Console</button>
</nav>
<main>
  <section class="tab active" id="tab-dashboard">
    <div class="card" id="lastprint-card">
      <h2>Dernière impression</h2>
      <div id="lastprint">Aucune impression.</div>
    </div>
  </section>

  <section class="tab" id="tab-logs">
    <div class="card">
      <h2>Logs <span class="muted" id="logmode">(polling)</span></h2>
      <div class="filters">
        <button data-f="All">Tous</button>
        <button data-f="Information">Info</button>
        <button data-f="Warning">Warning</button>
        <button data-f="Error">Error</button>
      </div>
      <div id="logs">…</div>
    </div>
  </section>

  <section class="tab" id="tab-printer">
    <div class="card">
      <h2>État imprimante <span class="muted">(modifs runtime — temporaires, réinitialisées au reboot)</span></h2>
      <div id="printer-badges"></div>
      <button class="act" data-printer="enable">Réactiver (cupsenable)</button>
      <button class="act" data-printer="accept">Accepter (cupsaccept)</button>
      <button class="act" data-printer="test">Test d'impression</button>
      <button class="act" data-printer="purge">Purger la file</button>
      <button class="act" data-printer="usb">Détecter USB (lpinfo)</button>
      <pre id="printer-out">…</pre>
    </div>
    <div class="card">
      <h2>File CUPS (lpq)</h2><pre id="printer-queue">—</pre>
      <h2>Logs CUPS (error_log)</h2><pre id="printer-cupslog">—</pre>
    </div>
  </section>

  <section class="tab" id="tab-config">
    <div class="card">
      <h2>Config (photobooth.json)</h2>
      <p class="muted">Édition de la config opérateur. « Appliquer » valide, écrit le fichier puis <strong>redémarre la borne</strong>.</p>
      <textarea id="config-text" spellcheck="false"></textarea>
      <div><button class="act" id="config-apply">Appliquer + redémarrer</button></div>
      <div id="config-msg" class="muted"></div>
    </div>
  </section>

  <section class="tab" id="tab-actions">
    <div class="card">
      <h2>Actions</h2>
      <button class="act" data-action="recover-gopro">Reprise GoPro</button>
      <button class="act" data-action="restart">Redémarrer l'app</button>
      <button class="act danger" data-action="reboot">Redémarrer la borne</button>
      <pre id="actions-out">—</pre>
    </div>
  </section>

  <section class="tab" id="tab-console">
    <div class="card">
      <h2>Console <span class="muted">(exécutée en pi ; sudo NOPASSWD disponible)</span></h2>
      <input type="text" id="console-cmd" placeholder="ex: sudo systemctl status photobooth --no-pager">
      <div><button class="act" id="console-run">Exécuter</button></div>
      <pre id="console-out">—</pre>
    </div>
  </section>
</main>
<footer><span id="version"></span> · <span id="time"></span></footer>
<script>
  let filter = "All", lastLines = [], csrf = "", live = null;

  function dot(v){ return v === true ? "ok" : v === false ? "ko" : "unknown"; }
  function $(id){ return document.getElementById(id); }

  async function getCsrf(){
    try { csrf = (await (await fetch("/api/csrf")).json()).token || ""; } catch (e) { csrf = ""; }
  }
  async function send(method, url, body){
    const opt = { method, headers: { "X-Admin-CSRF": csrf } };
    if (body !== undefined) { opt.headers["Content-Type"] = "application/json"; opt.body = body; }
    const res = await fetch(url, opt);
    let data = null; try { data = await res.json(); } catch (e) {}
    return { ok: res.ok, status: res.status, data };
  }
  const post = (url, body) => send("POST", url, body);
  const put  = (url, body) => send("PUT", url, body);

  // --- onglets ---
  document.querySelectorAll("nav button").forEach(b => b.onclick = () => {
    document.querySelectorAll("nav button").forEach(x => x.classList.remove("active"));
    document.querySelectorAll(".tab").forEach(x => x.classList.remove("active"));
    b.classList.add("active");
    $("tab-" + b.dataset.tab).classList.add("active");
    if (b.dataset.tab === "printer") refreshPrinter();
    if (b.dataset.tab === "config") loadConfig();
  });

  // --- dashboard + logs ---
  async function refresh(){
    try {
      const s = await (await fetch("/api/status")).json();
      $("state").textContent = s.state;
      $("gopro").className = "dot " + dot(s.goProReachable);
      $("printer").textContent = s.printer.enabled ? s.printer.type : "désactivée";
      const lp = $("lastprint"), card = $("lastprint-card");
      if (!s.lastPrint) { lp.textContent = "Aucune impression."; card.className = "card"; }
      else if (s.lastPrint.succeeded) { lp.textContent = "OK — " + s.lastPrint.at; card.className = "card ok-print"; }
      else { lp.textContent = "ÉCHEC — " + (s.lastPrint.reason || "") + " (" + s.lastPrint.at + ")"; card.className = "card fail"; }
      $("version").textContent = "v" + s.version;
      $("time").textContent = s.serverTimeUtc;
      if (!live) { lastLines = await (await fetch("/api/logs")).json(); renderLogs(); }
      $("freshness").textContent = "MAJ OK";
    } catch (e) { $("freshness").textContent = "hôte injoignable"; }
  }
  function renderLogs(){
    const box = $("logs");
    box.replaceChildren();
    lastLines.filter(l => filter === "All" || l.level === filter).forEach(l => {
      const row = document.createElement("div");
      const lvl = document.createElement("span");
      lvl.className = "lvl " + l.level; lvl.textContent = l.level;
      row.appendChild(lvl);
      row.appendChild(document.createTextNode(l.message + (l.exception ? "\n" + l.exception : "")));
      box.appendChild(row);
    });
    box.scrollTop = box.scrollHeight;
  }
  document.querySelectorAll(".filters button").forEach(b => b.onclick = () => { filter = b.dataset.f; renderLogs(); });

  function startLive(){
    try {
      live = new EventSource("/api/logs/stream");
      live.onopen = () => { lastLines = []; }; // le snapshot initial du flux fait foi (évite les doublons du poll)
      live.onmessage = ev => { try { lastLines.push(JSON.parse(ev.data)); if (lastLines.length > 500) lastLines.shift(); renderLogs(); } catch (e) {} };
      live.onerror = () => { if (live) { live.close(); live = null; $("logmode").textContent = "(polling)"; } };
      $("logmode").textContent = "(live)";
    } catch (e) { live = null; }
  }

  // --- imprimante ---
  function setText(id, r){ $(id).textContent = r ? ((r.exitCode === 0 ? "OK" : "exit " + r.exitCode) + (r.timedOut ? " [timeout]" : "") + "\n" + (r.stdout || "") + (r.stderr ? "\n" + r.stderr : "")) : "—"; }
  async function refreshPrinter(){
    try {
      const d = await (await fetch("/api/printer")).json();
      const b = $("printer-badges"); b.replaceChildren();
      [["enabled", d.enabled], ["accepting", d.accepting]].forEach(([k, v]) => {
        const s = document.createElement("span"); s.className = "badge"; s.textContent = k + " : " + (v === null ? "?" : v); b.appendChild(s);
      });
      const pre = $("printer-out"); pre.textContent = d.raw || "";
      $("printer-queue").textContent = (await (await fetch("/api/printer/queue")).json()).stdout || "—";
      $("printer-cupslog").textContent = (await (await fetch("/api/printer/cups-log")).json()).stdout || "—";
    } catch (e) { $("printer-out").textContent = "indisponible"; }
  }
  document.querySelectorAll("[data-printer]").forEach(b => b.onclick = async () => {
    const a = b.dataset.printer;
    if (a === "usb") { const r = await (await fetch("/api/printer/usb")).json(); setText("printer-out", r); return; }
    const r = await post("/api/printer/" + a);
    setText("printer-out", r.data); refreshPrinter();
  });

  // --- config ---
  async function loadConfig(){
    try { $("config-text").value = await (await fetch("/api/config")).text(); $("config-msg").textContent = ""; }
    catch (e) { $("config-msg").textContent = "lecture impossible"; }
  }
  $("config-apply").onclick = async () => {
    $("config-msg").textContent = "validation…";
    const r = await put("/api/config", $("config-text").value);
    if (r.status === 400) { $("config-msg").textContent = "Refusé : " + (r.data && r.data.error || "config invalide"); return; }
    $("config-msg").textContent = "Appliqué — redémarrage en cours…";
  };

  // --- actions ---
  document.querySelectorAll("[data-action]").forEach(b => b.onclick = async () => {
    const r = await post("/api/actions/" + b.dataset.action);
    $("actions-out").textContent = r.ok ? JSON.stringify(r.data) : ("échec (" + r.status + ")");
  });

  // --- console ---
  $("console-run").onclick = async () => {
    const cmd = $("console-cmd").value;
    if (!cmd) return;
    $("console-out").textContent = "…";
    const r = await post("/api/console", JSON.stringify({ command: cmd }));
    setText("console-out", r.data);
  };

  (async () => { await getCsrf(); await refresh(); startLive(); setInterval(refresh, 3000); })();
</script>
</body>
</html>
```

- [ ] **Step 4 : Lancer le test + non-régression**

Run: `dotnet test --filter "FullyQualifiedName~AdminEndpointsTests"`
Expected: PASS (dont le nouveau cas + les existants `Root_serves_html_page_when_no_pin`, etc.).

Run: `dotnet test`
Expected: PASS — suite complète verte.

- [ ] **Step 5 : Commit**

```bash
git add src/Photobooth.Admin/admin.html src/Photobooth.Tests/AdminEndpointsTests.cs
git commit -m "$(printf 'feat(admin): UI a onglets, textContent (fin XSS read-only), CSRF + SSE cote page\n\nCo-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>')"
```

---

## Task 4: PrinterControl (service de commandes imprimante/CUPS)

**Files:**
- Create: `src/Photobooth.Admin/PrinterControl.cs`
- Create: `src/Photobooth.Tests/FakeProcessRunner.cs`
- Test: `src/Photobooth.Tests/PrinterControlTests.cs`

**Interfaces:**
- Consumes: `IProcessRunner` (Task 1), `IOptions<PrinterOptions>`.
- Produces:
  - `Photobooth.Admin.PrinterDetail(string Raw, bool? Enabled, bool? Accepting)` (record).
  - `Photobooth.Admin.PrinterControl` :
    - `Task<PrinterDetail> StatusAsync(CancellationToken ct = default)`
    - `Task<ProcessResult> EnableAsync(CancellationToken ct = default)` — `sudo cupsenable <queue>`
    - `Task<ProcessResult> AcceptAsync(CancellationToken ct = default)` — `sudo cupsaccept <queue>`
    - `Task<ProcessResult> TestPrintAsync(CancellationToken ct = default)` — `<lp> -d <queue>` + stdin texte
    - `Task<ProcessResult> PurgeAsync(CancellationToken ct = default)` — `cancel -a <queue>`
    - `Task<ProcessResult> DetectUsbAsync(CancellationToken ct = default)` — `sudo lpinfo -v`
    - `Task<ProcessResult> QueueAsync(CancellationToken ct = default)` — `lpq -P <queue>`
    - `Task<ProcessResult> CupsLogAsync(CancellationToken ct = default)` — `sudo tail -n 200 /var/log/cups/error_log`

- [ ] **Step 1 : Créer le fake runner partagé**

Créer `src/Photobooth.Tests/FakeProcessRunner.cs` :

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Photobooth.Admin;

namespace Photobooth.Tests;

/// <summary>Faux IProcessRunner : enregistre les appels, renvoie un résultat configurable.</summary>
internal sealed class FakeProcessRunner : IProcessRunner
{
    public List<(string File, string[] Args, string? Stdin)> Calls { get; } = new();
    public ProcessResult Result { get; set; } = new(0, "", "", false);
    public Func<string, string[], ProcessResult>? OnRun { get; set; }

    public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments,
        string? stdin = null, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var args = arguments.ToArray();
        Calls.Add((fileName, args, stdin));
        return Task.FromResult(OnRun?.Invoke(fileName, args) ?? Result);
    }

    public Task<ProcessResult> RunShellAsync(string command,
        string? stdin = null, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        Calls.Add(("shell", new[] { command }, stdin));
        return Task.FromResult(OnRun?.Invoke("shell", new[] { command }) ?? Result);
    }
}
```

- [ ] **Step 2 : Écrire les tests qui échouent**

Créer `src/Photobooth.Tests/PrinterControlTests.cs` :

```csharp
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Photobooth.Admin;
using Photobooth.Core.Options;
using Xunit;

namespace Photobooth.Tests;

public sealed class PrinterControlTests
{
    private static PrinterControl Build(FakeProcessRunner runner, string queue = "photobooth-printer") =>
        new(runner, Options.Create(new PrinterOptions { Name = queue, LpCommand = "lp" }));

    [Fact]
    public async Task Enable_uses_sudo_cupsenable_on_the_queue()
    {
        var runner = new FakeProcessRunner();
        await Build(runner).EnableAsync();

        var call = runner.Calls.Single();
        Assert.Equal("sudo", call.File);
        Assert.Equal(new[] { "cupsenable", "photobooth-printer" }, call.Args);
    }

    [Fact]
    public async Task TestPrint_sends_to_the_queue_with_stdin()
    {
        var runner = new FakeProcessRunner();
        await Build(runner).TestPrintAsync();

        var call = runner.Calls.Single();
        Assert.Equal("lp", call.File);
        Assert.Equal(new[] { "-d", "photobooth-printer" }, call.Args);
        Assert.False(string.IsNullOrEmpty(call.Stdin));
    }

    [Fact]
    public async Task DetectUsb_uses_sudo_lpinfo_v()
    {
        var runner = new FakeProcessRunner();
        await Build(runner).DetectUsbAsync();

        var call = runner.Calls.Single();
        Assert.Equal("sudo", call.File);
        Assert.Equal(new[] { "lpinfo", "-v" }, call.Args);
    }

    [Fact]
    public async Task Status_parses_enabled_and_accepting()
    {
        var runner = new FakeProcessRunner
        {
            Result = new ProcessResult(0,
                "printer photobooth-printer is idle.  enabled since ...\n" +
                "photobooth-printer accepting requests since ...", "", false)
        };
        var d = await Build(runner).StatusAsync();

        Assert.True(d.Enabled);
        Assert.True(d.Accepting);
        Assert.Contains("photobooth-printer", d.Raw);
    }

    [Fact]
    public async Task Status_detects_disabled_and_rejecting()
    {
        var runner = new FakeProcessRunner
        {
            Result = new ProcessResult(0,
                "printer photobooth-printer disabled since ...\n" +
                "photobooth-printer not accepting requests", "", false)
        };
        var d = await Build(runner).StatusAsync();

        Assert.False(d.Enabled);
        Assert.False(d.Accepting);
    }
}
```

- [ ] **Step 3 : Lancer les tests pour vérifier qu'ils échouent**

Run: `dotnet test --filter "FullyQualifiedName~PrinterControlTests"`
Expected: ÉCHEC de compilation — `PrinterControl`/`PrinterDetail` introuvables.

- [ ] **Step 4 : Écrire l'implémentation**

Créer `src/Photobooth.Admin/PrinterControl.cs` :

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Photobooth.Core.Options;

namespace Photobooth.Admin;

/// <summary>État imprimante exposé à l'onglet imprimante.</summary>
public sealed record PrinterDetail(string Raw, bool? Enabled, bool? Accepting);

/// <summary>
/// Commandes imprimante/CUPS de l'onglet imprimante (debug terrain §8). Lecture en user pi
/// (lpstat/lpq) ; cupsenable/cupsaccept/lpinfo + lecture error_log via sudo (root). Les modifs
/// runtime (enable/accept) sont temporaires sous l'overlay (réinitialisées au reboot, §14.3).
/// </summary>
public sealed class PrinterControl
{
    private readonly IProcessRunner _runner;
    private readonly PrinterOptions _opt;

    public PrinterControl(IProcessRunner runner, IOptions<PrinterOptions> options)
    {
        _runner = runner;
        _opt = options.Value;
    }

    private string Queue => _opt.Name;

    public async Task<PrinterDetail> StatusAsync(CancellationToken ct = default)
    {
        var r = await _runner.RunAsync("lpstat", new[] { "-p", Queue, "-a", Queue }, ct: ct);
        var text = r.Stdout + "\n" + r.Stderr;
        bool? enabled = text.Contains("disabled", StringComparison.OrdinalIgnoreCase) ? false
            : text.Contains("enabled", StringComparison.OrdinalIgnoreCase) ? true : null;
        bool? accepting = text.Contains("not accepting", StringComparison.OrdinalIgnoreCase) ? false
            : text.Contains("accepting", StringComparison.OrdinalIgnoreCase) ? true : null;
        return new PrinterDetail(r.Stdout, enabled, accepting);
    }

    public Task<ProcessResult> EnableAsync(CancellationToken ct = default) =>
        _runner.RunAsync("sudo", new[] { "cupsenable", Queue }, ct: ct);

    public Task<ProcessResult> AcceptAsync(CancellationToken ct = default) =>
        _runner.RunAsync("sudo", new[] { "cupsaccept", Queue }, ct: ct);

    public Task<ProcessResult> TestPrintAsync(CancellationToken ct = default) =>
        _runner.RunAsync(_opt.LpCommand, new[] { "-d", Queue },
            stdin: $"Photobooth — test d'impression\n{DateTimeOffset.UtcNow:u}\n", ct: ct);

    public Task<ProcessResult> PurgeAsync(CancellationToken ct = default) =>
        _runner.RunAsync("cancel", new[] { "-a", Queue }, ct: ct);

    public Task<ProcessResult> DetectUsbAsync(CancellationToken ct = default) =>
        _runner.RunAsync("sudo", new[] { "lpinfo", "-v" }, ct: ct);

    public Task<ProcessResult> QueueAsync(CancellationToken ct = default) =>
        _runner.RunAsync("lpq", new[] { "-P", Queue }, ct: ct);

    public Task<ProcessResult> CupsLogAsync(CancellationToken ct = default) =>
        _runner.RunAsync("sudo", new[] { "tail", "-n", "200", "/var/log/cups/error_log" }, ct: ct);
}
```

- [ ] **Step 5 : Lancer les tests pour vérifier qu'ils passent**

Run: `dotnet test --filter "FullyQualifiedName~PrinterControlTests"`
Expected: PASS (5 tests).

- [ ] **Step 6 : Commit**

```bash
git add src/Photobooth.Admin/PrinterControl.cs src/Photobooth.Tests/FakeProcessRunner.cs src/Photobooth.Tests/PrinterControlTests.cs
git commit -m "$(printf 'feat(admin): PrinterControl (lpstat/cupsenable/accept/test/lpinfo/lpq/cancel/error_log)\n\nCo-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>')"
```

---

## Task 5: Endpoints imprimante (MapPrinter) testés en mémoire

**Files:**
- Modify: `src/Photobooth.Admin/AdminEndpoints.cs` (`MapPrinter`)
- Test: `src/Photobooth.Tests/AdminWriteEndpointsTests.cs` (créé ; section imprimante)

**Interfaces:**
- Consumes: `PrinterControl` (Task 4).
- Produces: `AdminEndpoints.MapPrinter(IEndpointRouteBuilder app)` — mappe :
  - `GET  /api/printer` → `PrinterDetail`
  - `GET  /api/printer/usb` / `/queue` / `/cups-log` → `ProcessResult`
  - `POST /api/printer/enable` / `/accept` / `/test` / `/purge` → `ProcessResult`

- [ ] **Step 1 : Écrire les tests qui échouent**

Créer `src/Photobooth.Tests/AdminWriteEndpointsTests.cs` :

```csharp
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Photobooth.Admin;
using Photobooth.Core.Options;
using Xunit;

namespace Photobooth.Tests;

public sealed class AdminWriteEndpointsTests
{
    private static WebApplication BuildPrinterApp(FakeProcessRunner runner)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<IProcessRunner>(runner);
        builder.Services.AddSingleton(new PrinterControl(runner,
            Options.Create(new PrinterOptions { Name = "photobooth-printer", LpCommand = "lp" })));
        var app = builder.Build();
        AdminEndpoints.MapPrinter(app);
        return app;
    }

    [Fact]
    public async Task Get_printer_returns_detail()
    {
        var runner = new FakeProcessRunner
        {
            Result = new ProcessResult(0, "printer photobooth-printer is idle. enabled since x\nphotobooth-printer accepting requests", "", false)
        };
        await using var app = BuildPrinterApp(runner);
        await app.StartAsync();
        var client = app.GetTestClient();

        var d = await client.GetFromJsonAsync<PrinterDetail>("/api/printer");
        Assert.NotNull(d);
        Assert.True(d!.Enabled);
    }

    [Fact]
    public async Task Post_enable_invokes_sudo_cupsenable()
    {
        var runner = new FakeProcessRunner();
        await using var app = BuildPrinterApp(runner);
        await app.StartAsync();
        var client = app.GetTestClient();

        var res = await client.PostAsync("/api/printer/enable", null);
        res.EnsureSuccessStatusCode();
        Assert.Contains(runner.Calls, c => c.File == "sudo" && c.Args.Length == 2 && c.Args[0] == "cupsenable");
    }
}
```

- [ ] **Step 2 : Lancer les tests pour vérifier qu'ils échouent**

Run: `dotnet test --filter "FullyQualifiedName~AdminWriteEndpointsTests"`
Expected: ÉCHEC de compilation — `AdminEndpoints.MapPrinter` introuvable.

- [ ] **Step 3 : Écrire MapPrinter**

Dans `src/Photobooth.Admin/AdminEndpoints.cs`, ajouter la méthode dans la classe `AdminEndpoints` :

```csharp
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
```

- [ ] **Step 4 : Lancer les tests + non-régression**

Run: `dotnet test --filter "FullyQualifiedName~AdminWriteEndpointsTests"`
Expected: PASS (2 tests).

Run: `dotnet test`
Expected: PASS — suite complète verte.

- [ ] **Step 5 : Commit**

```bash
git add src/Photobooth.Admin/AdminEndpoints.cs src/Photobooth.Tests/AdminWriteEndpointsTests.cs
git commit -m "$(printf 'feat(admin): endpoints imprimante (status + cupsenable/accept/test/purge/usb/queue/cups-log)\n\nCo-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>')"
```

---

## Task 6: IBoothCommandSink + PrivilegedActions + endpoints Actions

**Files:**
- Create: `src/Photobooth.Core/Workflow/IBoothCommandSink.cs`
- Modify: `src/Photobooth.Core/Workflow/PhotoboothWorkflow.cs:28` (implémenter l'interface)
- Create: `src/Photobooth.Admin/PrivilegedActions.cs`
- Modify: `src/Photobooth.Admin/AdminEndpoints.cs` (`MapActions`)
- Test: `src/Photobooth.Tests/PrivilegedActionsTests.cs` (créé)
- Test: `src/Photobooth.Tests/AdminWriteEndpointsTests.cs` (ajout section actions)

**Interfaces:**
- Consumes: `IProcessRunner` (Task 1), `BoothCommand` (`Photobooth.Core.Workflow`).
- Produces:
  - `Photobooth.Core.Workflow.IBoothCommandSink` : `void Submit(BoothCommand command)`.
  - `Photobooth.Admin.PrivilegedActions` :
    - `Task<ProcessResult> RestartAppAsync(CancellationToken ct = default)` — `sudo systemctl restart photobooth`
    - `Task<ProcessResult> RebootAsync(CancellationToken ct = default)` — `sudo systemctl reboot`
  - `AdminEndpoints.MapActions(IEndpointRouteBuilder app)` — `POST /api/actions/restart|reboot|recover-gopro`.

- [ ] **Step 1 : Écrire les tests qui échouent**

Créer `src/Photobooth.Tests/PrivilegedActionsTests.cs` :

```csharp
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Photobooth.Admin;
using Xunit;

namespace Photobooth.Tests;

public sealed class PrivilegedActionsTests
{
    [Fact]
    public async Task Restart_uses_sudo_systemctl_restart_photobooth()
    {
        var runner = new FakeProcessRunner();
        var pa = new PrivilegedActions(runner, NullLogger<PrivilegedActions>.Instance);

        await pa.RestartAppAsync();

        var call = runner.Calls.Single();
        Assert.Equal("sudo", call.File);
        Assert.Equal(new[] { "systemctl", "restart", "photobooth" }, call.Args);
    }

    [Fact]
    public async Task Reboot_uses_sudo_systemctl_reboot()
    {
        var runner = new FakeProcessRunner();
        var pa = new PrivilegedActions(runner, NullLogger<PrivilegedActions>.Instance);

        await pa.RebootAsync();

        Assert.Equal(new[] { "systemctl", "reboot" }, runner.Calls.Single().Args);
    }
}
```

- [ ] **Step 2 : Lancer les tests pour vérifier qu'ils échouent**

Run: `dotnet test --filter "FullyQualifiedName~PrivilegedActionsTests"`
Expected: ÉCHEC de compilation — `PrivilegedActions` introuvable.

- [ ] **Step 3 : Créer IBoothCommandSink et l'implémenter sur le workflow**

Créer `src/Photobooth.Core/Workflow/IBoothCommandSink.cs` :

```csharp
namespace Photobooth.Core.Workflow;

/// <summary>
/// Point d'injection de commandes dans le workflow, exposé aux composants externes (hôte admin)
/// sans leur donner accès à toute la surface du workflow. Implémenté par PhotoboothWorkflow.
/// </summary>
public interface IBoothCommandSink
{
    void Submit(BoothCommand command);
}
```

Dans `src/Photobooth.Core/Workflow/PhotoboothWorkflow.cs`, remplacer la déclaration de classe (ligne 28) :

```csharp
public sealed class PhotoboothWorkflow : IAsyncDisposable
```

par :

```csharp
public sealed class PhotoboothWorkflow : IAsyncDisposable, IBoothCommandSink
```

> La méthode `public void Submit(BoothCommand command)` (ligne 90) satisfait déjà l'interface — aucun autre changement.

- [ ] **Step 4 : Écrire PrivilegedActions**

Créer `src/Photobooth.Admin/PrivilegedActions.cs` :

```csharp
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
```

- [ ] **Step 5 : Lancer les tests unitaires + écrire l'endpoint**

Run: `dotnet test --filter "FullyQualifiedName~PrivilegedActionsTests"`
Expected: PASS (2 tests).

Dans `src/Photobooth.Admin/AdminEndpoints.cs`, ajouter dans la classe :

```csharp
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
```

> **Note** : aucun nouveau `using` n'est requis — les types `IBoothCommandSink`/`BoothCommand` sont référencés en nom pleinement qualifié (`Photobooth.Core.Workflow.*`).

- [ ] **Step 6 : Ajouter le test d'endpoint actions**

Dans `src/Photobooth.Tests/AdminWriteEndpointsTests.cs`, ajouter (en tête, `using Microsoft.Extensions.Logging.Abstractions;` et `using Photobooth.Core.Workflow;`) puis ce helper + test à la classe :

```csharp
    private sealed class RecordingSink : Photobooth.Core.Workflow.IBoothCommandSink
    {
        public int Count { get; private set; }
        public void Submit(Photobooth.Core.Workflow.BoothCommand command) => Count++;
    }

    [Fact]
    public async Task Recover_gopro_submits_a_recovered_command()
    {
        var sink = new RecordingSink();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<Photobooth.Core.Workflow.IBoothCommandSink>(sink);
        builder.Services.AddSingleton(new PrivilegedActions(new FakeProcessRunner(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PrivilegedActions>.Instance));
        var app = builder.Build();
        AdminEndpoints.MapActions(app);
        await app.StartAsync();
        var client = app.GetTestClient();

        var res = await client.PostAsync("/api/actions/recover-gopro", null);
        res.EnsureSuccessStatusCode();
        Assert.Equal(1, sink.Count);
    }
```

- [ ] **Step 7 : Lancer les tests + non-régression**

Run: `dotnet test --filter "FullyQualifiedName~AdminWriteEndpointsTests"`
Expected: PASS.

Run: `dotnet test`
Expected: PASS — suite complète verte.

- [ ] **Step 8 : Commit**

```bash
git add src/Photobooth.Core/Workflow/IBoothCommandSink.cs src/Photobooth.Core/Workflow/PhotoboothWorkflow.cs src/Photobooth.Admin/PrivilegedActions.cs src/Photobooth.Admin/AdminEndpoints.cs src/Photobooth.Tests/PrivilegedActionsTests.cs src/Photobooth.Tests/AdminWriteEndpointsTests.cs
git commit -m "$(printf 'feat(admin): actions privilegiees (restart/reboot via sudo) + reprise GoPro\n\nCo-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>')"
```

---

## Task 7: ConfigStore + endpoints config (GET/PUT, validation, écriture atomique, restart)

**Files:**
- Create: `src/Photobooth.Admin/ConfigStore.cs`
- Modify: `src/Photobooth.Admin/AdminEndpoints.cs` (`MapConfig`)
- Test: `src/Photobooth.Tests/ConfigStoreTests.cs` (créé)
- Test: `src/Photobooth.Tests/AdminWriteEndpointsTests.cs` (ajout section config)

**Interfaces:**
- Consumes: `IProcessRunner` (Task 1), `PrivilegedActions` (Task 6), classes `*Options` + `Validate()` (Core).
- Produces:
  - `Photobooth.Admin.AdminConfigTarget(string Path)` (record) — chemin du `photobooth.json` à éditer.
  - `Photobooth.Admin.ConfigStore` :
    - `Task<string> ReadAsync(CancellationToken ct = default)` — texte du fichier, `"{}"` si absent.
    - `string? Validate(string json)` — null si valide, sinon message (réutilise les `Validate()` existants).
    - `Task WriteAsync(string json, CancellationToken ct = default)` — écriture atomique (temp+rename) ; repli `sudo` helper sur `UnauthorizedAccessException`.
  - `AdminEndpoints.MapConfig(IEndpointRouteBuilder app)` — `GET /api/config` (texte), `PUT /api/config` (valide→400 ou écrit+restart).

- [ ] **Step 1 : Écrire les tests qui échouent**

Créer `src/Photobooth.Tests/ConfigStoreTests.cs` :

```csharp
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Photobooth.Admin;
using Xunit;

namespace Photobooth.Tests;

public sealed class ConfigStoreTests
{
    private static (ConfigStore store, string path) Build()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pb-cfg-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "photobooth.json");
        var store = new ConfigStore(new AdminConfigTarget(path), new FakeProcessRunner(),
            NullLogger<ConfigStore>.Instance);
        return (store, path);
    }

    [Fact]
    public async Task Read_returns_empty_object_when_absent()
    {
        var (store, _) = Build();
        Assert.Equal("{}", (await store.ReadAsync()).Trim());
    }

    [Fact]
    public void Validate_rejects_invalid_printer_section()
    {
        var (store, _) = Build();
        var err = store.Validate("{ \"Printer\": { \"Type\": \"cups\", \"Copies\": 0 } }");
        Assert.NotNull(err);
        Assert.Contains("Copies", err);
    }

    [Fact]
    public void Validate_rejects_malformed_json()
    {
        var (store, _) = Build();
        Assert.NotNull(store.Validate("{ not json"));
    }

    [Fact]
    public void Validate_accepts_a_valid_document()
    {
        var (store, _) = Build();
        Assert.Null(store.Validate("{ \"Printer\": { \"Type\": \"cups\", \"Copies\": 1 } }"));
    }

    [Fact]
    public async Task Write_persists_atomically_to_the_target()
    {
        var (store, path) = Build();
        await store.WriteAsync("{ \"Printer\": { \"Copies\": 2 } }");
        Assert.True(File.Exists(path));
        Assert.Contains("Copies", await File.ReadAllTextAsync(path));
    }
}
```

- [ ] **Step 2 : Lancer les tests pour vérifier qu'ils échouent**

Run: `dotnet test --filter "FullyQualifiedName~ConfigStoreTests"`
Expected: ÉCHEC de compilation — `ConfigStore`/`AdminConfigTarget` introuvables.

- [ ] **Step 3 : Écrire ConfigStore**

Créer `src/Photobooth.Admin/ConfigStore.cs` :

```csharp
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Photobooth.Core.Options;

namespace Photobooth.Admin;

/// <summary>Chemin du photobooth.json éditable (FAT32 sur la borne, ./config en dev).</summary>
public sealed record AdminConfigTarget(string Path);

/// <summary>
/// Lecture / validation / écriture du photobooth.json opérateur. La validation réutilise les
/// Validate() existants des classes Options (zéro duplication, §6). L'écriture est atomique
/// (temp + rename, résiste à une coupure secteur sur FAT32, §14.1) ; si l'app (pi) n'a pas le droit
/// d'écrire (FAT32 root sur la borne), repli sur un helper root via sudo.
/// </summary>
public sealed class ConfigStore
{
    private const string Helper = "/usr/local/sbin/photobooth-write-config.sh";

    private readonly AdminConfigTarget _target;
    private readonly IProcessRunner _runner;
    private readonly ILogger<ConfigStore> _log;

    public ConfigStore(AdminConfigTarget target, IProcessRunner runner, ILogger<ConfigStore> log)
    {
        _target = target;
        _runner = runner;
        _log = log;
    }

    public async Task<string> ReadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_target.Path))
            return "{}";
        return await File.ReadAllTextAsync(_target.Path, ct);
    }

    public string? Validate(string json)
    {
        IConfiguration cfg;
        try
        {
            cfg = new ConfigurationBuilder()
                .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json)))
                .Build();
        }
        catch (Exception ex)
        {
            return $"JSON invalide : {ex.Message}";
        }

        return cfg.GetSection(GoProOptions.Section).Get<GoProOptions>()?.Validate()
            ?? cfg.GetSection(HardwareOptions.Section).Get<HardwareOptions>()?.Validate()
            ?? cfg.GetSection(TimingOptions.Section).Get<TimingOptions>()?.Validate()
            ?? cfg.GetSection(ThemeOptions.Section).Get<ThemeOptions>()?.Validate()
            ?? cfg.GetSection(PrinterOptions.Section).Get<PrinterOptions>()?.Validate()
            ?? cfg.GetSection(AdminOptions.Section).Get<AdminOptions>()?.Validate();
    }

    public async Task WriteAsync(string json, CancellationToken ct = default)
    {
        var dir = Path.GetDirectoryName(_target.Path)!;
        try
        {
            Directory.CreateDirectory(dir);
            var tmp = Path.Combine(dir, "." + Path.GetFileName(_target.Path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            await File.WriteAllTextAsync(tmp, json, ct);
            File.Move(tmp, _target.Path, overwrite: true);
        }
        catch (UnauthorizedAccessException)
        {
            _log.LogInformation("Écriture config directe refusée ; repli sur le helper root via sudo.");
            var r = await _runner.RunAsync("sudo", new[] { Helper }, stdin: json, ct: ct);
            if (r.ExitCode != 0)
                throw new IOException($"Helper d'écriture config échoué (exit {r.ExitCode}) : {r.Stderr}");
        }
    }
}
```

- [ ] **Step 4 : Lancer les tests unitaires pour vérifier qu'ils passent**

Run: `dotnet test --filter "FullyQualifiedName~ConfigStoreTests"`
Expected: PASS (5 tests).

- [ ] **Step 5 : Écrire MapConfig + son test**

Dans `src/Photobooth.Admin/AdminEndpoints.cs`, ajouter dans la classe (ajouter en tête `using System.IO;` s'il n'y est pas — il y est déjà depuis le Plan 2/3) :

```csharp
    /// <summary>Onglet config (§6/Étape 4). PUT valide via les Validate() existants, écrit, restart.</summary>
    public static void MapConfig(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/config", async (ConfigStore store) =>
            Results.Text(await store.ReadAsync(), "application/json"));

        app.MapPut("/api/config", async (HttpContext ctx, ConfigStore store, PrivilegedActions pa) =>
        {
            using var reader = new StreamReader(ctx.Request.Body);
            var json = await reader.ReadToEndAsync();

            var error = store.Validate(json);
            if (error is not null)
                return Results.Json(new { error }, statusCode: StatusCodes.Status400BadRequest);

            await store.WriteAsync(json);
            var restart = await pa.RestartAppAsync();
            return Results.Json(new { applied = true, restart });
        });
    }
```

Dans `src/Photobooth.Tests/AdminWriteEndpointsTests.cs`, ajouter ce helper + tests (ajouter `using System.IO;`, `using System.Net;`, `using System.Net.Http;` en tête si absents) :

```csharp
    private static (WebApplication app, string path) BuildConfigApp()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pb-cfg-ep-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "photobooth.json");

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        var runner = new FakeProcessRunner();
        builder.Services.AddSingleton(new ConfigStore(new AdminConfigTarget(path), runner,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ConfigStore>.Instance));
        builder.Services.AddSingleton(new PrivilegedActions(runner,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PrivilegedActions>.Instance));
        var app = builder.Build();
        AdminEndpoints.MapConfig(app);
        return (app, path);
    }

    [Fact]
    public async Task Put_invalid_config_is_rejected_400()
    {
        var (app, _) = BuildConfigApp();
        await using var _app = app;
        await app.StartAsync();
        var client = app.GetTestClient();

        var res = await client.PutAsync("/api/config",
            new StringContent("{ \"Printer\": { \"Type\": \"cups\", \"Copies\": 0 } }"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Put_valid_config_writes_and_requests_restart()
    {
        var (app, path) = BuildConfigApp();
        await using var _app = app;
        await app.StartAsync();
        var client = app.GetTestClient();

        var res = await client.PutAsync("/api/config",
            new StringContent("{ \"Printer\": { \"Copies\": 3 } }"));
        res.EnsureSuccessStatusCode();
        Assert.True(File.Exists(path));
        Assert.Contains("Copies", await File.ReadAllTextAsync(path));
    }
```

- [ ] **Step 6 : Lancer les tests + non-régression**

Run: `dotnet test --filter "FullyQualifiedName~AdminWriteEndpointsTests"`
Expected: PASS.

Run: `dotnet test`
Expected: PASS — suite complète verte.

- [ ] **Step 7 : Commit**

```bash
git add src/Photobooth.Admin/ConfigStore.cs src/Photobooth.Admin/AdminEndpoints.cs src/Photobooth.Tests/ConfigStoreTests.cs src/Photobooth.Tests/AdminWriteEndpointsTests.cs
git commit -m "$(printf 'feat(admin): config GET/PUT (validation Validate() existants, ecriture atomique, restart)\n\nCo-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>')"
```

---

## Task 8: Flux de logs live (SSE)

**Files:**
- Modify: `src/Photobooth.Admin/InMemoryLogSink.cs` (event `Emitted`)
- Modify: `src/Photobooth.Admin/AdminEndpoints.cs` (`MapLogStream`)
- Test: `src/Photobooth.Tests/InMemoryLogSinkTests.cs` (ajout test event)
- Test: `src/Photobooth.Tests/LogStreamTests.cs` (créé)

**Interfaces:**
- Consumes: `InMemoryLogSink`, `LogLine` (Plan 1/3).
- Produces:
  - `InMemoryLogSink.Emitted` : `event Action<LogLine>?` levé après l'ajout au buffer.
  - `AdminEndpoints.MapLogStream(IEndpointRouteBuilder app)` — `GET /api/logs/stream` (text/event-stream) : snapshot initial puis push des nouveaux events.

- [ ] **Step 1 : Écrire le test unitaire de l'event qui échoue**

Dans `src/Photobooth.Tests/InMemoryLogSinkTests.cs`, ajouter ce `[Fact]` :

```csharp
    [Fact]
    public void Emitted_event_fires_for_each_log()
    {
        var sink = new InMemoryLogSink();
        var received = 0;
        sink.Emitted += _ => received++;
        using (var logger = new Serilog.LoggerConfiguration().WriteTo.Sink(sink).CreateLogger())
            logger.Information("ping");
        Assert.Equal(1, received);
    }
```

- [ ] **Step 2 : Lancer le test pour vérifier qu'il échoue**

Run: `dotnet test --filter "FullyQualifiedName~InMemoryLogSinkTests.Emitted_event_fires_for_each_log"`
Expected: ÉCHEC de compilation — `Emitted` introuvable.

- [ ] **Step 3 : Ajouter l'event au sink**

Dans `src/Photobooth.Admin/InMemoryLogSink.cs` :

Ajouter le champ event dans la classe (après `private readonly LinkedList<LogLine> _lines = new();`) :

```csharp
    /// <summary>Levé après l'ajout d'une ligne au buffer (alimente le flux SSE). Thread-arbitraire.</summary>
    public event System.Action<LogLine>? Emitted;
```

Dans `Emit`, après le bloc `lock` (à la toute fin de la méthode), ajouter :

```csharp
        Emitted?.Invoke(line);
```

- [ ] **Step 4 : Écrire le test SSE qui échoue**

Créer `src/Photobooth.Tests/LogStreamTests.cs` :

```csharp
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Photobooth.Admin;
using Serilog;
using Xunit;

namespace Photobooth.Tests;

public sealed class LogStreamTests
{
    [Fact]
    public async Task Stream_emits_the_initial_snapshot_as_sse()
    {
        var sink = new InMemoryLogSink();
        using (var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger())
            logger.Information("snapshot {Who}", "live");

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(sink);
        await using var app = builder.Build();
        AdminEndpoints.MapLogStream(app);
        await app.StartAsync();
        var client = app.GetTestClient();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var res = await client.GetAsync("/api/logs/stream", HttpCompletionOption.ResponseHeadersRead, cts.Token);
        Assert.Equal("text/event-stream", res.Content.Headers.ContentType?.MediaType);

        await using var stream = await res.Content.ReadAsStreamAsync(cts.Token);
        var buffer = new byte[4096];
        var read = await stream.ReadAsync(buffer, cts.Token);
        var text = System.Text.Encoding.UTF8.GetString(buffer, 0, read);
        Assert.Contains("live", text);
    }
}
```

- [ ] **Step 5 : Lancer le test pour vérifier qu'il échoue**

Run: `dotnet test --filter "FullyQualifiedName~LogStreamTests"`
Expected: ÉCHEC de compilation — `AdminEndpoints.MapLogStream` introuvable.

- [ ] **Step 6 : Écrire MapLogStream**

Dans `src/Photobooth.Admin/AdminEndpoints.cs`, ajouter en tête (avec les autres `using`) :

```csharp
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
```

Puis ajouter dans la classe :

```csharp
    /// <summary>Flux de logs live en Server-Sent Events : snapshot initial puis push (§6).</summary>
    public static void MapLogStream(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/logs/stream", async (HttpContext ctx, InMemoryLogSink sink, CancellationToken ct) =>
        {
            ctx.Response.Headers.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";

            var channel = Channel.CreateUnbounded<LogLine>();
            void Handler(LogLine l) => channel.Writer.TryWrite(l);
            sink.Emitted += Handler;
            try
            {
                foreach (var line in sink.Snapshot())
                    await WriteSse(ctx, line, ct);
                await ctx.Response.Body.FlushAsync(ct);

                while (!ct.IsCancellationRequested)
                {
                    var line = await channel.Reader.ReadAsync(ct);
                    await WriteSse(ctx, line, ct);
                    await ctx.Response.Body.FlushAsync(ct);
                }
            }
            catch (OperationCanceledException) { /* client déconnecté */ }
            finally { sink.Emitted -= Handler; }
        });
    }

    private static async Task WriteSse(HttpContext ctx, LogLine line, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(line, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await ctx.Response.WriteAsync($"data: {json}\n\n", ct);
    }
```

- [ ] **Step 7 : Lancer les tests + non-régression**

Run: `dotnet test --filter "FullyQualifiedName~LogStreamTests"`
Expected: PASS.

Run: `dotnet test --filter "FullyQualifiedName~InMemoryLogSinkTests"`
Expected: PASS (existants + event).

Run: `dotnet test`
Expected: PASS — suite complète verte.

- [ ] **Step 8 : Commit**

```bash
git add src/Photobooth.Admin/InMemoryLogSink.cs src/Photobooth.Admin/AdminEndpoints.cs src/Photobooth.Tests/InMemoryLogSinkTests.cs src/Photobooth.Tests/LogStreamTests.cs
git commit -m "$(printf 'feat(admin): flux de logs live (SSE) + event Emitted sur InMemoryLogSink\n\nCo-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>')"
```

---

## Task 9: ConsoleService + endpoint console

**Files:**
- Create: `src/Photobooth.Admin/ConsoleService.cs`
- Modify: `src/Photobooth.Admin/AdminEndpoints.cs` (`MapConsole`)
- Test: `src/Photobooth.Tests/ConsoleServiceTests.cs` (créé)
- Test: `src/Photobooth.Tests/AdminWriteEndpointsTests.cs` (ajout section console)

**Interfaces:**
- Consumes: `IProcessRunner` (Task 1).
- Produces:
  - `Photobooth.Admin.ConsoleService` : `Task<ProcessResult> RunAsync(string command, CancellationToken ct = default)` — audit-log puis `RunShellAsync` (timeout 30 s).
  - `Photobooth.Admin.ConsoleRequest(string Command)` (record) — corps JSON du POST.
  - `AdminEndpoints.MapConsole(IEndpointRouteBuilder app)` — `POST /api/console` → `ProcessResult`.

- [ ] **Step 1 : Écrire le test qui échoue**

Créer `src/Photobooth.Tests/ConsoleServiceTests.cs` :

```csharp
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Photobooth.Admin;
using Xunit;

namespace Photobooth.Tests;

public sealed class ConsoleServiceTests
{
    [Fact]
    public async Task Run_passes_command_to_the_shell()
    {
        var runner = new FakeProcessRunner { Result = new ProcessResult(0, "hi", "", false) };
        var svc = new ConsoleService(runner, NullLogger<ConsoleService>.Instance);

        var r = await svc.RunAsync("echo hi");

        Assert.Equal(0, r.ExitCode);
        Assert.Equal("shell", runner.Calls.Single().File);
        Assert.Equal("echo hi", runner.Calls.Single().Args[0]);
    }
}
```

- [ ] **Step 2 : Lancer le test pour vérifier qu'il échoue**

Run: `dotnet test --filter "FullyQualifiedName~ConsoleServiceTests"`
Expected: ÉCHEC de compilation — `ConsoleService` introuvable.

- [ ] **Step 3 : Écrire ConsoleService**

Créer `src/Photobooth.Admin/ConsoleService.cs` :

```csharp
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
```

- [ ] **Step 4 : Lancer le test unitaire + écrire l'endpoint**

Run: `dotnet test --filter "FullyQualifiedName~ConsoleServiceTests"`
Expected: PASS (1 test).

Dans `src/Photobooth.Admin/AdminEndpoints.cs`, ajouter dans la classe :

```csharp
    /// <summary>Onglet console (D8). Soumis au middleware CSRF + audit-log côté service.</summary>
    public static void MapConsole(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/console", async (ConsoleRequest req, ConsoleService console) =>
        {
            if (string.IsNullOrWhiteSpace(req.Command))
                return Results.Json(new { error = "commande vide" }, statusCode: StatusCodes.Status400BadRequest);
            return Results.Json(await console.RunAsync(req.Command));
        });
    }
```

- [ ] **Step 5 : Ajouter le test d'endpoint console**

Dans `src/Photobooth.Tests/AdminWriteEndpointsTests.cs`, ajouter ce test (utilise `System.Net.Http.Json` déjà importé) :

```csharp
    [Fact]
    public async Task Post_console_runs_command_and_returns_output()
    {
        var runner = new FakeProcessRunner { Result = new ProcessResult(0, "pong", "", false) };
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(new ConsoleService(runner,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ConsoleService>.Instance));
        var app = builder.Build();
        AdminEndpoints.MapConsole(app);
        await app.StartAsync();
        var client = app.GetTestClient();

        var res = await client.PostAsJsonAsync("/api/console", new ConsoleRequest("echo pong"));
        res.EnsureSuccessStatusCode();
        var result = await res.Content.ReadFromJsonAsync<ProcessResult>();
        Assert.NotNull(result);
        Assert.Contains("pong", result!.Stdout);
    }
```

- [ ] **Step 6 : Lancer les tests + non-régression**

Run: `dotnet test --filter "FullyQualifiedName~AdminWriteEndpointsTests"`
Expected: PASS.

Run: `dotnet test`
Expected: PASS — suite complète verte.

- [ ] **Step 7 : Commit**

```bash
git add src/Photobooth.Admin/ConsoleService.cs src/Photobooth.Admin/AdminEndpoints.cs src/Photobooth.Tests/ConsoleServiceTests.cs src/Photobooth.Tests/AdminWriteEndpointsTests.cs
git commit -m "$(printf 'feat(admin): console de commandes arbitraire (shell, audit-log, timeout)\n\nCo-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>')"
```

---

## Task 10: Déploiement — sudoers NOPASSWD + helper d'écriture config + câblage image

**Files:**
- Create: `deploy/sudoers.d/photobooth`
- Create: `deploy/photobooth-write-config.sh`
- Modify: `image-builder/scripts/00-photobooth.sh` (install des deux, après le bloc imprimante)

> **Pas de test CI** : artefacts shell/provisioning. Vérification : `visudo -c -f deploy/sudoers.d/photobooth`, `bash -n deploy/photobooth-write-config.sh`, et la checklist manuelle (Task 11).

- [ ] **Step 1 : Créer le fichier sudoers**

Créer `deploy/sudoers.d/photobooth` (NOPASSWD: ALL — dérogation D9 ; liste blanche abandonnée) :

```
# Photobooth admin (Plan 3/3, dérogation read-write 2026-06-23).
# L'hôte web d'admin exécute des actions système et une console arbitraire en user pi.
# Décision actée : NOPASSWD: ALL (liste blanche abandonnée). La SEULE frontière est Admin.Pin.
# Installé en 0440 root:root par image-builder/scripts/00-photobooth.sh, validé par visudo -c.
pi ALL=(ALL) NOPASSWD: ALL
```

- [ ] **Step 2 : Vérifier la syntaxe sudoers**

Run: `visudo -c -f deploy/sudoers.d/photobooth`
Expected: `deploy/sudoers.d/photobooth: parsed OK`

- [ ] **Step 3 : Créer le helper d'écriture config atomique**

Créer `deploy/photobooth-write-config.sh` :

```bash
#!/usr/bin/env bash
# Écrit photobooth.json sur la FAT32 (root) de façon atomique (temp + rename), depuis stdin.
# Appelé en root via sudo par l'hôte admin (pi) quand l'écriture directe est refusée (overlay/FAT32 root).
# La FAT n'a pas de journal -> temp + rename pour résister à une coupure secteur (§14.1).
set -euo pipefail

DEST="/boot/firmware/photobooth/photobooth.json"
DIR="$(dirname "$DEST")"
mkdir -p "$DIR"

TMP="$(mktemp "$DIR/.photobooth.json.XXXXXX")"
cat > "$TMP"            # stdin -> fichier temporaire
sync "$TMP" 2>/dev/null || true
mv -f "$TMP" "$DEST"    # rename atomique sur le même volume
sync "$DIR" 2>/dev/null || true
echo "config écrite: $DEST"
```

- [ ] **Step 4 : Vérifier la syntaxe du script**

Run: `bash -n deploy/photobooth-write-config.sh`
Expected: (aucune sortie, exit 0).

- [ ] **Step 5 : Câbler l'install dans l'image**

Dans `image-builder/scripts/00-photobooth.sh`, juste après la ligne (≈178) :

```bash
install -m 0644 /files/deploy/systemd/photobooth-printer.service /etc/systemd/system/
```

ajouter ce bloc :

```bash

# -----------------------------------------------------------------------------
# 3.4bis — Admin web (Plan 3/3) : sudoers NOPASSWD + helper écriture config
# -----------------------------------------------------------------------------
say "Installation des privilèges admin (sudoers NOPASSWD + helper config)."
install -m 0440 -o root -g root /files/deploy/sudoers.d/photobooth /etc/sudoers.d/photobooth
visudo -c -f /etc/sudoers.d/photobooth || { warn "sudoers admin invalide — retrait."; rm -f /etc/sudoers.d/photobooth; }
install -m 0755 /files/deploy/photobooth-write-config.sh /usr/local/sbin/photobooth-write-config.sh
sed -i 's/\r$//' /usr/local/sbin/photobooth-write-config.sh
```

- [ ] **Step 6 : Commit**

```bash
git add deploy/sudoers.d/photobooth deploy/photobooth-write-config.sh image-builder/scripts/00-photobooth.sh
git commit -m "$(printf 'feat(deploy): sudoers NOPASSWD + helper ecriture config atomique + cablage image\n\nDerogation read-write 2026-06-23 : pi ALL=(ALL) NOPASSWD: ALL (liste blanche\nabandonnee). Helper root pour ecriture atomique du photobooth.json sur FAT32.\n\nCo-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>')"
```

---

## Task 11: Intégration App (DI + mapping de l'hôte) + vérification finale

**Files:**
- Modify: `src/Photobooth.App/Composition/ServiceConfiguration.cs` (DI des nouveaux services)
- Modify: `src/Photobooth.Admin/AdminWebHost.cs` (forwards + maps write)
- Test: `src/Photobooth.Tests/AdminOptionsTests.cs` (la résolution DI reste verte)

**Interfaces:**
- Consumes: tous les services des Tasks 1–9.
- Produces: l'app vivante expose les endpoints write derrière auth+CSRF ; `AdminWebHost` est résoluble.

- [ ] **Step 1 : Enregistrer les nouveaux services en DI**

Dans `src/Photobooth.App/Composition/ServiceConfiguration.cs`, ajouter en tête (avec les autres `using`) :

```csharp
using Photobooth.Core.Workflow;
```

> `using Photobooth.Admin;`, `using Photobooth.Core.Diagnostics;` et `using System;`/`using System.IO;` sont déjà présents.

Dans `AddPhotobooth`, juste avant `services.AddSingleton<AdminWebHost>();`, ajouter :

```csharp
        // Admin read-write (Plan 3/3).
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<PrinterControl>();
        services.AddSingleton<PrivilegedActions>();
        services.AddSingleton<ConsoleService>();
        services.AddSingleton<ConfigStore>();
        services.AddSingleton<IBoothCommandSink>(sp => sp.GetRequiredService<PhotoboothWorkflow>());
        var adminConfigDir = Environment.GetEnvironmentVariable("PHOTOBOOTH_CONFIG_DIR")
            ?? "/boot/firmware/photobooth";
        services.AddSingleton(new AdminConfigTarget(Path.Combine(adminConfigDir, "photobooth.json")));
```

> `PhotoboothWorkflow` est déjà enregistré en singleton ; `IBoothCommandSink` partage la même instance. `AdminConfigTarget` utilise le même répertoire que le chargement de config dans `Program.cs` (même variable d'env, même défaut).

- [ ] **Step 2 : Câbler les forwards + maps write dans l'hôte**

Dans `src/Photobooth.Admin/AdminWebHost.cs`, méthode `StartAsync` :

Remplacer le commentaire marqueur :

```csharp
            // [PLAN 3/3 — Task 11] forwards write ajoutés ici.
```

par :

```csharp
            Forward<IProcessRunner>(builder.Services);
            Forward<PrinterControl>(builder.Services);
            Forward<PrivilegedActions>(builder.Services);
            Forward<ConsoleService>(builder.Services);
            Forward<ConfigStore>(builder.Services);
            Forward<Photobooth.Core.Workflow.IBoothCommandSink>(builder.Services);
```

Remplacer le commentaire marqueur :

```csharp
            // [PLAN 3/3 — Task 11] MapPrinter/MapActions/MapConfig/MapConsole/MapLogStream ajoutés ici.
```

par :

```csharp
            AdminEndpoints.MapPrinter(app);
            AdminEndpoints.MapActions(app);
            AdminEndpoints.MapConfig(app);
            AdminEndpoints.MapConsole(app);
            AdminEndpoints.MapLogStream(app);
```

- [ ] **Step 3 : Build + validation DI + non-régression complète**

Run: `dotnet build Photobooth.sln`
Expected: Build succeeded, **0 erreur, 0 warning**.

Run: `dotnet test`
Expected: PASS — **toute** la suite verte (52 d'origine + tous les nouveaux).

> Le test `AdminOptionsTests.Invalid_admin_port_is_surfaced_by_ValidateOptions` (Plan 2/3) construit `new ServiceCollection().AddPhotobooth(config)` et résout `ValidateOptions` : il doit rester vert (les nouveaux singletons sont lazy, jamais résolus par `ValidateOptions`).

- [ ] **Step 4 : Vérification manuelle (hors CI — à faire sur dev box puis Pi)**

Sur une machine de dev (config dir inscriptible), lancer l'app avec `Admin.Enabled=true`, `Admin.Pin="1234"` (ex. via `PHOTOBOOTH_Admin__Enabled=true PHOTOBOOTH_Admin__Pin=1234`) et vérifier dans un navigateur :

- [ ] `/` → login PIN, puis page à 6 onglets.
- [ ] Onglet **Logs** : passe en « (live) » (SSE) ; une ligne de log apparaît sans recharger.
- [ ] Onglet **Console** : `echo ok` → sortie `ok` ; une commande inexistante → stderr remontée ; commande loggée (visible dans l'onglet Logs).
- [ ] Onglet **Config** : le JSON se charge ; un `Copies: 0` est refusé (message) ; un JSON valide est écrit (vérifier le fichier sur disque).
- [ ] Mutations sans cookie/CSRF (via `curl`) → 401/403 ; avec → OK.
- [ ] `Admin.Enabled=true` + `Admin.Pin=""` → **warning bruyant** dans les logs au démarrage, surface complète tout de même exposée.
- [ ] Sur **Pi provisionné** (image rebâtie avec Task 10) : `sudo -n true` réussit ; restart/reboot/cupsenable/tail error_log fonctionnent ; config-apply écrit la FAT32 via le helper puis redémarre.

- [ ] **Step 5 : Commit**

```bash
git add src/Photobooth.App/Composition/ServiceConfiguration.cs src/Photobooth.Admin/AdminWebHost.cs
git commit -m "$(printf 'feat(admin): cablage DI + mapping des endpoints write (imprimante/actions/config/console/SSE)\n\nCo-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>')"
```

---

## Self-Review

**1. Couverture spec (périmètre 3/3 d'après le brief + design §6/§7/§8/§12) :**
- §8 Étape 1 (dernier échec) → déjà livré (2/3, `BoothTelemetry.LastPrint` affiché). ✅
- §8 Étape 2 (file CUPS : `lpstat`, `lpq`) → Task 4 (`StatusAsync`/`QueueAsync`) + Task 5. ✅
- §8 Étape 3 (cupsenable/cupsaccept/test/lpinfo + badges) → Task 4/5 + UI Task 3. ✅
- §8 Étape 4 (config Printer éditable → flux §6) → Task 7 (édition JSON section-générique, valide via Validate()). ✅
- §8 Étape 5 (tail error_log, purge `cancel -a`) → Task 4/5 (`CupsLogAsync`/`PurgeAsync`). ✅
- §6 config GET/PUT (valide via Validate() existants → écrit → restart) → Task 7. ✅
- §6 actions (push BoothCommand : recover-gopro ; systemd : restart/reboot) → Task 6. ✅
- §6/D8 console one-shot arbitraire (root via sudo, dérogation) → Task 9. ✅
- §6 logs live SSE → Task 8. ✅
- Modèle de menace read-write (textContent, CSRF, SameSite, warning no-PIN, audit, NOPASSWD) → Global Constraints + Tasks 2/3/6/9 + Task 10. ✅
- Privilèges remontés (sudoers + helper + provisioning) → Task 10. ✅
- Hors périmètre (AP/mDNS/overlay/Exposure/FAT32-logs) → non implémentés (annoncé). ✅

**2. Scan placeholders :** chaque step de code contient le code complet. Le marqueur `using_directive_placeholder` (Task 6, Step 5) est explicitement signalé comme à supprimer (ce n'est pas du code livré). Les marqueurs `// [PLAN 3/3 — Task 11]` dans `AdminWebHost` (posés en Task 2) sont remplacés en Task 11. ✅

**3. Cohérence des types :**
- `ProcessResult(ExitCode, Stdout, Stderr, TimedOut)` : identique entre Task 1 (def.), le `FakeProcessRunner` (Task 4), `PrinterControl`/`PrivilegedActions`/`ConsoleService` (retour), les endpoints (sérialisé camelCase) et le JS (`exitCode`/`stdout`/`stderr`/`timedOut`). ✅
- `IProcessRunner.RunAsync/RunShellAsync` : signatures identiques entre Task 1, le fake, et tous les consommateurs. ✅
- `PrinterControl` méthodes + `PrinterDetail(Raw, Enabled?, Accepting?)` : identiques entre Task 4 (def.), Task 5 (endpoints), JS (`raw`/`enabled`/`accepting`). ✅
- `PrivilegedActions.RestartAppAsync/RebootAsync` : identiques entre Task 6 (def.), Task 7 (PUT config restart) et Task 6 (endpoints). ✅
- `IBoothCommandSink.Submit(BoothCommand)` : déclaré Task 6, implémenté par `PhotoboothWorkflow` (Submit existant), enregistré Task 11. ✅
- `AdminConfigTarget(Path)` / `ConfigStore.ReadAsync/Validate/WriteAsync` : identiques entre Task 7 (def.) et Task 11 (DI). ✅
- `AdminEndpoints.Map{Csrf,Printer,Actions,Config,Console,LogStream}` + `UseCsrf` : signatures stables, appelées par `AdminWebHost` (Tasks 2/11) et les tests. ✅
- `InMemoryLogSink.Emitted` (event `Action<LogLine>`) : déclaré Task 8, consommé par `MapLogStream` (Task 8). ✅

**4. Ambiguïté résolue :**
- **Console arbitraire vs « whitelist »** : décision utilisateur — console root via `sudo`, pas de whitelist (Task 9 + Task 10). Audit-log compensatoire (Task 9). ✅
- **Restart pendant PUT config** : `RestartAppAsync` est `await`é puis on renvoie ; sur Pi réel l'app meurt avant le flush (l'UI affiche « redémarrage en cours », repli polling). En test, `FakeProcessRunner` retourne instantanément → déterministe. ✅
- **Forward optionnel** dans `AdminWebHost` (`GetService` → no-op si absent) : permet à `AdminWebHostTests` (services read seulement) de rester vert sans enregistrer la surface write. En prod, tout est enregistré (Task 11). ✅
- **Console « streamée » (D8/§6)** : ce plan livre une console **bufferisée avec timeout** (sortie complète jusqu'au timeout), plus simple et testable ; le flux *live* est couvert par les logs SSE (Task 8). Le vrai streaming d'une commande longue est une amélioration possible hors-périmètre. ⚠️ *(simplification assumée — à confirmer si le streaming console est requis.)*
