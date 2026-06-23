# Admin/Debug — Phase 1, Plan 2/3 : Visualiseur de debug (lecture seule) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Exposer l'état de la borne et les logs (déjà capturés au Plan 1/3) via un petit hôte web embarqué **optionnel** (off par défaut), avec endpoints JSON + page HTML autonome + PIN optionnel, pour debug sur place sans SSH.

**Architecture:** Un hôte Kestrel `AdminWebHost` dans `Photobooth.Admin` (ASP.NET embarqué via `FrameworkReference`), démarré seulement si `Admin.Enabled`, **dégradé jamais fatal**. Il monte un `WebApplication` minimal-API, y enregistre les singletons déjà résolus (`BoothTelemetry`, `InMemoryLogSink`, `IPrinterAdapter`, options), et mappe des endpoints **lecture seule**. Une extension minimale de `BoothTelemetry` (état borne + joignabilité GoPro) alimente le dashboard. Une page HTML embarquée poll ces endpoints.

**Tech Stack:** .NET 8, ASP.NET Core minimal API (Kestrel, `WebApplication`), Serilog 4.2.0, xUnit 2.9.2 + `Microsoft.AspNetCore.TestHost`, Microsoft.Extensions.Options/DI 8.x.

## Global Constraints

- **TargetFramework** : `net8.0` (hérité de `src/Directory.Build.props` — ne jamais le redéclarer).
- **Publication cible** : `--self-contained` linux-arm64. ASP.NET est embarqué via `<FrameworkReference Include="Microsoft.AspNetCore.App" />` (rien à installer sur le Pi).
- **Framework de test** : xUnit (`[Fact]`, `Assert.*`). Endpoints testés en mémoire via `Microsoft.AspNetCore.TestHost` (pas de vrai socket sauf le test de cycle de vie de `AdminWebHost` qui bind `127.0.0.1:0`).
- **Solution** : `./Photobooth.sln` — aucun nouveau projet ici (tout va dans des projets existants).
- **Thread-safety** : les nouveaux champs de `BoothTelemetry` (`State`, `GoProReachable`) utilisent le verrou interne existant ; lectures depuis des threads de requête web.
- **Dégradé jamais fatal** : tout échec de l'hôte admin (bind, port occupé, adresse invalide) est loggé puis avalé ; la borne tourne sans lui. `Admin.Enabled=false` ⇒ l'hôte ne démarre pas.
- **Lecture seule** : aucun endpoint d'écriture, aucune action, aucun privilège dans ce plan.
- **Style** : `sealed` par défaut ; conventions du repo (commentaires FR/EN tolérés) ; Conventional Commits.
- **Commits** : terminer chaque message par `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`.
- **Pas de régression** : `dotnet test` (suite existante, 34 tests) reste vert après chaque tâche.
- **Sérialisation** : `Results.Json(...)` utilise les défauts web (camelCase) — les tests assertent en camelCase (`state`, `goProReachable`, `lastPrint`, …).

---

## File Structure

- `src/Photobooth.Core/Options/AdminOptions.cs` — **créé** : section `Admin` + `Validate()`.
- `src/Photobooth.Core/Diagnostics/BoothTelemetry.cs` — **modifié** : `State` + `GoProReachable` (+ recorders).
- `src/Photobooth.Core/Workflow/PhotoboothWorkflow.cs` — **modifié** : 2 lignes (record state dans `SetState`, record reachability dans la boucle connectivité).
- `src/Photobooth.Admin/Photobooth.Admin.csproj` — **modifié** : `FrameworkReference` ASP.NET + `EmbeddedResource` `admin.html`.
- `src/Photobooth.Admin/AdminStatus.cs` — **créé** : DTO de statut.
- `src/Photobooth.Admin/AdminEndpoints.cs` — **créé** : mapping minimal-API (api, auth, page).
- `src/Photobooth.Admin/AdminWebHost.cs` — **créé** : cycle de vie Kestrel (start/stop/dégradé).
- `src/Photobooth.Admin/admin.html` — **créé** : page autonome embarquée.
- `src/Photobooth.App/Composition/ServiceConfiguration.cs` — **modifié** : DI + `Validate()` chaîné.
- `src/Photobooth.App/App.axaml.cs` — **modifié** : démarrage dégradé après `workflow.StartAsync()`.
- `src/Photobooth.App/appsettings.json` + `deploy/boot-config/photobooth.json` — **modifiés** : section `Admin`.
- `src/Photobooth.Tests/Photobooth.Tests.csproj` — **modifié** : `FrameworkReference` ASP.NET + `Microsoft.AspNetCore.TestHost`.
- `src/Photobooth.Tests/AdminOptionsTests.cs`, `AdminEndpointsTests.cs`, `AdminWebHostTests.cs` — **créés** ; `BoothTelemetryTests.cs`, `PrintTelemetryTests.cs` — **modifiés** (extension télémétrie).

---

## Task 1: AdminOptions (section de config + validation)

**Files:**
- Create: `src/Photobooth.Core/Options/AdminOptions.cs`
- Test: `src/Photobooth.Tests/AdminOptionsTests.cs`

**Interfaces:**
- Consumes: rien.
- Produces: `Photobooth.Core.Options.AdminOptions` avec `const string Section = "Admin"`, `bool Enabled` (def. `false`), `string ListenAddress` (def. `"0.0.0.0"`), `int Port` (def. `8080`), `string Pin` (def. `""`), et `string? Validate()`.

- [ ] **Step 1 : Écrire les tests qui échouent**

Créer `src/Photobooth.Tests/AdminOptionsTests.cs` :

```csharp
using Photobooth.Core.Options;
using Xunit;

namespace Photobooth.Tests;

public sealed class AdminOptionsTests
{
    [Fact]
    public void Defaults_are_disabled_and_valid()
    {
        var o = new AdminOptions();
        Assert.False(o.Enabled);
        Assert.Equal("0.0.0.0", o.ListenAddress);
        Assert.Equal(8080, o.Port);
        Assert.Equal("", o.Pin);
        Assert.Null(o.Validate());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(70000)]
    public void Port_out_of_range_is_rejected(int port)
    {
        var o = new AdminOptions { Port = port };
        Assert.NotNull(o.Validate());
    }

    [Fact]
    public void Empty_listen_address_is_rejected()
    {
        var o = new AdminOptions { ListenAddress = "  " };
        Assert.NotNull(o.Validate());
    }
}
```

