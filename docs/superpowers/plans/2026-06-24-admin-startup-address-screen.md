# Écran de démarrage « adresse d'admin » — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Au démarrage, si l'admin web est activé, afficher l'URL d'admin (`http://<ip>:<port>`) en plein écran ; le premier appui sur le bouton photo ferme cet écran sans capturer, puis la borne fonctionne normalement.

**Architecture:** Approche A (gate côté App). L'overlay est un affichage pur posé une fois au boot (modèle du bandeau `Diagnostic`) ; l'interception « premier appui = fermeture » vit dans `App.axaml.cs`. `Photobooth.Core`/`PhotoboothWorkflow` restent inchangés. La résolution d'IP et la décision de fermeture sont extraites dans des unités pures et testables (`Photobooth.Admin.AdminAddress`, `Photobooth.App.StartupNoticeGate`).

**Tech Stack:** .NET 8, Avalonia (kiosk), xUnit. Spec : `docs/superpowers/specs/2026-06-23-admin-startup-address-screen-design.md`.

## Global Constraints

- Cible : `.NET 8`. Build : `dotnet build Photobooth.sln`. Tests : `dotnet test Photobooth.sln`.
- Le mode admin est **opt-in** : tout doit être no-op si `Admin.Enabled == false`.
- Le bloc admin/affichage ne doit **jamais** rendre la borne fatale : best-effort, exceptions avalées, dégradation seulement (cohérent avec l'existant).
- `Photobooth.Core` et `PhotoboothWorkflow` ne sont **pas** modifiés.
- Langue : commentaires/messages en français, sans accents dans les identifiants C#, accents OK dans les chaînes et le XAML (cohérent avec l'existant).

---

### Task 1: Option `Admin.ShowAddressOnStartup`

**Files:**
- Modify: `src/Photobooth.Core/Options/AdminOptions.cs` (après la propriété `Pin`, ~ligne 21)
- Modify: `src/Photobooth.App/appsettings.json:61-66` (section `Admin`)
- Test: `src/Photobooth.Tests/AdminOptionsTests.cs:13-21` (méthode `Defaults_are_disabled_and_valid`)

**Interfaces:**
- Produces: `Photobooth.Core.Options.AdminOptions.ShowAddressOnStartup` (`bool`, défaut `true`).

- [ ] **Step 1: Compléter le test des défauts**

Dans `src/Photobooth.Tests/AdminOptionsTests.cs`, ajouter une assertion dans `Defaults_are_disabled_and_valid`, juste après `Assert.Equal("", o.Pin);` :

```csharp
        Assert.True(o.ShowAddressOnStartup);
```

- [ ] **Step 2: Lancer le test, vérifier l'échec de compilation**

Run: `dotnet test Photobooth.sln --filter "FullyQualifiedName~AdminOptionsTests.Defaults_are_disabled_and_valid"`
Expected: échec de build — `'AdminOptions' does not contain a definition for 'ShowAddressOnStartup'`.

- [ ] **Step 3: Ajouter la propriété**

Dans `src/Photobooth.Core/Options/AdminOptions.cs`, après la propriété `Pin` (ligne 21) :

```csharp
    /// <summary>Affiche l'URL d'admin a l'ecran au demarrage (1er appui bouton photo = fermeture).
    /// N'a d'effet que si <see cref="Enabled"/>. Defaut true.</summary>
    public bool ShowAddressOnStartup { get; set; } = true;
```

- [ ] **Step 4: Refléter le défaut dans appsettings.json**

Dans `src/Photobooth.App/appsettings.json`, section `Admin` (lignes 61-66), ajouter la clé après `"Pin": ""` (ajouter la virgule au bout de la ligne `Pin`) :

```json
  "Admin": {
    "Enabled": false,
    "ListenAddress": "0.0.0.0",
    "Port": 8080,
    "Pin": "",
    "ShowAddressOnStartup": true
  },
```

- [ ] **Step 5: Lancer le test, vérifier le succès**

Run: `dotnet test Photobooth.sln --filter "FullyQualifiedName~AdminOptionsTests"`
Expected: PASS (toutes les méthodes `AdminOptionsTests`).

- [ ] **Step 6: Commit**

```bash
git add src/Photobooth.Core/Options/AdminOptions.cs src/Photobooth.App/appsettings.json src/Photobooth.Tests/AdminOptionsTests.cs
git commit -m "feat(admin): option ShowAddressOnStartup (defaut true)"
```

---

### Task 2: Résolution de l'URL d'admin (`AdminAddress`)

**Files:**
- Create: `src/Photobooth.Admin/AdminAddress.cs`
- Test: `src/Photobooth.Tests/AdminAddressTests.cs`

**Interfaces:**
- Produces:
  - `Photobooth.Admin.AdminAddress.BuildUrls(IEnumerable<System.Net.IPAddress> addresses, int port) -> IReadOnlyList<string>` — pur/testable.
  - `Photobooth.Admin.AdminAddress.LocalUrls(int port) -> IReadOnlyList<string>` — énumère les interfaces réelles ; best-effort (liste vide sur exception).

- [ ] **Step 1: Écrire les tests qui échouent**

Create `src/Photobooth.Tests/AdminAddressTests.cs` :

```csharp
using System.Net;
using Photobooth.Admin;
using Xunit;

namespace Photobooth.Tests;

public sealed class AdminAddressTests
{
    [Fact]
    public void BuildUrls_excludes_loopback_and_formats_with_port()
    {
        var urls = AdminAddress.BuildUrls(
            new[] { IPAddress.Parse("127.0.0.1"), IPAddress.Parse("10.5.5.100") }, 8080);
        Assert.Equal(new[] { "http://10.5.5.100:8080" }, urls);
    }

    [Fact]
    public void BuildUrls_excludes_ipv6()
    {
        var urls = AdminAddress.BuildUrls(
            new[] { IPAddress.Parse("fe80::1"), IPAddress.Parse("192.168.1.50") }, 8080);
        Assert.Equal(new[] { "http://192.168.1.50:8080" }, urls);
    }

    [Fact]
    public void BuildUrls_deduplicates()
    {
        var urls = AdminAddress.BuildUrls(
            new[] { IPAddress.Parse("10.5.5.100"), IPAddress.Parse("10.5.5.100") }, 8080);
        Assert.Single(urls);
    }

    [Fact]
    public void BuildUrls_returns_empty_when_no_usable_address()
    {
        var urls = AdminAddress.BuildUrls(new[] { IPAddress.Loopback }, 8080);
        Assert.Empty(urls);
    }
}
```

- [ ] **Step 2: Lancer les tests, vérifier l'échec de compilation**

Run: `dotnet test Photobooth.sln --filter "FullyQualifiedName~AdminAddressTests"`
Expected: échec de build — le type `AdminAddress` n'existe pas.

- [ ] **Step 3: Implémenter `AdminAddress`**

Create `src/Photobooth.Admin/AdminAddress.cs` :

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Photobooth.Admin;

/// <summary>
/// Resout les URLs d'admin a afficher a l'ecran au demarrage (http://ip:port).
/// </summary>
public static class AdminAddress
{
    /// <summary>
    /// Pur/testable : ne garde que les IPv4, exclut le loopback (127.0.0.0/8), formate en
    /// http://ip:port et deduplique. Pas de tri ni de priorisation.
    /// </summary>
    public static IReadOnlyList<string> BuildUrls(IEnumerable<IPAddress> addresses, int port)
    {
        return addresses
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork) // IPv4 uniquement
            .Where(a => !IPAddress.IsLoopback(a))
            .Select(a => a.ToString())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(ip => $"http://{ip}:{port}")
            .ToList();
    }

    /// <summary>
    /// Enumere les interfaces reelles (Up, hors loopback) puis delegue a <see cref="BuildUrls"/>.
    /// Best-effort : toute exception -> liste vide (le mode debug ne doit jamais casser la borne).
    /// </summary>
    public static IReadOnlyList<string> LocalUrls(int port)
    {
        try
        {
            var addresses = NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up)
                .Where(ni => ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(ni => ni.GetIPProperties().UnicastAddresses)
                .Select(u => u.Address);
            return BuildUrls(addresses, port);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
```

- [ ] **Step 4: Lancer les tests, vérifier le succès**

Run: `dotnet test Photobooth.sln --filter "FullyQualifiedName~AdminAddressTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Photobooth.Admin/AdminAddress.cs src/Photobooth.Tests/AdminAddressTests.cs
git commit -m "feat(admin): AdminAddress.BuildUrls/LocalUrls (URLs d_admin pour l_ecran de boot)"
```

---

### Task 3: Porte d'entrée du premier appui (`StartupNoticeGate`)

**Files:**
- Create: `src/Photobooth.App/StartupNoticeGate.cs`
- Test: `src/Photobooth.Tests/StartupNoticeGateTests.cs`

**Interfaces:**
- Produces: `Photobooth.App.StartupNoticeGate` avec `bool Pending { get; }`, `void Arm()`, `bool ConsumePress()` (true si l'appui a fermé l'écran → pas de capture).

- [ ] **Step 1: Écrire les tests qui échouent**

Create `src/Photobooth.Tests/StartupNoticeGateTests.cs` :

```csharp
using Photobooth.App;
using Xunit;

namespace Photobooth.Tests;

public sealed class StartupNoticeGateTests
{
    [Fact]
    public void Not_pending_by_default()
    {
        Assert.False(new StartupNoticeGate().Pending);
    }

    [Fact]
    public void Arm_then_first_press_is_consumed_once()
    {
        var gate = new StartupNoticeGate();
        gate.Arm();
        Assert.True(gate.Pending);
        Assert.True(gate.ConsumePress());   // 1er appui ferme l'ecran
        Assert.False(gate.Pending);
        Assert.False(gate.ConsumePress());  // appuis suivants = capture normale
    }

    [Fact]
    public void Consume_without_arm_returns_false()
    {
        Assert.False(new StartupNoticeGate().ConsumePress());
    }
}
```

- [ ] **Step 2: Lancer les tests, vérifier l'échec de compilation**

Run: `dotnet test Photobooth.sln --filter "FullyQualifiedName~StartupNoticeGateTests"`
Expected: échec de build — le type `StartupNoticeGate` n'existe pas.

- [ ] **Step 3: Implémenter `StartupNoticeGate`**

Create `src/Photobooth.App/StartupNoticeGate.cs` :

```csharp
namespace Photobooth.App;

/// <summary>
/// Porte d'entree de l'ecran d'accueil admin affiche au boot. Tant qu'elle est armee, le 1er appui
/// "photo" est consomme pour fermer l'ecran (aucune capture) ; les appuis suivants passent normalement.
/// Logique extraite du code-behind UI pour etre testable.
/// </summary>
public sealed class StartupNoticeGate
{
    private bool _pending;

    /// <summary>Vrai tant que l'ecran d'accueil est affiche et n'a pas encore ete ferme.</summary>
    public bool Pending => _pending;

    /// <summary>Arme la porte (ecran affiche).</summary>
    public void Arm() => _pending = true;

    /// <summary>
    /// Consomme un appui photo. Retourne true si l'appui a ferme l'ecran (donc PAS de capture),
    /// false si l'ecran etait deja ferme (capture normale).
    /// </summary>
    public bool ConsumePress()
    {
        var was = _pending;
        _pending = false;
        return was;
    }
}
```

- [ ] **Step 4: Lancer les tests, vérifier le succès**

Run: `dotnet test Photobooth.sln --filter "FullyQualifiedName~StartupNoticeGateTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Photobooth.App/StartupNoticeGate.cs src/Photobooth.Tests/StartupNoticeGateTests.cs
git commit -m "feat(admin): StartupNoticeGate (1er appui photo ferme l_ecran d_accueil)"
```

---

### Task 4: Affichage de l'overlay (MainViewModel + MainView.axaml)

**Files:**
- Modify: `src/Photobooth.App/ViewModels/MainViewModel.cs` (après les membres `Diagnostic`/`HasDiagnostic`, ~lignes 158-195)
- Modify: `src/Photobooth.App/Views/MainView.axaml` (après le bandeau `Diagnostic`, ~lignes 345-352)

**Interfaces:**
- Consumes: rien (affichage pur).
- Produces:
  - `MainViewModel.AdminAddress` (`string?`), `MainViewModel.HasAdminAddress` (`bool`).
  - `MainViewModel.ShowAdminAddress(string? text)`, `MainViewModel.ClearAdminAddress()` (posent sur le thread UI).

> Pas de test unitaire : `MainViewModel` instancie des ressources Avalonia (AssetLoader, Dispatcher) et le projet n'a pas de harnais Avalonia headless. Vérification = build + run manuel en Task 5.

- [ ] **Step 1: Ajouter les membres au MainViewModel**

Dans `src/Photobooth.App/ViewModels/MainViewModel.cs`, juste après la méthode `ShowDiagnostic` (ligne 195, qui se termine par `Dispatcher.UIThread.Post(() => Diagnostic = message);`), ajouter :

```csharp

    private string? _adminAddress;

    /// <summary>
    /// Ecran d'accueil admin (URL a ouvrir), pose une fois au boot par l'App si Admin.Enabled &&
    /// ShowAddressOnStartup. Comme <see cref="Diagnostic"/>, le workflow n'y touche jamais ;
    /// ferme au 1er appui photo (gate cote App).
    /// </summary>
    public string? AdminAddress
    {
        get => _adminAddress;
        private set
        {
            if (SetField(ref _adminAddress, value))
                Raise(nameof(HasAdminAddress));
        }
    }

    public bool HasAdminAddress => !string.IsNullOrEmpty(_adminAddress);

    /// <summary>Affiche l'ecran d'accueil admin. Appele par l'App sur le thread UI.</summary>
    public void ShowAdminAddress(string? text) =>
        Dispatcher.UIThread.Post(() => AdminAddress = text);

    /// <summary>Ferme l'ecran d'accueil admin (1er appui photo).</summary>
    public void ClearAdminAddress() =>
        Dispatcher.UIThread.Post(() => AdminAddress = null);
```

- [ ] **Step 2: Ajouter l'overlay dans MainView.axaml**

Dans `src/Photobooth.App/Views/MainView.axaml`, juste après le bloc `<Border ... Binding Diagnostic ...>` qui se termine ligne 352 (`</Border>`) et avant la fermeture `</Panel>` (ligne 354), insérer :

```xml
        <!-- Ecran d'accueil admin : affiche au boot si Admin.Enabled && ShowAddressOnStartup (D5).
             Opaque, au-dessus de tout sauf le bandeau Diagnostic (300). Ferme au 1er appui bouton
             photo (gere dans App.axaml.cs) ; ne depend pas du pointeur. -->
        <Panel ZIndex="270" IsVisible="{Binding HasAdminAddress}" IsHitTestVisible="False">
          <Rectangle Fill="#F2000000" />
          <StackPanel HorizontalAlignment="Center" VerticalAlignment="Center" Spacing="24" MaxWidth="1100">
            <TextBlock Text="Administration" FontSize="48" FontWeight="Bold" Foreground="White"
                       TextAlignment="Center" HorizontalAlignment="Center" />
            <TextBlock Text="Connectez-vous au Wi-Fi de la borne, puis ouvrez cette adresse :"
                       FontSize="30" Foreground="#D0D0D0" TextAlignment="Center"
                       TextWrapping="Wrap" HorizontalAlignment="Center" />
            <TextBlock Text="{Binding AdminAddress}" FontSize="56" FontWeight="Bold"
                       Foreground="#7FFF7F" TextAlignment="Center"
                       TextWrapping="Wrap" HorizontalAlignment="Center" />
            <TextBlock Text="Appuyez sur le bouton photo pour démarrer"
                       FontSize="28" Foreground="#9A9A9A" TextAlignment="Center"
                       HorizontalAlignment="Center" Margin="0,16,0,0" />
          </StackPanel>
        </Panel>
```

- [ ] **Step 3: Vérifier le build**

Run: `dotnet build Photobooth.sln`
Expected: build réussi, 0 erreur (l'AXAML compile, le binding `AdminAddress`/`HasAdminAddress` résout sur `MainViewModel`).

- [ ] **Step 4: Commit**

```bash
git add src/Photobooth.App/ViewModels/MainViewModel.cs src/Photobooth.App/Views/MainView.axaml
git commit -m "feat(admin): overlay ecran d_accueil affichant l_URL d_admin (VM + vue)"
```

---

### Task 5: Câblage au boot et gate des appuis (App.axaml.cs)

**Files:**
- Modify: `src/Photobooth.App/App.axaml.cs` (champ ~ligne 23 ; câblage clavier lignes 67 & 75 ; câblage boutons lignes 84-87 ; bloc admin lignes 102-114 ; méthode `OnKey` lignes 131-146)

**Interfaces:**
- Consumes:
  - `Photobooth.Core.Options.AdminOptions.{Enabled, Port, ShowAddressOnStartup}` (Task 1).
  - `Photobooth.Admin.AdminAddress.LocalUrls(int)` (Task 2).
  - `Photobooth.App.StartupNoticeGate.{Pending, Arm, ConsumePress}` (Task 3).
  - `MainViewModel.{ShowAdminAddress, ClearAdminAddress}` (Task 4).

> Code-behind UI non testé unitairement (cohérent avec l'existant). Le comportement de fermeture est couvert par les tests `StartupNoticeGate` (Task 3) ; vérification d'intégration = build + suite complète + run manuel.

- [ ] **Step 1: Ajouter le champ `_noticeGate`**

Dans `src/Photobooth.App/App.axaml.cs`, remplacer la ligne 23 :

```csharp
    private PhotoboothWorkflow? _workflow;
```

par :

```csharp
    private PhotoboothWorkflow? _workflow;
    private readonly StartupNoticeGate _noticeGate = new();
```

- [ ] **Step 2: Afficher l'overlay et armer la porte AVANT le démarrage des boutons**

Dans `src/Photobooth.App/App.axaml.cs`, après la ligne 55 (`_workflow = workflow;`), insérer :

```csharp

        // Decision admin lue une fois : sert a l'ecran d'accueil (ci-dessous) et au demarrage de l'hote.
        var adminOpt = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<Photobooth.Core.Options.AdminOptions>>().Value;

        // 1re action si l'admin est active : afficher l'URL a ouvrir (D5). Arme AVANT le demarrage des
        // boutons pour qu'aucun appui ne soit capture avant la fermeture de l'ecran.
        if (adminOpt.Enabled && adminOpt.ShowAddressOnStartup)
        {
            var urls = Photobooth.Admin.AdminAddress.LocalUrls(adminOpt.Port);
            vm.ShowAdminAddress(urls.Count > 0
                ? string.Join("\n", urls)
                : "Adresse introuvable — vérifiez le réseau.");
            _noticeGate.Arm();
        }
```

- [ ] **Step 3: Router le clavier via une méthode d'instance**

Dans `src/Photobooth.App/App.axaml.cs`, remplacer la ligne 67 :

```csharp
                window.KeyDown += (_, e) => OnKey(e, workflow);
```

par :

```csharp
                window.KeyDown += (_, e) => OnKey(e, vm, workflow);
```

Puis remplacer la ligne 75 :

```csharp
            view.KeyDown += (_, e) => OnKey(e, workflow);
```

par :

```csharp
            view.KeyDown += (_, e) => OnKey(e, vm, workflow);
```

- [ ] **Step 4: Gater le câblage des boutons GPIO**

Dans `src/Photobooth.App/App.axaml.cs`, remplacer les lignes 84-87 :

```csharp
        // Route hardware buttons (and keyboard) to the workflow's command channel.
        buttons.PhotoPressed += () => workflow.Submit(new BoothCommand.PhotoRequested());
        buttons.VideoPressed += () => workflow.Submit(new BoothCommand.VideoToggleRequested());
        buttons.PrintPressed += () => workflow.Submit(new BoothCommand.PrintRequested());
```

par :

```csharp
        // Route hardware buttons (and keyboard) to the workflow's command channel.
        // Tant que l'ecran d'accueil admin est affiche (_noticeGate), le 1er appui photo le ferme
        // sans capturer, et les appuis video/impression sont ignores.
        buttons.PhotoPressed += () => SubmitPhoto(vm, workflow);
        buttons.VideoPressed += () => { if (!_noticeGate.Pending) workflow.Submit(new BoothCommand.VideoToggleRequested()); };
        buttons.PrintPressed += () => { if (!_noticeGate.Pending) workflow.Submit(new BoothCommand.PrintRequested()); };
```

- [ ] **Step 5: Réutiliser `adminOpt` pour le démarrage de l'hôte**

Dans `src/Photobooth.App/App.axaml.cs`, remplacer le bloc lignes 105-114 :

```csharp
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

par :

```csharp
        try
        {
            if (adminOpt.Enabled)
                _ = sp.GetRequiredService<AdminWebHost>().StartAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Démarrage de l'hôte admin ignoré (mode dégradé).");
        }
```

- [ ] **Step 6: Rendre `OnKey` d'instance et ajouter `SubmitPhoto`**

Dans `src/Photobooth.App/App.axaml.cs`, remplacer la méthode `OnKey` (lignes 131-146) :

```csharp
    private static void OnKey(KeyEventArgs e, PhotoboothWorkflow workflow)
    {
        switch (e.Key)
        {
            case Key.Space:
            case Key.Enter:
                workflow.Submit(new BoothCommand.PhotoRequested());
                break;
            case Key.V:
                workflow.Submit(new BoothCommand.VideoToggleRequested());
                break;
            case Key.P:
                workflow.Submit(new BoothCommand.PrintRequested());
                break;
        }
    }
```

par :

```csharp
    private void OnKey(KeyEventArgs e, MainViewModel vm, PhotoboothWorkflow workflow)
    {
        switch (e.Key)
        {
            case Key.Space:
            case Key.Enter:
                SubmitPhoto(vm, workflow);
                break;
            case Key.V:
                if (!_noticeGate.Pending) workflow.Submit(new BoothCommand.VideoToggleRequested());
                break;
            case Key.P:
                if (!_noticeGate.Pending) workflow.Submit(new BoothCommand.PrintRequested());
                break;
        }
    }

    // 1er appui pendant l'ecran d'accueil admin = fermeture (pas de capture) ; sinon capture normale.
    private void SubmitPhoto(MainViewModel vm, PhotoboothWorkflow workflow)
    {
        if (_noticeGate.ConsumePress())
        {
            vm.ClearAdminAddress();
            return;
        }
        workflow.Submit(new BoothCommand.PhotoRequested());
    }
```

- [ ] **Step 7: Vérifier le build et la suite complète**

Run: `dotnet build Photobooth.sln` puis `dotnet test Photobooth.sln`
Expected: build OK ; tous les tests verts (les tests existants + `AdminAddressTests`, `StartupNoticeGateTests`, `AdminOptionsTests`).

- [ ] **Step 8: Vérification manuelle (run desktop, GoPro simulée)**

Run:
```bash
PHOTOBOOTH_Admin__Enabled=true PHOTOBOOTH_Gopro__Mode=fake dotnet run --project src/Photobooth.App
```
Expected :
1. Au lancement, l'overlay « Administration » s'affiche avec une URL `http://<ip>:8080`.
2. `Espace` (= bouton photo) ferme l'overlay **sans** lancer de séquence photo (pas de « Prenez la pose » / « 3·2·1 »).
3. Un second `Espace` lance bien une séquence photo normale.
4. Relancer sans `PHOTOBOOTH_Admin__Enabled=true` : aucun overlay n'apparaît.

- [ ] **Step 9: Commit**

```bash
git add src/Photobooth.App/App.axaml.cs
git commit -m "feat(admin): afficher l_URL d_admin au boot + gate du 1er appui photo (approche A)"
```

---

### Task 6: Documentation

**Files:**
- Modify: `README_NET8.md:86-91` (table des clés `Admin`)
- Modify: `GUIDE_OPERATEUR.md:170` (après l'avertissement PIN, avant le `---`)
- Modify: `docs/superpowers/specs/2026-06-23-admin-debug-web-interface-design.md` (note de statut sur D5)

**Interfaces:** aucune (docs).

- [ ] **Step 1: Ajouter la clé à la table README**

Dans `README_NET8.md`, dans la table des clés `Admin`, après la ligne `| `Pin` | ... |` (ligne 91), ajouter :

```markdown
| `ShowAddressOnStartup` | `true` | affiche l'URL d'admin à l'écran au démarrage (1er appui bouton photo = fermeture, sans photo) |
```

- [ ] **Step 2: Ajouter la note opérateur**

Dans `GUIDE_OPERATEUR.md`, juste après le blockquote `> ⚠️ **Toujours un PIN**...` (ligne 170) et avant la ligne `---`, ajouter un paragraphe :

```markdown
Une fois activée, **au démarrage la borne affiche à l'écran l'adresse web à ouvrir** (par ex. `http://10.5.5.x:8080`). Notez-la, puis **appuyez sur le bouton photo** : l'écran disparaît et la borne démarre normalement — ce premier appui ne prend pas de photo.
```

- [ ] **Step 3: Mettre à jour le statut de D5 dans la spec admin**

Dans `docs/superpowers/specs/2026-06-23-admin-debug-web-interface-design.md`, dans la ligne du tableau de décisions pour `D5`, remplacer son texte de choix :

```
mDNS (`photobooth.local`) **+** overlay boot affichant l'URL/IP, **dismiss au 1er appui bouton** puis caché en usage normal
```

par :

```
mDNS (`photobooth.local`, hors périmètre) **+** overlay boot affichant l'URL/IP, **dismiss au 1er appui bouton photo** puis caché en usage normal. **Overlay livré le 2026-06-24** (`Admin.ShowAddressOnStartup`, défaut true) — voir `docs/superpowers/plans/2026-06-24-admin-startup-address-screen.md`.
```

- [ ] **Step 4: Commit**

```bash
git add README_NET8.md GUIDE_OPERATEUR.md docs/superpowers/specs/2026-06-23-admin-debug-web-interface-design.md
git commit -m "docs(admin): documenter l_ecran de demarrage affichant l_URL d_admin (D5)"
```

---

## Self-Review

**1. Spec coverage**

| Spec (§) | Tâche |
|---|---|
| §4.1 `ShowAddressOnStartup` (défaut true) | Task 1 |
| §4.2 résolution d'IP (`BuildUrls`/`LocalUrls`, loopback exclu, pas de tri) | Task 2 |
| §4.5 `StartupNoticeGate` testable | Task 3 |
| §4.3 membres `MainViewModel` (`AdminAddress`/`HasAdminAddress`/`ShowAdminAddress`/`ClearAdminAddress`) | Task 4 |
| §4.4 overlay XAML (ZIndex 270, opaque, `IsHitTestVisible=False`) | Task 4 |
| §4.5 câblage App (affichage synchrone, gate photo + Espace/Entrée, vidéo/impression ignorés) | Task 5 |
| §6 dégradation (repli « adresse introuvable », best-effort, indépendant du bind) | Task 2 (`LocalUrls` try/catch) + Task 5 (repli) |
| §7 tests (option, BuildUrls, gate) | Tasks 1-3 |
| §8 docs (README, GUIDE, D5) | Task 6 |
| §2 comportement (1er appui ferme sans capture, suivants normaux, no-op si désactivé) | Task 5 (vérif. Step 8) |

Aucune lacune. §9 (hors périmètre : mDNS, mode AP, affichage du PIN, ré-affichage) n'engendre aucune tâche — conforme.

**2. Placeholder scan** : aucun TBD/TODO ; tout le code est fourni en entier.

**3. Type consistency** : `StartupNoticeGate.{Pending, Arm, ConsumePress}`, `AdminAddress.{BuildUrls, LocalUrls}`, `MainViewModel.{AdminAddress, HasAdminAddress, ShowAdminAddress, ClearAdminAddress}`, `AdminOptions.ShowAddressOnStartup`, `SubmitPhoto(vm, workflow)`, `OnKey(e, vm, workflow)` — noms et signatures identiques entre la définition (Tasks 1-4) et l'usage (Task 5).
