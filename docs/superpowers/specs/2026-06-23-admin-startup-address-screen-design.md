# Écran de démarrage « adresse d'admin » — Design

- **Date** : 2026-06-23
- **Branche cible** : `feat/printer`
- **Statut** : design validé (approche A). Implémente la décision **D5** (« Découverte d'IP : overlay boot affichant l'URL/IP, dismiss au 1er appui bouton puis caché en usage normal ») de `2026-06-23-admin-debug-web-interface-design.md`.

## 1. Contexte & problème

Quand l'admin web est activé (`Admin.Enabled`, opt-in, défaut `false`), Kestrel écoute sur `0.0.0.0:8080`. Pour l'atteindre, le `README_NET8.md` dit littéralement « puis `http://<ip>:8080` » — mais **rien n'affiche cette `<ip>`**. Sur le terrain, la borne est cliente WiFi de la GoPro (sous-réseau `10.5.5.x`, sans DNS pratique) : l'opérateur doit deviner l'IP ou ouvrir une session SSH, alors que **l'écran kiosk est la seule sortie du Pi**.

**Objectif** : à l'allumage, si l'admin est activé, la **première chose affichée** est l'URL d'admin (`http://<ip>:<port>`), pour pouvoir s'y connecter — notamment via le réseau WiFi de la GoPro. Le **premier appui sur le bouton photo ferme cet écran sans prendre de photo** ; ensuite la borne fonctionne normalement.

## 2. Comportement attendu (validé)