- [ ] **Step 2 : Lancer le test pour vérifier qu'il échoue**

Run: `dotnet test --filter "FullyQualifiedName~AdminOptionsTests"`
Expected: ÉCHEC de compilation — `AdminOptions` introuvable.

- [ ] **Step 3 : Écrire l'implémentation**

Créer `src/Photobooth.Core/Options/AdminOptions.cs` :

```csharp
namespace Photobooth.Core.Options;

/// <summary>
/// Configuration de l'interface web d'admin/debug embarquée (section "Admin").
/// Opt-in : tant que <see cref="Enabled"/> est false, aucun hôte web n'écoute (zéro surface d'attaque).
/// </summary>
public sealed class AdminOptions
{
    public const string Section = "Admin";

    /// <summary>Active l'hôte web d'admin. Défaut false (rien n'écoute).</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Interface d'écoute Kestrel. Sur le Pi terrain la seule iface est le WiFi GoPro.</summary>
    public string ListenAddress { get; set; } = "0.0.0.0";

    /// <summary>Port Kestrel sur l'IP du Pi (distinct du 8080 de la GoPro 10.5.5.9).</summary>
    public int Port { get; set; } = 8080;

    /// <summary>PIN d'accès optionnel : vide = pas d'authentification.</summary>
    public string Pin { get; set; } = "";

    public string? Validate()
    {
        if (Port is < 1 or > 65535)
            return "Admin.Port doit etre compris entre 1 et 65535.";
        if (string.IsNullOrWhiteSpace(ListenAddress))
            return "Admin.ListenAddress ne doit pas etre vide (ex: 0.0.0.0).";
        return null;
    }
}
```

- [ ] **Step 4 : Lancer le test pour vérifier qu'il passe**

Run: `dotnet test --filter "FullyQualifiedName~AdminOptionsTests"`
Expected: PASS (5 cas).

- [ ] **Step 5 : Commit**

```bash
git add src/Photobooth.Core/Options/AdminOptions.cs src/Photobooth.Tests/AdminOptionsTests.cs
git commit -m "$(printf 'feat(admin): AdminOptions (section Admin opt-in) + validation\n\nCo-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>')"
```

---

## Task 2: Étendre BoothTelemetry (état borne + joignabilité GoPro)

**Files:**
- Modify: `src/Photobooth.Core/Diagnostics/BoothTelemetry.cs`
- Modify: `src/Photobooth.Core/Workflow/PhotoboothWorkflow.cs` (`SetState`, `ConnectivityLoopAsync`)
- Modify: `src/Photobooth.Tests/BoothTelemetryTests.cs` (ajout de cas)
- Test: `src/Photobooth.Tests/PrintTelemetryTests.cs` (ajout d'un cas d'intégration)

**Interfaces:**
- Consumes: `BoothTelemetry` (Plan 1/3), `BoothState` (`Photobooth.Core.Workflow`).
- Produces: sur `BoothTelemetry` : `BoothState State { get; }` (def. `Idle`), `bool? GoProReachable { get; }` (def. `null`), `void RecordState(BoothState)`, `void RecordGoProReachable(bool)`.

- [ ] **Step 1 : Écrire les tests unitaires qui échouent**

Ajouter dans `src/Photobooth.Tests/BoothTelemetryTests.cs` (ajouter `using Photobooth.Core.Workflow;` en tête, avec les autres `using`) ces `[Fact]` à la classe `BoothTelemetryTests` :

```csharp
    [Fact]
    public void State_defaults_to_Idle_and_GoProReachable_is_null()
    {
        var t = new BoothTelemetry();
        Assert.Equal(BoothState.Idle, t.State);
        Assert.Null(t.GoProReachable);
    }

    [Fact]
    public void RecordState_and_RecordGoProReachable_are_reflected()
    {
        var t = new BoothTelemetry();
        t.RecordState(BoothState.Degraded);
        t.RecordGoProReachable(false);

        Assert.Equal(BoothState.Degraded, t.State);
        Assert.Equal(false, t.GoProReachable);
    }
```

- [ ] **Step 2 : Lancer le test pour vérifier qu'il échoue**

Run: `dotnet test --filter "FullyQualifiedName~BoothTelemetryTests"`
Expected: ÉCHEC de compilation — `State`/`GoProReachable`/`RecordState`/`RecordGoProReachable` introuvables.

- [ ] **Step 3a : Étendre BoothTelemetry**

Dans `src/Photobooth.Core/Diagnostics/BoothTelemetry.cs`, ajouter l'import en tête (avec les `using` existants) :

```csharp
using Photobooth.Core.Workflow;
```

Ajouter les champs après `private PrintResult? _lastPrint;` :

```csharp
    private BoothState _state = BoothState.Idle;
    private bool? _goProReachable;
```

Ajouter les membres dans la classe (par ex. après la propriété `LastPrint`) :

```csharp
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
```

- [ ] **Step 3b : Brancher dans le workflow**

Dans `src/Photobooth.Core/Workflow/PhotoboothWorkflow.cs`, méthode `SetState` — ajouter la ligne d'enregistrement :

```csharp
    private void SetState(BoothState s)
    {
        Volatile.Write(ref _stateValue, (int)s);
        _telemetry.RecordState(s);
        _log.LogDebug("State -> {State}", s);
    }
```

Toujours dans `PhotoboothWorkflow.cs`, dans `ConnectivityLoopAsync`, juste après le bloc qui calcule `reachable` (`try { reachable = await _gopro.IsReachableAsync(ct); } … catch { reachable = false; }`) et **avant** le `if (reachable != last)` :

```csharp
                _telemetry.RecordGoProReachable(reachable);
```

