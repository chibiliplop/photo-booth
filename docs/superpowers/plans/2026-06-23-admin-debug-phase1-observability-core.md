# Admin/Debug — Phase 1, Plan 1/3 : Socle d'observabilité — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> ✅ **Implémenté le 2026-06-23** sur `feat/printer` — commits `e74185e..3cefad8` (les 3 tâches). Revue par tâche (spec + qualité) puis revue de branche : OK, 0 finding Critical/Important. `dotnet test` : **34/34 vert**.

**Goal:** Capturer dans l'app la vraie raison d'un échec d'impression (aujourd'hui avalée) et garder en RAM un tampon des derniers logs, comme fondation lecture de la future UI d'admin.

**Architecture:** Un singleton `BoothTelemetry` (modèle pur, thread-safe) dans `Photobooth.Core`, alimenté par le workflow au point exact où l'exception d'impression était jusqu'ici seulement logguée puis remplacée par un « Impression impossible » générique (`PhotoboothWorkflow.cs`). Un sink Serilog `InMemoryLogSink` (ring buffer) dans un nouveau projet `Photobooth.Admin`, branché sur le pipeline Serilog existant de `Program.cs`. Aucune UI, aucun serveur web dans ce plan.

**Tech Stack:** .NET 8, xUnit 2.9.2, Serilog 4.2.0, Microsoft.Extensions.DependencyInjection/Options 8.x.

## Global Constraints