1. Boot avec `Admin.Enabled == true` **et** `Admin.ShowAddressOnStartup == true` (nouvelle option, défaut `true`) → un overlay plein écran affiche l'URL d'admin **immédiatement**, avant toute autre interaction.
2. Le **premier appui bouton photo** (ou touche `Espace`/`Entrée`) **ferme** l'overlay et **ne déclenche aucune capture**.
3. Tant que l'overlay est visible, les appuis **vidéo / impression** sont **ignorés**.
4. Après fermeture, comportement normal (les appuis suivants capturent/impriment/filment comme aujourd'hui).
5. Si `Admin.Enabled == false` ou `ShowAddressOnStartup == false` → aucun changement, l'overlay n'apparaît jamais.

## 3. Décision d'architecture — Approche A (gate côté App)

L'overlay est un **affichage pur**, posé une fois au boot, sur le modèle exact du bandeau `Diagnostic` (set-once, jamais touché par le workflow). L'interception « premier appui = fermeture » vit dans `src/Photobooth.App/App.axaml.cs`, là où les boutons GPIO et le clavier sont déjà câblés au workflow.

**`Photobooth.Core` et `PhotoboothWorkflow` restent inchangés** : aucune préoccupation admin/réseau n'entre dans le cœur, et la machine à états critique de la borne (« ne doit jamais tomber ») n'est pas touchée. L'overlay étant **opaque**, le fait que le slideshow/connectivité tournent derrière est sans effet visuel.

> Approche B écartée (état `BoothState.AwaitingStart` dans le workflow + injection de l'URL) : fait entrer du réseau/admin dans `Core` et modifie du code critique, sans bénéfice ici.

## 4. Composants

### 4.1 `Photobooth.Core/Options/AdminOptions.cs`
Ajouter :
```csharp
/// <summary>Affiche l'URL d'admin à l'écran au démarrage (1er appui bouton photo = fermeture).
/// N'a d'effet que si Enabled. Défaut true.</summary>
public bool ShowAddressOnStartup { get; set; } = true;
```
`Validate()` inchangé (rien à valider).

### 4.2 Résolution de l'adresse — `Photobooth.Admin`
Un formateur **pur et testable** + un wrapper d'énumération réelle :

```csharp
public static class AdminAddress
{
    // Pur, testable : ordonne/filtre les IP et produit les URLs d'affichage.
    public static IReadOnlyList<string> BuildUrls(IEnumerable<IPAddress> addresses, int port);

    // Wrapper non testé : énumère les interfaces réelles puis délègue à BuildUrls.
    public static IReadOnlyList<string> LocalUrls(int port);
}
```

Règles de `BuildUrls` :
- ne garder que les **IPv4** ;
- **exclure** loopback (`127.0.0.0/8`) et link-local/APIPA (`169.254.0.0/16`) ;
- **trier** : sous-réseau GoPro `10.5.5.0/24` d'abord (cas « via la GoPro »), puis les autres ;
- format `http://{ip}:{port}` ;
- dédupliquer.

`LocalUrls` énumère via `System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()` (interfaces `OperationalStatus.Up`, hors loopback) et délègue à `BuildUrls`. Toute exception est avalée → liste vide (best-effort).

### 4.3 `Photobooth.App/ViewModels/MainViewModel.cs`
Sur le modèle de `Diagnostic` (set-once, post UI thread) :
```csharp
public string? AdminAddress { get; private set; }   // notif -> HasAdminAddress
public bool HasAdminAddress => !string.IsNullOrEmpty(AdminAddress);
public void ShowAdminAddress(string text) => Dispatcher.UIThread.Post(() => AdminAddress = text);
public void ClearAdminAddress()           => Dispatcher.UIThread.Post(() => AdminAddress = null);
```
`AdminAddress` contient la/les URL(s) (jointes par `\n` si plusieurs). Le titre et la consigne sont fixes côté XAML.

### 4.4 `Photobooth.App/Views/MainView.axaml`
Nouvel overlay **plein écran opaque**, `ZIndex="270"` (au-dessus de tout sauf le bandeau rouge `Diagnostic` à `300`, qui doit rester visible si la config est cassée), `IsVisible="{Binding HasAdminAddress}"`, `IsHitTestVisible="False"` (l'entrée passe par les boutons/touches, pas le pointeur). Contenu centré :
- titre « Administration » ;
- la/les URL en gros (lisibles de loin) ;
- consigne « Connectez-vous au Wi-Fi de la borne, puis ouvrez cette adresse. » ;
- « Appuyez sur le bouton photo pour démarrer ».

### 4.5 Gate — `Photobooth.App/App.axaml.cs`
- Décision de fermeture extraite dans une mini-classe testable (pas noyée dans le code-behind UI) :
  ```csharp
  // Vrai si l'appui a été consommé pour fermer l'écran d'accueil (donc PAS de capture).
  public sealed class StartupNoticeGate
  {
      private bool _pending;
      public bool Pending => _pending;
      public void Arm()  => _pending = true;
      public bool ConsumePress() { var was = _pending; _pending = false; return was; }
  }
  ```
- Au boot, après la décision d'activer l'admin : si `adminOpt.Enabled && adminOpt.ShowAddressOnStartup`, résoudre `AdminAddress.LocalUrls(adminOpt.Port)`, construire le texte (repli « Adresse introuvable — vérifiez le réseau. » si liste vide), `vm.ShowAdminAddress(text)` et `gate.Arm()`. **Affichage synchrone**, indépendant de l'`await` du démarrage Kestrel (c'est bien la « 1ʳᵉ action »).
- Câblage des appuis via des helpers gardés :
  - photo (bouton GPIO + `Espace`/`Entrée`) → `if (gate.ConsumePress()) { vm.ClearAdminAddress(); return; }` sinon `workflow.Submit(PhotoRequested)` ;
  - vidéo / impression → `if (gate.Pending) return;` sinon soumission normale.
  - `OnKey` passe de `static` à instance (ou reçoit un callback) pour accéder au `gate`.

## 5. Flux

```
Boot
 └─ OnFrameworkInitializationCompleted (UI thread)
     ├─ wiring boutons/clavier -> helpers gardés (gate)
     ├─ workflow.StartAsync()            (slideshow/connectivité tournent derrière, invisibles)
     ├─ admin: si Enabled -> AdminWebHost.StartAsync() (best-effort, inchangé)
     └─ si Enabled && ShowAddressOnStartup:
            urls = AdminAddress.LocalUrls(port)
            vm.ShowAdminAddress(format(urls)); gate.Arm()
1er appui photo -> gate.ConsumePress()==true -> vm.ClearAdminAddress(); pas de capture
appuis suivants -> comportement normal
```

## 6. Erreurs / dégradation

Cohérent avec l'existant (tout est best-effort, jamais fatal) :
- aucune IP exploitable → texte de repli, l'overlay s'affiche quand même puis se ferme normalement ;
- l'affichage ne dépend pas du succès du bind Kestrel (le port voulu est connu) ; un échec de bind reste loggé comme aujourd'hui ;
- toute exception d'énumération réseau est avalée (liste vide).

## 7. Tests

- `AdminOptionsTests` : `ShowAddressOnStartup` vaut `true` par défaut.
- Nouveau test `AdminAddress.BuildUrls` : ordre (`10.5.5.x` d'abord), exclusions (loopback `127.x`, link-local `169.254.x`), format `http://ip:port`, dédup, cas « aucune IP » → liste vide.
- Nouveau test `StartupNoticeGate` : `Arm` → `ConsumePress()` vrai une seule fois (puis faux) ; `Pending` reflète l'état.
- Vérification manuelle / screenshot : overlay visible au boot avec admin activé, fermé au 1er appui sans capture.

## 8. Docs

- `README_NET8.md` : ajouter `ShowAddressOnStartup` à la table des clés `Admin`.
- `GUIDE_OPERATEUR.md` : une ligne « au démarrage, l'URL d'admin s'affiche à l'écran ; appuyez sur le bouton photo pour démarrer ».
- Mettre D5 de la spec admin à jour (statut : overlay implémenté).

## 9. Hors périmètre (YAGNI)

- mDNS / `photobooth.local` (autre volet de D5, non requis ici).
- Mode AP dédié (Phase 2 de la spec admin).
- Affichage du PIN à l'écran (jamais : ce serait exposer le secret).
- Re-affichage de l'overlay après fermeture (une fois fermé, il ne revient pas avant le prochain boot).