- [ ] **Step 4 : Lancer les tests unitaires pour vérifier qu'ils passent**

Run: `dotnet test --filter "FullyQualifiedName~BoothTelemetryTests"`
Expected: PASS (tous, anciens + 2 nouveaux).

- [ ] **Step 5 : Ajouter un test d'intégration workflow**

Ajouter dans `src/Photobooth.Tests/PrintTelemetryTests.cs` (même classe ou une nouvelle ; le `using` `Photobooth.Core.Workflow` y est déjà via les types `BoothCommand`/`BoothState`) :

```csharp
    [Fact]
    public async Task Telemetry_tracks_state_and_gopro_reachability()
    {
        var rig = TestHarness.Build(NewFake(), printerEnabled: false);
        await rig.Workflow.StartAsync();
        try
        {
            // La boucle connectivité sonde immédiatement au démarrage (fake renvoie reachable=true).
            Assert.True(await TestHarness.WaitForAsync(() => rig.Telemetry.GoProReachable is not null));

            rig.Workflow.Submit(new BoothCommand.PhotoRequested());
            Assert.True(await TestHarness.WaitForAsync(
                () => rig.Display.PhotoCount >= 1 && rig.Workflow.State == BoothState.Idle));

            // L'état télémétrie suit l'état réel (écrit via SetState).
            Assert.Equal(BoothState.Idle, rig.Telemetry.State);
        }
        finally { await rig.Workflow.DisposeAsync(); }
    }
```

- [ ] **Step 6 : Lancer les tests (ciblé puis complet)**

Run: `dotnet test --filter "FullyQualifiedName~PrintTelemetryTests"`
Expected: PASS.

Run: `dotnet test`
Expected: PASS — toute la suite reste verte.

- [ ] **Step 7 : Commit**

```bash
git add src/Photobooth.Core/Diagnostics/BoothTelemetry.cs src/Photobooth.Core/Workflow/PhotoboothWorkflow.cs src/Photobooth.Tests/BoothTelemetryTests.cs src/Photobooth.Tests/PrintTelemetryTests.cs
git commit -m "$(printf 'feat(telemetry): BoothTelemetry expose etat borne + joignabilite GoPro\n\nState enregistre au point unique SetState ; GoProReachable a chaque sonde\nde la boucle connectivite. Alimente le futur dashboard admin (lecture).\n\nCo-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>')"
```

---

## Task 3: Endpoints lecture (AdminStatus + AdminEndpoints.MapApi) testés en mémoire

**Files:**
- Modify: `src/Photobooth.Admin/Photobooth.Admin.csproj` (FrameworkReference ASP.NET)
- Modify: `src/Photobooth.Tests/Photobooth.Tests.csproj` (FrameworkReference + TestHost)
- Create: `src/Photobooth.Admin/AdminStatus.cs`
- Create: `src/Photobooth.Admin/AdminEndpoints.cs`
- Test: `src/Photobooth.Tests/AdminEndpointsTests.cs`

**Interfaces:**
- Consumes: `BoothTelemetry`, `InMemoryLogSink`, `IPrinterAdapter`, `IOptions<PrinterOptions>`, `PrintResult`.
- Produces:
  - `Photobooth.Admin.AdminStatus(string State, bool? GoProReachable, AdminPrinterInfo Printer, PrintResult? LastPrint, string Version, System.DateTimeOffset ServerTimeUtc)` (record).
  - `Photobooth.Admin.AdminPrinterInfo(bool Enabled, string Type)` (record).
  - `Photobooth.Admin.AdminEndpoints.MapApi(IEndpointRouteBuilder app)` — mappe `GET /api/status` et `GET /api/logs`.

- [ ] **Step 1 : Activer ASP.NET sur Photobooth.Admin**

Dans `src/Photobooth.Admin/Photobooth.Admin.csproj`, ajouter un `ItemGroup` :

```xml
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>
```

- [ ] **Step 2 : Activer ASP.NET + TestHost sur Photobooth.Tests**

Dans `src/Photobooth.Tests/Photobooth.Tests.csproj` :
- ajouter dans le `ItemGroup` des `PackageReference` :
```xml
    <PackageReference Include="Microsoft.AspNetCore.TestHost" Version="8.0.0" />
```
- ajouter un nouvel `ItemGroup` :
```xml
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>
```

- [ ] **Step 3 : Écrire les tests qui échouent**

Créer `src/Photobooth.Tests/AdminEndpointsTests.cs` :

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Photobooth.Admin;
using Photobooth.Core.Abstractions;
using Photobooth.Core.Diagnostics;
using Photobooth.Core.Options;
using Photobooth.Core.Workflow;
using Serilog;
using Xunit;

namespace Photobooth.Tests;

public sealed class AdminEndpointsTests
{
    // Imprimante de test minimale (lecture de IsEnabled uniquement).
    private sealed class StubPrinter : IPrinterAdapter
    {
        public bool IsEnabled { get; init; }
        public System.Threading.Tasks.Task PrintAsync(byte[] imageData, System.Threading.CancellationToken ct = default)
            => System.Threading.Tasks.Task.CompletedTask;
    }