- **TargetFramework** : `net8.0` (hérité de `src/Directory.Build.props` — ne pas le redéclarer dans les nouveaux csproj).
- **Publication cible** : `--self-contained` linux-arm64 (aucune dépendance runtime à installer sur le Pi).
- **Framework de test** : xUnit (`[Fact]`, `Assert.*`). Commande : `dotnet test`.
- **Solution** : `./Photobooth.sln` — tout nouveau projet doit y être ajouté (`dotnet sln add`).
- **Thread-safety** : `BoothTelemetry` et `InMemoryLogSink` sont écrits depuis le thread conscommateur du workflow et des threads Serilog arbitraires → verrouillage interne obligatoire.
- **Style** : suivre les conventions du repo (commentaires bilingues FR/EN tolérés comme l'existant, `sealed` par défaut, Conventional Commits).
- **Commits** : terminer chaque message par `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`.
- **Pas de régression** : `dotnet test` (suite existante `WorkflowTests`/`ThemeOptionsTests`) doit rester vert après chaque tâche.

---

## File Structure

- `src/Photobooth.Core/Diagnostics/BoothTelemetry.cs` — **créé** : singleton d'état diagnostic + record `PrintResult`.
- `src/Photobooth.Core/Workflow/PhotoboothWorkflow.cs` — **modifié** : ajout du paramètre `BoothTelemetry` au constructeur ; capture du résultat d'impression dans `PrintLastPhotoAsync`.
- `src/Photobooth.App/Composition/ServiceConfiguration.cs` — **modifié** : enregistrement DI de `BoothTelemetry`.
- `src/Photobooth.Admin/Photobooth.Admin.csproj` — **créé** : nouveau projet (référence Core + Serilog).
- `src/Photobooth.Admin/InMemoryLogSink.cs` — **créé** : sink Serilog ring buffer + record `LogLine`.
- `src/Photobooth.App/Photobooth.App.csproj` — **modifié** : `ProjectReference` vers Photobooth.Admin.
- `src/Photobooth.App/Program.cs` — **modifié** : création du sink, branchement Serilog, enregistrement DI.
- `src/Photobooth.Tests/Photobooth.Tests.csproj` — **modifié** : `ProjectReference` vers Photobooth.Admin + `PackageReference` Serilog.
- `src/Photobooth.Tests/TestSupport.cs` — **modifié** : `Rig` expose `Telemetry` ; `RecordingPrinter` gagne `ThrowOnPrint`.
- `src/Photobooth.Tests/BoothTelemetryTests.cs` — **créé** : tests unitaires de `BoothTelemetry`.
- `src/Photobooth.Tests/PrintTelemetryTests.cs` — **créé** : test d'intégration capture d'échec via le workflow.
- `src/Photobooth.Tests/InMemoryLogSinkTests.cs` — **créé** : tests unitaires du sink.

---

## Task 1: BoothTelemetry (modèle d'état diagnostic)

**Files:**
- Create: `src/Photobooth.Core/Diagnostics/BoothTelemetry.cs`
- Test: `src/Photobooth.Tests/BoothTelemetryTests.cs`

**Interfaces:**
- Consumes: rien.
- Produces:
  - `Photobooth.Core.Diagnostics.BoothTelemetry` (singleton) avec :
    - `PrintResult? LastPrint { get; }`
    - `void RecordPrintFailure(string reason)`
    - `void RecordPrintSuccess()`
  - `Photobooth.Core.Diagnostics.PrintResult(bool Succeeded, string? Reason, DateTimeOffset At)` (record).

- [x] **Step 1 : Écrire les tests qui échouent**

Créer `src/Photobooth.Tests/BoothTelemetryTests.cs` :

```csharp
using Photobooth.Core.Diagnostics;
using Xunit;

namespace Photobooth.Tests;

public class BoothTelemetryTests
{
    [Fact]
    public void LastPrint_is_null_before_any_attempt()
    {
        var telemetry = new BoothTelemetry();
        Assert.Null(telemetry.LastPrint);
    }

    [Fact]
    public void RecordPrintFailure_captures_the_real_reason()
    {
        var telemetry = new BoothTelemetry();
        telemetry.RecordPrintFailure("lp failed with exit code 1: unknown destination");

        Assert.NotNull(telemetry.LastPrint);
        Assert.False(telemetry.LastPrint!.Succeeded);
        Assert.Equal("lp failed with exit code 1: unknown destination", telemetry.LastPrint.Reason);
    }

    [Fact]
    public void RecordPrintSuccess_overwrites_a_previous_failure_and_clears_reason()
    {
        var telemetry = new BoothTelemetry();
        telemetry.RecordPrintFailure("boom");
        telemetry.RecordPrintSuccess();

        Assert.NotNull(telemetry.LastPrint);
        Assert.True(telemetry.LastPrint!.Succeeded);
        Assert.Null(telemetry.LastPrint.Reason);
    }
}
```

- [x] **Step 2 : Lancer le test pour vérifier qu'il échoue**

Run: `dotnet test --filter "FullyQualifiedName~BoothTelemetryTests"`
Expected: ÉCHEC de compilation — `The type or namespace name 'Diagnostics' does not exist` / `BoothTelemetry` introuvable.

- [x] **Step 3 : Écrire l'implémentation minimale**

Créer `src/Photobooth.Core/Diagnostics/BoothTelemetry.cs` :

```csharp
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
```

- [x] **Step 4 : Lancer le test pour vérifier qu'il passe**

Run: `dotnet test --filter "FullyQualifiedName~BoothTelemetryTests"`
Expected: PASS (3 tests).

- [x] **Step 5 : Commit**

```bash
git add src/Photobooth.Core/Diagnostics/BoothTelemetry.cs src/Photobooth.Tests/BoothTelemetryTests.cs
git commit -m "$(printf 'feat(telemetry): BoothTelemetry capture le résultat de la dernière impression\n\nCo-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>')"
```

---

## Task 2: Capturer la vraie raison d'échec dans le workflow

**Files:**
- Modify: `src/Photobooth.Core/Workflow/PhotoboothWorkflow.cs` (constructeur + `PrintLastPhotoAsync`)
- Modify: `src/Photobooth.App/Composition/ServiceConfiguration.cs` (enregistrement DI)
- Modify: `src/Photobooth.Tests/TestSupport.cs` (`Rig`, `TestHarness.Build`, `RecordingPrinter`)
- Test: `src/Photobooth.Tests/PrintTelemetryTests.cs`

**Interfaces:**
- Consumes: `BoothTelemetry` (Task 1).
- Produces:
  - Constructeur `PhotoboothWorkflow(IGoProClient, ILightOutput, IPhotoDisplay, IPrinterAdapter, BoothTelemetry, IOptions<TimingOptions>, IOptions<GoProOptions>, IOptions<PrinterOptions>, ILogger<PhotoboothWorkflow>)` — le **5ᵉ** paramètre (`BoothTelemetry`) est nouveau, inséré juste après `printer`.
  - `TestHarness.Rig` gagne un membre `BoothTelemetry Telemetry`.
  - `RecordingPrinter` gagne `bool ThrowOnPrint { get; set; }`.

- [x] **Step 1 : Écrire le test d'intégration qui échoue**

Créer `src/Photobooth.Tests/PrintTelemetryTests.cs` :

```csharp
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Photobooth.Adapters.GoPro;
using Photobooth.Core.Workflow;
using Xunit;

namespace Photobooth.Tests;

public class PrintTelemetryTests
{
    private static FakeGoProClient NewFake() =>
        new(new[] { new byte[] { 1, 2, 3 } }, NullLogger<FakeGoProClient>.Instance);

    [Fact]
    public async Task Print_failure_reason_is_captured_in_telemetry_instead_of_being_swallowed()
    {
        var rig = TestHarness.Build(NewFake(), printerEnabled: true);
        rig.Printer.ThrowOnPrint = true;
        await rig.Workflow.StartAsync();
        try
        {
            // Capturer une photo pour qu'il y ait quelque chose à imprimer.
            rig.Workflow.Submit(new BoothCommand.PhotoRequested());
            Assert.True(await TestHarness.WaitForAsync(
                () => rig.Display.PhotoCount >= 1 && rig.Workflow.State == BoothState.Idle));

            // Demander l'impression : l'imprimante lève, le workflow doit enregistrer la vraie raison.
            rig.Workflow.Submit(new BoothCommand.PrintRequested());
            Assert.True(await TestHarness.WaitForAsync(() => rig.Telemetry.LastPrint is not null));

            Assert.False(rig.Telemetry.LastPrint!.Succeeded);
            Assert.Contains("unknown destination", rig.Telemetry.LastPrint.Reason);
        }
        finally { await rig.Workflow.DisposeAsync(); }
    }
}
```

- [x] **Step 2 : Lancer le test pour vérifier qu'il échoue**

Run: `dotnet test --filter "FullyQualifiedName~PrintTelemetryTests"`
Expected: ÉCHEC de compilation — `'TestHarness.Rig' does not contain a definition for 'Telemetry'` et `'RecordingPrinter' does not contain a definition for 'ThrowOnPrint'`.

- [x] **Step 3a : Étendre `RecordingPrinter` et `Rig` dans TestSupport.cs**

Dans `src/Photobooth.Tests/TestSupport.cs`, ajouter l'import en tête (après les autres `using`) :

```csharp
using Photobooth.Core.Diagnostics;
```

Remplacer la classe `RecordingPrinter` par :

```csharp
internal sealed class RecordingPrinter : IPrinterAdapter
{
    public bool IsEnabled { get; set; }
    public bool ThrowOnPrint { get; set; }
    public int PrintCount { get; private set; }
    public byte[]? LastPrinted { get; private set; }

    public Task PrintAsync(byte[] imageData, CancellationToken ct = default)
    {
        if (ThrowOnPrint)
            throw new System.InvalidOperationException("lp failed with exit code 1: unknown destination");
        PrintCount++;
        LastPrinted = imageData.ToArray();
        return Task.CompletedTask;
    }
}
```

Remplacer la déclaration du record `Rig` par :

```csharp
    public sealed record Rig(PhotoboothWorkflow Workflow, RecordingDisplay Display, FakeLightOutput Light, IGoProClient GoPro, RecordingPrinter Printer, BoothTelemetry Telemetry);
```

Dans `TestHarness.Build`, remplacer le bloc de construction du workflow (création `display`/`light`/`printer` puis `new PhotoboothWorkflow(...)` puis `return`) par :

```csharp
        var display = new RecordingDisplay();
        var light = new FakeLightOutput(NullLogger<FakeLightOutput>.Instance);
        var printer = new RecordingPrinter { IsEnabled = printerEnabled };
        var telemetry = new BoothTelemetry();
        var wf = new PhotoboothWorkflow(
            gopro, light, display, printer, telemetry,
            Options.Create(timings), Options.Create(gopt), Options.Create(popt),
            NullLogger<PhotoboothWorkflow>.Instance);
        return new Rig(wf, display, light, gopro, printer, telemetry);
```

- [x] **Step 3b : Ajouter le paramètre `BoothTelemetry` au workflow**

Dans `src/Photobooth.Core/Workflow/PhotoboothWorkflow.cs`, ajouter l'import (avec les autres `using Photobooth.Core.*`) :

```csharp
using Photobooth.Core.Diagnostics;
```

Ajouter le champ, juste après `private readonly IPrinterAdapter _printer;` :

```csharp
    private readonly BoothTelemetry _telemetry;
```

Remplacer la signature du constructeur et l'affectation. La signature devient :

```csharp
    public PhotoboothWorkflow(
        IGoProClient gopro,
        ILightOutput light,
        IPhotoDisplay display,
        IPrinterAdapter printer,
        BoothTelemetry telemetry,
        IOptions<TimingOptions> timings,
        IOptions<GoProOptions> goproOptions,
        IOptions<PrinterOptions> printerOptions,
        ILogger<PhotoboothWorkflow> log)
```

Et dans le corps du constructeur, ajouter juste après `_printer = printer;` :

```csharp
        _telemetry = telemetry;
```

- [x] **Step 3c : Enregistrer le résultat d'impression dans `PrintLastPhotoAsync`**

Toujours dans `PhotoboothWorkflow.cs`, remplacer le bloc `try/catch` de `PrintLastPhotoAsync` par :

```csharp
        try
        {
            await _printer.PrintAsync(photo, watchdog.Token);
            _telemetry.RecordPrintSuccess();
            _display.SetStatus("Impression lancee", BoothStatusLevel.Ready);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Printing failed.");
            _telemetry.RecordPrintFailure(ex.Message);
            _display.SetStatus("Impression impossible", BoothStatusLevel.Error);
        }
```

> Note : un timeout du watchdog (`OperationCanceledException` non issue de `lifetime`) tombe dans le `catch (Exception)` générique — sa raison est donc aussi capturée (« The operation was canceled. »), ce qui couvre le cas « impression trop lente ».

- [x] **Step 3d : Enregistrer `BoothTelemetry` en DI**

Dans `src/Photobooth.App/Composition/ServiceConfiguration.cs`, ajouter l'import (avec les autres `using Photobooth.Core.*`) :

```csharp
using Photobooth.Core.Diagnostics;
```

Ajouter l'enregistrement juste avant `services.AddSingleton<PhotoboothWorkflow>();` :

```csharp
        services.AddSingleton<BoothTelemetry>();
```

- [x] **Step 4 : Lancer les tests (ciblé puis complet)**

Run: `dotnet test --filter "FullyQualifiedName~PrintTelemetryTests"`
Expected: PASS (1 test).

Run: `dotnet test`
Expected: PASS — toute la suite existante reste verte (le changement de constructeur est absorbé par `TestHarness.Build` et la DI).

- [x] **Step 5 : Commit**

```bash
git add src/Photobooth.Core/Workflow/PhotoboothWorkflow.cs src/Photobooth.App/Composition/ServiceConfiguration.cs src/Photobooth.Tests/TestSupport.cs src/Photobooth.Tests/PrintTelemetryTests.cs
git commit -m "$(printf 'fix(printer): capturer la vraie raison d_échec dans BoothTelemetry\n\nLe workflow loggait Printing failed puis affichait un Impression impossible\ngénérique sans conserver la cause. La cause (code lp + stderr, ou timeout\nwatchdog) est désormais enregistrée dans BoothTelemetry pour la future UI.\n\nCo-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>')"
```

---

## Task 3: InMemoryLogSink (nouveau projet Photobooth.Admin)

**Files:**
- Create: `src/Photobooth.Admin/Photobooth.Admin.csproj`
- Create: `src/Photobooth.Admin/InMemoryLogSink.cs`
- Modify: `src/Photobooth.App/Photobooth.App.csproj` (ProjectReference)
- Modify: `src/Photobooth.App/Program.cs` (création sink + Serilog + DI)
- Modify: `src/Photobooth.Tests/Photobooth.Tests.csproj` (ProjectReference + Serilog)
- Test: `src/Photobooth.Tests/InMemoryLogSinkTests.cs`

**Interfaces:**
- Consumes: rien.
- Produces:
  - `Photobooth.Admin.InMemoryLogSink : Serilog.Core.ILogEventSink` avec :
    - `const int Capacity = 500`
    - `void Emit(LogEvent logEvent)`
    - `IReadOnlyList<LogLine> Snapshot()` (du plus ancien au plus récent)
  - `Photobooth.Admin.LogLine(DateTimeOffset Timestamp, string Level, string Message, string? Exception)` (record).

- [x] **Step 1 : Créer le projet Photobooth.Admin et l'ajouter à la solution**

Créer `src/Photobooth.Admin/Photobooth.Admin.csproj` :

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <ProjectReference Include="..\Photobooth.Core\Photobooth.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Serilog" Version="4.2.0" />
  </ItemGroup>

</Project>
```

Puis :

```bash
dotnet sln Photobooth.sln add src/Photobooth.Admin/Photobooth.Admin.csproj
```

- [x] **Step 2 : Écrire les tests qui échouent**

D'abord rendre `Photobooth.Admin` + Serilog visibles depuis les tests. Dans `src/Photobooth.Tests/Photobooth.Tests.csproj` :

- Ajouter dans le `ItemGroup` des `PackageReference` :
```xml
    <PackageReference Include="Serilog" Version="4.2.0" />
```
- Ajouter dans le `ItemGroup` des `ProjectReference` :
```xml
    <ProjectReference Include="..\Photobooth.Admin\Photobooth.Admin.csproj" />
```

Créer `src/Photobooth.Tests/InMemoryLogSinkTests.cs` :

```csharp
using Photobooth.Admin;
using Serilog;
using Xunit;

namespace Photobooth.Tests;

public class InMemoryLogSinkTests
{
    [Fact]
    public void Emitted_events_are_buffered_with_level_and_message()
    {
        var sink = new InMemoryLogSink();
        using var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();

        logger.Information("hello {Name}", "world");

        var snap = sink.Snapshot();
        Assert.Single(snap);
        Assert.Equal("Information", snap[0].Level);
        Assert.Contains("world", snap[0].Message);
        Assert.Null(snap[0].Exception);
    }

    [Fact]
    public void Exception_is_captured_as_text()
    {
        var sink = new InMemoryLogSink();
        using var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();

        logger.Error(new System.InvalidOperationException("kaboom"), "print broke");

        var snap = sink.Snapshot();
        Assert.Single(snap);
        Assert.NotNull(snap[0].Exception);
        Assert.Contains("kaboom", snap[0].Exception!);
    }

    [Fact]
    public void Buffer_keeps_only_the_most_recent_entries()
    {
        var sink = new InMemoryLogSink();
        using var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();

        for (var i = 0; i < InMemoryLogSink.Capacity + 50; i++)
            logger.Information("line {N}", i);

        var snap = sink.Snapshot();
        Assert.Equal(InMemoryLogSink.Capacity, snap.Count);
        Assert.Contains((InMemoryLogSink.Capacity + 49).ToString(), snap[^1].Message); // le plus récent est conservé
    }
}
```

- [x] **Step 3 : Lancer les tests pour vérifier qu'ils échouent**

Run: `dotnet test --filter "FullyQualifiedName~InMemoryLogSinkTests"`
Expected: ÉCHEC de compilation — `InMemoryLogSink` introuvable.

- [x] **Step 4 : Écrire l'implémentation du sink**

Créer `src/Photobooth.Admin/InMemoryLogSink.cs` :

```csharp
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
```

- [x] **Step 5 : Lancer les tests pour vérifier qu'ils passent**

Run: `dotnet test --filter "FullyQualifiedName~InMemoryLogSinkTests"`
Expected: PASS (3 tests).

- [x] **Step 6 : Brancher le sink sur Serilog et la DI dans l'app**

Dans `src/Photobooth.App/Photobooth.App.csproj`, ajouter dans le `ItemGroup` des `ProjectReference` :

```xml
    <ProjectReference Include="..\Photobooth.Admin\Photobooth.Admin.csproj" />
```

Dans `src/Photobooth.App/Program.cs` :

Ajouter l'import (avec les autres `using`) :

```csharp
using Photobooth.Admin;
```

Remplacer la signature de `ConfigureSerilog` par (ajout du paramètre sink) :

```csharp
    private static void ConfigureSerilog(IConfiguration config, string baseDir, InMemoryLogSink logBuffer)
```

Dans le corps de `ConfigureSerilog`, insérer `.WriteTo.Sink(logBuffer)` dans la chaîne, juste après `.WriteTo.Console()` :

```csharp
            .WriteTo.Console()
            .WriteTo.Sink(logBuffer)
```

Dans `Main`, remplacer la ligne `ConfigureSerilog(config, baseDir);` par :

```csharp
        var logBuffer = new InMemoryLogSink();
        ConfigureSerilog(config, baseDir, logBuffer);
```

Toujours dans `Main`, juste après `sc.AddPhotobooth(config);`, ajouter :

```csharp
            sc.AddSingleton(logBuffer);
```

- [x] **Step 7 : Vérifier la compilation et la non-régression**

Run: `dotnet build Photobooth.sln`
Expected: Build succeeded, 0 erreurs.

Run: `dotnet test`
Expected: PASS — toute la suite (existante + nouvelle) verte.

- [x] **Step 8 : Commit**

```bash
git add src/Photobooth.Admin/ src/Photobooth.App/Photobooth.App.csproj src/Photobooth.App/Program.cs src/Photobooth.Tests/Photobooth.Tests.csproj src/Photobooth.Tests/InMemoryLogSinkTests.cs Photobooth.sln
git commit -m "$(printf 'feat(admin): InMemoryLogSink (ring buffer) branché sur Serilog\n\nNouveau projet Photobooth.Admin ; sink mémoire des 500 derniers logs,\nlu plus tard par lUI dadmin (journald étant volatil sous overlay).\n\nCo-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>')"
```

---

## Self-Review

**1. Couverture spec (périmètre de ce plan)** :
- Spec §5 « `BoothTelemetry` … dernière raison d'échec impression » → Tasks 1 & 2. ✅
- Spec §5 « `InMemoryLogSink` (Serilog) … ring buffer » → Task 3. ✅
- Spec §5 intégration « capturer l'exception aujourd'hui avalée en `PhotoboothWorkflow.cs:348` » → Task 2, Step 3c. ✅
- Spec §5 « nouveau projet `Photobooth.Admin` … référencé par `Photobooth.App` » → Task 3 (projet créé ; ASP.NET sera ajouté au Plan 2/3). ✅
- Hors périmètre de CE plan (couverts par Plans 2/3) : `AdminOptions`, `AdminWebHost`, endpoints, UI, console, écriture config, mode AP, mDNS, overlay boot. ✅ (Décomposition annoncée.)

**2. Scan placeholders** : aucun « TBD / TODO / à compléter ». Chaque étape de code contient le code complet. ✅

**3. Cohérence des types** :
- `BoothTelemetry.RecordPrintFailure(string)` / `RecordPrintSuccess()` / `LastPrint` : noms identiques entre Task 1 (définition), Task 2 (appel workflow + test) et le test Task 1. ✅
- Constructeur `PhotoboothWorkflow` : 5ᵉ param `BoothTelemetry telemetry` — l'ordre utilisé dans `TestHarness.Build` (Step 3a) correspond exactement à la signature (Step 3b). ✅
- `RecordingPrinter.ThrowOnPrint` et `Rig.Telemetry` définis (Step 3a) avant usage dans le test (Step 1). ✅
- `InMemoryLogSink.Capacity` / `Snapshot()` / `LogLine.Message`/`.Exception`/`.Level` : noms identiques entre l'impl (Step 4) et les tests (Step 2). ✅

**4. Ambiguïté** : `RenderMessage()` peut entourer les chaînes de guillemets selon Serilog → les tests assertent `Contains("world")` et non l'égalité stricte, pour rester robustes au formatage. ✅