    // Monte un WebApplication en mémoire (TestServer) avec les fakes fournis, puis MapApi.
    private static WebApplication BuildApp(BoothTelemetry tel, InMemoryLogSink sink, bool printerEnabled)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(tel);
        builder.Services.AddSingleton(sink);
        builder.Services.AddSingleton<IPrinterAdapter>(new StubPrinter { IsEnabled = printerEnabled });
        builder.Services.AddSingleton(Options.Create(new PrinterOptions { Type = "cups" }));
        var app = builder.Build();
        AdminEndpoints.MapApi(app);
        return app;
    }

    [Fact]
    public async Task Status_reports_state_printer_and_last_print()
    {
        var tel = new BoothTelemetry();
        tel.RecordState(BoothState.Degraded);
        tel.RecordGoProReachable(true);
        tel.RecordPrintFailure("lp failed with exit code 1: unknown destination");
        var sink = new InMemoryLogSink();

        await using var app = BuildApp(tel, sink, printerEnabled: true);
        await app.StartAsync();
        var client = app.GetTestClient();

        var status = await client.GetFromJsonAsync<AdminStatus>("/api/status");

        Assert.NotNull(status);
        Assert.Equal("Degraded", status!.State);
        Assert.Equal(true, status.GoProReachable);
        Assert.True(status.Printer.Enabled);
        Assert.Equal("cups", status.Printer.Type);
        Assert.NotNull(status.LastPrint);
        Assert.False(status.LastPrint!.Succeeded);
        Assert.Contains("unknown destination", status.LastPrint.Reason);
    }

    [Fact]
    public async Task Logs_returns_buffered_lines()
    {
        var tel = new BoothTelemetry();
        var sink = new InMemoryLogSink();
        using (var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger())
            logger.Information("hello {Who}", "admin");

        await using var app = BuildApp(tel, sink, printerEnabled: false);
        await app.StartAsync();
        var client = app.GetTestClient();

        var lines = await client.GetFromJsonAsync<LogLine[]>("/api/logs");

        Assert.NotNull(lines);
        Assert.Single(lines!);
        Assert.Contains("admin", lines![0].Message);
    }
}
```

- [ ] **Step 4 : Lancer les tests pour vérifier qu'ils échouent**

Run: `dotnet test --filter "FullyQualifiedName~AdminEndpointsTests"`
Expected: ÉCHEC de compilation — `AdminStatus`/`AdminEndpoints` introuvables.

- [ ] **Step 5 : Écrire le DTO de statut**

Créer `src/Photobooth.Admin/AdminStatus.cs` :

```csharp
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
```

- [ ] **Step 6 : Écrire le mapping des endpoints**

Créer `src/Photobooth.Admin/AdminEndpoints.cs` :

```csharp
using System;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
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
```

- [ ] **Step 7 : Lancer les tests + compilation**

Run: `dotnet build Photobooth.sln`
Expected: Build succeeded, 0 erreur.

Run: `dotnet test --filter "FullyQualifiedName~AdminEndpointsTests"`
Expected: PASS (2 tests).

Run: `dotnet test`
Expected: PASS — suite complète verte.

- [ ] **Step 8 : Commit**

```bash
git add src/Photobooth.Admin/AdminStatus.cs src/Photobooth.Admin/AdminEndpoints.cs src/Photobooth.Admin/Photobooth.Admin.csproj src/Photobooth.Tests/Photobooth.Tests.csproj src/Photobooth.Tests/AdminEndpointsTests.cs
git commit -m "$(printf 'feat(admin): endpoints lecture /api/status et /api/logs (minimal API)\n\nASP.NET embarque via FrameworkReference ; testes en memoire (TestHost).\n\nCo-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>')"
```

---

## Task 4: AdminWebHost (cycle de vie Kestrel, dégradé jamais fatal)

**Files:**
- Create: `src/Photobooth.Admin/AdminWebHost.cs`
- Test: `src/Photobooth.Tests/AdminWebHostTests.cs`

**Interfaces:**
- Consumes: `BoothTelemetry`, `InMemoryLogSink`, `IPrinterAdapter`, `IOptions<PrinterOptions>`, `IOptions<AdminOptions>`, `ILogger<AdminWebHost>`, `AdminEndpoints.MapApi`.
- Produces: `Photobooth.Admin.AdminWebHost : IAsyncDisposable` avec `Task StartAsync()`, `Task StopAsync()`, `string? BoundUrl { get; }`.

- [ ] **Step 1 : Écrire les tests qui échouent**

Créer `src/Photobooth.Tests/AdminWebHostTests.cs` :

```csharp
using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Photobooth.Admin;
using Photobooth.Core.Abstractions;
using Photobooth.Core.Diagnostics;
using Photobooth.Core.Options;
using Xunit;

namespace Photobooth.Tests;

public sealed class AdminWebHostTests
{
    private sealed class StubPrinter : IPrinterAdapter
    {
        public bool IsEnabled => false;
        public Task PrintAsync(byte[] imageData, System.Threading.CancellationToken ct = default) => Task.CompletedTask;
    }

    private static AdminWebHost Build(AdminOptions opt) => new(
        new BoothTelemetry(),
        new InMemoryLogSink(),
        new StubPrinter(),
        Options.Create(new PrinterOptions()),
        Options.Create(opt),
        NullLogger<AdminWebHost>.Instance);

    [Fact]
    public async Task Disabled_does_not_listen()
    {
        await using var host = Build(new AdminOptions { Enabled = false });
        await host.StartAsync();
        Assert.Null(host.BoundUrl);
    }

    [Fact]
    public async Task Enabled_serves_logs_endpoint_on_loopback()
    {
        await using var host = Build(new AdminOptions { Enabled = true, ListenAddress = "127.0.0.1", Port = 0 });
        await host.StartAsync();

        Assert.NotNull(host.BoundUrl);
        using var client = new HttpClient();
        var res = await client.GetAsync($"{host.BoundUrl}/api/logs");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        await host.StopAsync();
    }

    [Fact]
    public async Task Bind_failure_is_degraded_not_fatal()
    {
        // Adresse non assignable -> le bind échoue ; StartAsync doit avaler et ne pas lever.
        await using var host = Build(new AdminOptions { Enabled = true, ListenAddress = "203.0.113.1", Port = 8080 });
        await host.StartAsync(); // ne doit pas lever
        Assert.Null(host.BoundUrl);
    }
}
```

- [ ] **Step 2 : Lancer les tests pour vérifier qu'ils échouent**

Run: `dotnet test --filter "FullyQualifiedName~AdminWebHostTests"`
Expected: ÉCHEC de compilation — `AdminWebHost` introuvable.

- [ ] **Step 3 : Écrire l'implémentation**

Créer `src/Photobooth.Admin/AdminWebHost.cs` :

```csharp
using System;
using System.Linq;
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
/// Hôte web Kestrel embarqué (lecture seule). Démarré seulement si Admin.Enabled. Tout échec de
/// démarrage (bind, port occupé, adresse invalide) est loggé puis avalé : la borne n'est jamais
/// dégradée par le mode debug. Lit BoothTelemetry / InMemoryLogSink ; ne dépend pas du workflow.
/// </summary>
public sealed class AdminWebHost : IAsyncDisposable
{
    private readonly BoothTelemetry _telemetry;
    private readonly InMemoryLogSink _logSink;
    private readonly IPrinterAdapter _printer;
    private readonly IOptions<PrinterOptions> _printerOptions;
    private readonly AdminOptions _opt;
    private readonly ILogger<AdminWebHost> _log;
    private WebApplication? _app;

    public AdminWebHost(
        BoothTelemetry telemetry,
        InMemoryLogSink logSink,
        IPrinterAdapter printer,
        IOptions<PrinterOptions> printerOptions,
        IOptions<AdminOptions> options,
        ILogger<AdminWebHost> log)
    {
        _telemetry = telemetry;
        _logSink = logSink;
        _printer = printer;
        _printerOptions = printerOptions;
        _opt = options.Value;
        _log = log;
    }

    /// <summary>URL effectivement écoutée après StartAsync, ou null si désactivé/échec.</summary>
    public string? BoundUrl { get; private set; }

    public async Task StartAsync()
    {
        if (!_opt.Enabled)
            return;

        try
        {
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders(); // Serilog gère déjà les logs applicatifs.
            builder.Services.AddSingleton(_telemetry);
            builder.Services.AddSingleton(_logSink);
            builder.Services.AddSingleton(_printer);
            builder.Services.AddSingleton(_printerOptions);
            builder.WebHost.UseUrls($"http://{_opt.ListenAddress}:{_opt.Port}");

            var app = builder.Build();
            AdminEndpoints.MapApi(app);
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

    public async Task StopAsync()
    {
        if (_app is null) return;
        try { await _app.StopAsync(); }
        catch (Exception ex) { _log.LogWarning(ex, "Arrêt de l'hôte admin échoué (ignoré)."); }
        await _app.DisposeAsync();
        _app = null;
        BoundUrl = null;
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
```

- [ ] **Step 4 : Lancer les tests + non-régression**

Run: `dotnet test --filter "FullyQualifiedName~AdminWebHostTests"`
Expected: PASS (3 tests).

Run: `dotnet test`
Expected: PASS — suite complète verte.

- [ ] **Step 5 : Commit**

```bash
git add src/Photobooth.Admin/AdminWebHost.cs src/Photobooth.Tests/AdminWebHostTests.cs
git commit -m "$(printf 'feat(admin): AdminWebHost (Kestrel embarque) demarre opt-in, degrade jamais fatal\n\nCo-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>')"
```

---

## Task 5: Authentification PIN optionnelle

**Files:**
- Modify: `src/Photobooth.Admin/AdminEndpoints.cs` (middleware d'auth + routes /login)
- Modify: `src/Photobooth.Admin/AdminWebHost.cs` (génère un jeton + appelle l'auth)
- Test: `src/Photobooth.Tests/AdminEndpointsTests.cs` (gate PIN)

**Interfaces:**
- Consumes: `AdminOptions`.
- Produces: `Photobooth.Admin.AdminEndpoints.UseAuth(WebApplication app, AdminOptions opt, string authToken)` — installe le middleware de garde et les routes `GET/POST /login` ; no-op si `opt.Pin` est vide.

- [ ] **Step 1 : Écrire les tests qui échouent**

Ajouter dans `src/Photobooth.Tests/AdminEndpointsTests.cs` : un helper qui monte l'app AVEC auth, puis les cas de gate. Ajouter ces `using` en tête s'ils sont absents : `using System.Collections.Generic;`, `using System.Net.Http;`.

```csharp
    private static WebApplication BuildAppWithAuth(string pin, string token)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(new BoothTelemetry());
        builder.Services.AddSingleton(new InMemoryLogSink());
        builder.Services.AddSingleton<IPrinterAdapter>(new StubPrinter { IsEnabled = false });
        builder.Services.AddSingleton(Options.Create(new PrinterOptions()));
        var app = builder.Build();
        AdminEndpoints.UseAuth(app, new AdminOptions { Pin = pin }, token);
        AdminEndpoints.MapApi(app);
        return app;
    }

    [Fact]
    public async Task Pin_gate_accepts_correct_cookie_only()
    {
        await using var app = BuildAppWithAuth(pin: "1234", token: "TESTTOKEN");
        await app.StartAsync();
        var client = app.GetTestClient();

        // Sans cookie -> 401.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/status")).StatusCode);

        // Mauvais cookie -> 401.
        var wrong = new HttpRequestMessage(HttpMethod.Get, "/api/status");
        wrong.Headers.Add("Cookie", "padmin=WRONG");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(wrong)).StatusCode);

        // Bon cookie (jeton attendu) -> 200.
        var good = new HttpRequestMessage(HttpMethod.Get, "/api/status");
        good.Headers.Add("Cookie", "padmin=TESTTOKEN");
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(good)).StatusCode);
    }

    [Fact]
    public async Task Login_sets_cookie_on_correct_pin_only()
    {
        await using var app = BuildAppWithAuth(pin: "1234", token: "TESTTOKEN");
        await app.StartAsync();
        var client = app.GetTestClient(); // handler TestServer : ne suit pas les redirections.

        // Bon PIN -> 302 vers / + Set-Cookie portant le jeton.
        var ok = await client.PostAsync("/login",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["pin"] = "1234" }));
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
        Assert.Contains(ok.Headers.GetValues("Set-Cookie"), v => v.Contains("padmin=TESTTOKEN"));

        // Mauvais PIN -> aucun Set-Cookie.
        var bad = await client.PostAsync("/login",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["pin"] = "9999" }));
        Assert.False(bad.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Empty_pin_means_no_auth()
    {
        await using var app = BuildAppWithAuth(pin: "", token: "TESTTOKEN");
        await app.StartAsync();
        var client = app.GetTestClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/status")).StatusCode);
    }
```

> Note : le client de TestServer (`GetTestClient`) utilise le handler en mémoire — il **ne suit pas** les redirections et **ne gère pas** de cookie jar. On vérifie donc directement le `302`+`Set-Cookie` côté `/login`, et la garde en présentant explicitement l'en-tête `Cookie: padmin=<jeton>`.

- [ ] **Step 2 : Lancer les tests pour vérifier qu'ils échouent**

Run: `dotnet test --filter "FullyQualifiedName~AdminEndpointsTests"`
Expected: ÉCHEC de compilation — `AdminEndpoints.UseAuth` introuvable.

- [ ] **Step 3 : Implémenter l'auth**

Dans `src/Photobooth.Admin/AdminEndpoints.cs`, ajouter les `using` :

```csharp
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
```

Ajouter la méthode `UseAuth` et un helper dans la classe `AdminEndpoints` :

```csharp
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
            if (ctx.Request.Cookies.TryGetValue(CookieName, out var c) && c == authToken)
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
```

Dans `src/Photobooth.Admin/AdminWebHost.cs`, générer un jeton et appeler `UseAuth` avant `MapApi`. Ajouter le `using` :

```csharp
using System.Security.Cryptography;
```

Ajouter le champ (avec les autres champs) :

```csharp
    private readonly string _authToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
```

Dans `StartAsync`, remplacer la ligne `AdminEndpoints.MapApi(app);` par :

```csharp
            AdminEndpoints.UseAuth(app, _opt, _authToken);
            AdminEndpoints.MapApi(app);
```

- [ ] **Step 4 : Lancer les tests + non-régression**

Run: `dotnet test --filter "FullyQualifiedName~AdminEndpointsTests"`
Expected: PASS (anciens + 3 gate).

Run: `dotnet test`
Expected: PASS — suite complète verte.

- [ ] **Step 5 : Commit**

```bash
git add src/Photobooth.Admin/AdminEndpoints.cs src/Photobooth.Admin/AdminWebHost.cs src/Photobooth.Tests/AdminEndpointsTests.cs
git commit -m "$(printf 'feat(admin): PIN optionnel (cookie opaque) sur l_hote web admin\n\nVide = pas d_auth. Comparaison temps constant ; le PIN n_est jamais stocke\ncote client (cookie = jeton aleatoire de l_hote).\n\nCo-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>')"
```

---

## Task 6: Page HTML embarquée (GET /)

**Files:**
- Create: `src/Photobooth.Admin/admin.html`
- Modify: `src/Photobooth.Admin/Photobooth.Admin.csproj` (EmbeddedResource)
- Modify: `src/Photobooth.Admin/AdminEndpoints.cs` (`AdminPage` + `GET /`)
- Test: `src/Photobooth.Tests/AdminEndpointsTests.cs` (la page est servie / redirigée)

**Interfaces:**
- Consumes: ressource embarquée `Photobooth.Admin.admin.html`.
- Produces: `GET /` sert la page HTML (`text/html`), soumise au middleware d'auth.

- [ ] **Step 1 : Écrire les tests qui échouent**

Ajouter dans `src/Photobooth.Tests/AdminEndpointsTests.cs` :

```csharp
    [Fact]
    public async Task Root_serves_html_page_when_no_pin()
    {
        await using var app = BuildApp(new BoothTelemetry(), new InMemoryLogSink(), printerEnabled: false);
        // BuildApp ne mappe que l'API ; on ajoute la page comme l'hôte réel.
        AdminEndpoints.MapPage(app);
        await app.StartAsync();
        var client = app.GetTestClient();

        var res = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("text/html", res.Content.Headers.ContentType?.MediaType);
        var html = await res.Content.ReadAsStringAsync();
        Assert.Contains("Borne photo", html);
    }
```

> `BuildApp` (Task 3) est réutilisé ; on ajoute juste `AdminEndpoints.MapPage(app)` avant `StartAsync`.

- [ ] **Step 2 : Lancer le test pour vérifier qu'il échoue**

Run: `dotnet test --filter "FullyQualifiedName~AdminEndpointsTests.Root_serves_html_page_when_no_pin"`
Expected: ÉCHEC de compilation — `AdminEndpoints.MapPage` introuvable.

- [ ] **Step 3 : Créer la page embarquée**

Créer `src/Photobooth.Admin/admin.html` (autonome, CSS/JS inline, zéro ressource externe) :

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
  main { padding: 1rem; }
  .card { background: #1c1c1c; border-radius: .5rem; padding: 1rem; margin-bottom: 1rem; }
  .fail { border-left: 4px solid #c0392b; }
  .ok-print { border-left: 4px solid #2e9b4f; }
  #logs { font-family: ui-monospace, monospace; font-size: .8rem; white-space: pre-wrap;
          max-height: 50vh; overflow: auto; background: #000; padding: .6rem; border-radius: .4rem; }
  .lvl { display: inline-block; width: 5rem; color: #9cf; }
  .Warning { color: #f1c40f; } .Error, .Fatal { color: #e74c3c; }
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
<main>
  <div class="card" id="lastprint-card">
    <h2>Dernière impression</h2>
    <div id="lastprint">Aucune impression.</div>
  </div>
  <div class="card">
    <h2>Logs</h2>
    <div class="filters">
      <button data-f="All">Tous</button>
      <button data-f="Information">Info</button>
      <button data-f="Warning">Warning</button>
      <button data-f="Error">Error</button>
    </div>
    <div id="logs">…</div>
  </div>
</main>
<footer><span id="version"></span> · <span id="time"></span></footer>
<script>
  let filter = "All", lastLines = [];
  function dot(v){ return v === true ? "ok" : v === false ? "ko" : "unknown"; }
  async function refresh(){
    try {
      const s = await (await fetch("/api/status")).json();
      document.getElementById("state").textContent = s.state;
      document.getElementById("gopro").className = "dot " + dot(s.goProReachable);
      document.getElementById("printer").textContent = s.printer.enabled ? s.printer.type : "désactivée";
      const lp = document.getElementById("lastprint"), card = document.getElementById("lastprint-card");
      if (!s.lastPrint) { lp.textContent = "Aucune impression."; card.className = "card"; }
      else if (s.lastPrint.succeeded) { lp.textContent = "OK — " + s.lastPrint.at; card.className = "card ok-print"; }
      else { lp.textContent = "ÉCHEC — " + (s.lastPrint.reason || "") + " (" + s.lastPrint.at + ")"; card.className = "card fail"; }
      document.getElementById("version").textContent = "v" + s.version;
      document.getElementById("time").textContent = s.serverTimeUtc;
      lastLines = await (await fetch("/api/logs")).json();
      renderLogs();
      document.getElementById("freshness").textContent = "MAJ OK";
    } catch (e) {
      document.getElementById("freshness").textContent = "hôte injoignable";
    }
  }
  function renderLogs(){
    const box = document.getElementById("logs");
    const rows = lastLines.filter(l => filter === "All" || l.level === filter)
      .map(l => '<div><span class="lvl ' + l.level + '">' + l.level + '</span>' +
                l.message + (l.exception ? "\n" + l.exception : "") + '</div>').join("");
    box.innerHTML = rows; box.scrollTop = box.scrollHeight;
  }
  document.querySelectorAll(".filters button").forEach(b =>
    b.onclick = () => { filter = b.dataset.f; renderLogs(); });
  refresh(); setInterval(refresh, 3000);
</script>
</body>
</html>
```

- [ ] **Step 4 : Embarquer la ressource**

Dans `src/Photobooth.Admin/Photobooth.Admin.csproj`, ajouter un `ItemGroup` :

```xml
  <ItemGroup>
    <EmbeddedResource Include="admin.html" />
  </ItemGroup>
```

- [ ] **Step 5 : Servir la page**

Dans `src/Photobooth.Admin/AdminEndpoints.cs`, ajouter les `using` si absents :

```csharp
using System.IO;
using System.Reflection;
```

Ajouter dans la classe `AdminEndpoints` le chargement de la page + le mapping :

```csharp
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
```

Dans `src/Photobooth.Admin/AdminWebHost.cs`, `StartAsync`, ajouter l'appel après `MapApi` :

```csharp
            AdminEndpoints.MapPage(app);
```

- [ ] **Step 6 : Lancer les tests + non-régression**

Run: `dotnet test --filter "FullyQualifiedName~AdminEndpointsTests"`
Expected: PASS.

Run: `dotnet build Photobooth.sln` puis `dotnet test`
Expected: Build 0 erreur ; suite complète verte.

- [ ] **Step 7 : Commit**

```bash
git add src/Photobooth.Admin/admin.html src/Photobooth.Admin/Photobooth.Admin.csproj src/Photobooth.Admin/AdminEndpoints.cs src/Photobooth.Admin/AdminWebHost.cs src/Photobooth.Tests/AdminEndpointsTests.cs
git commit -m "$(printf 'feat(admin): page HTML embarquee autonome (status + logs, polling)\n\nCo-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>')"
```

---

## Task 7: Intégration App (DI, démarrage dégradé, config)

**Files:**
- Modify: `src/Photobooth.App/Composition/ServiceConfiguration.cs`
- Modify: `src/Photobooth.App/App.axaml.cs`
- Modify: `src/Photobooth.App/appsettings.json`
- Modify: `deploy/boot-config/photobooth.json`
- Test: `src/Photobooth.Tests/AdminOptionsTests.cs` (validation chaînée via DI)

**Interfaces:**
- Consumes: `AdminOptions`, `AdminWebHost`, `ServiceConfiguration.ValidateOptions`.
- Produces: l'app enregistre/valide `AdminOptions` et démarre `AdminWebHost` (opt-in) au lancement.

- [ ] **Step 1 : Écrire le test de validation chaînée**

Ajouter dans `src/Photobooth.Tests/AdminOptionsTests.cs` (ajouter en tête `using Microsoft.Extensions.DependencyInjection;`, `using Microsoft.Extensions.Configuration;`, `using Photobooth.App.Composition;` ; le projet de tests référence déjà Photobooth.App via la chaîne de références — sinon ajouter la ProjectReference, voir note) :

```csharp
    [Fact]
    public void Invalid_admin_port_is_surfaced_by_ValidateOptions()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Admin:Port"] = "0" })
            .Build();
        var sp = new ServiceCollection().AddPhotobooth(config).BuildServiceProvider();

        var error = ServiceConfiguration.ValidateOptions(sp);

        Assert.NotNull(error);
        Assert.Contains("Admin.Port", error);
    }
```

> Note : si `Photobooth.Tests` ne référence pas encore `Photobooth.App`, ajouter dans `Photobooth.Tests.csproj` (ItemGroup ProjectReference) :
> `<ProjectReference Include="..\Photobooth.App\Photobooth.App.csproj" />`
> et `using System.Collections.Generic;` en tête du fichier de test.

- [ ] **Step 2 : Lancer le test pour vérifier qu'il échoue**

Run: `dotnet test --filter "FullyQualifiedName~AdminOptionsTests.Invalid_admin_port_is_surfaced_by_ValidateOptions"`
Expected: ÉCHEC — `Admin.Port` n'est pas encore validé (error null), ou erreur de compilation si `AddPhotobooth`/`ValidateOptions` non visibles.

- [ ] **Step 3 : Enregistrer + valider AdminOptions en DI**

Dans `src/Photobooth.App/Composition/ServiceConfiguration.cs` :

Ajouter les `using` (avec les autres) :

```csharp
using Photobooth.Admin;
using Photobooth.Core.Diagnostics;
```

Dans `AddPhotobooth`, après `services.Configure<PrinterOptions>(...)` :

```csharp
        services.Configure<AdminOptions>(config.GetSection(AdminOptions.Section));
```

Toujours dans `AddPhotobooth`, après `services.AddSingleton<PhotoboothWorkflow>();` :

```csharp
        services.AddSingleton<AdminWebHost>();
```

Dans `ValidateOptions`, chaîner `AdminOptions.Validate()` à la fin de la chaîne `??` :

```csharp
    public static string? ValidateOptions(IServiceProvider sp)
    {
        return sp.GetRequiredService<IOptions<GoProOptions>>().Value.Validate()
            ?? sp.GetRequiredService<IOptions<HardwareOptions>>().Value.Validate()
            ?? sp.GetRequiredService<IOptions<TimingOptions>>().Value.Validate()
            ?? sp.GetRequiredService<IOptions<ThemeOptions>>().Value.Validate()
            ?? sp.GetRequiredService<IOptions<PrinterOptions>>().Value.Validate()
            ?? sp.GetRequiredService<IOptions<AdminOptions>>().Value.Validate();
    }
```

> `BoothTelemetry` et `InMemoryLogSink` sont déjà des singletons DI (Plan 1/3), donc le constructeur de `AdminWebHost` est résoluble.

- [ ] **Step 4 : Démarrer l'hôte au lancement (dégradé)**

Dans `src/Photobooth.App/App.axaml.cs`, ajouter le `using` :

```csharp
using Photobooth.Admin;
```

Dans `OnFrameworkInitializationCompleted`, juste après `_ = workflow.StartAsync();` (ligne ~99), ajouter :

```csharp
        // Hôte d'admin/debug optionnel (off par défaut). Tout échec est dégradé, jamais fatal :
        // la borne ne doit jamais tomber à cause du mode debug. Le conteneur (IAsyncDisposable)
        // arrête l'hôte à l'extinction, comme le workflow.
        try
        {
            var adminOpt = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Photobooth.Core.Options.AdminOptions>>().Value;
            if (adminOpt.Enabled)
                _ = sp.GetRequiredService<AdminWebHost>().StartAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Démarrage de l'hôte admin ignoré (mode dégradé).");
        }
```

- [ ] **Step 5 : Déclarer la section Admin dans la config**

Dans `src/Photobooth.App/appsettings.json`, ajouter une section `Admin` (au même niveau que `Printer`/`Logging`) :

```jsonc
  "Admin": {
    "Enabled": false,
    "ListenAddress": "0.0.0.0",
    "Port": 8080,
    "Pin": ""
  },
```

Dans `deploy/boot-config/photobooth.json`, ajouter la même section `Admin` (valeurs par défaut, `Enabled` à `false`) avec un commentaire opérateur si le fichier en contient déjà ailleurs (suivre le style du fichier).

- [ ] **Step 6 : Vérifier compilation, validation et non-régression**

Run: `dotnet build Photobooth.sln`
Expected: Build succeeded, 0 erreur.

Run: `dotnet test --filter "FullyQualifiedName~AdminOptionsTests"`
Expected: PASS (dont le cas de validation chaînée).

Run: `dotnet test`
Expected: PASS — suite complète verte (existants + nouveaux).

- [ ] **Step 7 : Commit**

```bash
git add src/Photobooth.App/Composition/ServiceConfiguration.cs src/Photobooth.App/App.axaml.cs src/Photobooth.App/appsettings.json deploy/boot-config/photobooth.json src/Photobooth.Tests/Photobooth.Tests.csproj src/Photobooth.Tests/AdminOptionsTests.cs
git commit -m "$(printf 'feat(admin): cablage DI + demarrage opt-in degrade de l_hote admin\n\nAdminOptions enregistre et valide ; AdminWebHost demarre apres le workflow\nsi Admin.Enabled, sous try/catch ; arret via le conteneur (IAsyncDisposable).\nSection Admin ajoutee a appsettings.json et au modele boot-config.\n\nCo-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>')"
```

---

## Self-Review

**1. Couverture spec** :
- §2/§4 `AdminOptions` opt-in + Validate → Task 1. ✅
- §3 extension `BoothTelemetry` (State + GoProReachable) → Task 2. ✅
- §5 endpoints `/api/status` + `/api/logs` → Task 3. ✅
- §3/§7 `AdminWebHost` Kestrel dégradé + `FrameworkReference` → Task 4. ✅
- §5 PIN optionnel (login + cookie) → Task 5. ✅
- §6 page HTML embarquée autonome → Task 6. ✅
- §8 points d'intégration (DI, démarrage dégradé, config) → Task 7. ✅
- §9 tests (options, télémétrie, endpoints en mémoire, gate PIN, cycle de vie) → répartis Tasks 1–7. ✅
- Hors périmètre (actions/config/console/SSE/AP/mDNS/overlay/exposure) : non implémentés (annoncé §2). ✅

**2. Scan placeholders** : aucun TBD/TODO ; chaque step de code contient le code complet. ✅

**3. Cohérence des types** :
- `AdminOptions` : `Section`/`Enabled`/`ListenAddress`/`Port`/`Pin`/`Validate()` identiques entre Task 1 (def.) et Tasks 4/7 (usage). ✅
- `BoothTelemetry.State`/`GoProReachable`/`RecordState`/`RecordGoProReachable` : identiques entre Task 2 (def.) et Task 3 (lecture endpoint) / tests. ✅
- `AdminStatus`/`AdminPrinterInfo` : champs identiques entre Task 3 (def.), le JS de la page (Task 6, camelCase) et les tests. ✅
- `AdminEndpoints.MapApi`/`UseAuth`/`MapPage` : signatures stables, appelées par `AdminWebHost` (Tasks 4/5/6) et les tests. ✅
- `AdminWebHost` ctor (6 paramètres) : identique entre Task 4 (def.) et l'enregistrement DI Task 7 (résolu par type). ✅

**4. Ambiguïté** :
- Sérialisation camelCase : assumée (défaut `Results.Json`) ; tests désérialisent en camelCase via les records → robustes. ✅
- TestServer + cookies : le client TestServer ne gère pas le cookie jar ; le test de gate présente explicitement l'en-tête `Cookie` (documenté en note Task 5). ✅
- `BoundUrl` via `IServerAddressesFeature` après `UseUrls(:0)` → port éphémère résolu ; test loopback non-flaky. ✅
