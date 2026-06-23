# Admin/Debug — Phase 1, Plan 2/3 : Visualiseur de debug (lecture seule) — Design

- **Date** : 2026-06-23
- **Branche cible** : `feat/printer`
- **Statut** : design en attente de revue avant plan d'implémentation
- **Spec parente** : [`2026-06-23-admin-debug-web-interface-design.md`](2026-06-23-admin-debug-web-interface-design.md) (design d'ensemble, décisions D1–D11 verrouillées)
- **Plan précédent** : [`../plans/2026-06-23-admin-debug-phase1-observability-core.md`](../plans/2026-06-23-admin-debug-phase1-observability-core.md) (Plan 1/3, socle : `BoothTelemetry` + `InMemoryLogSink` + capture d'échec, livré commits `e74185e..3cefad8`)

## 1. Contexte & objectif

Le Plan 1/3 a posé une fondation **invisible** : `BoothTelemetry` (dernière raison d'échec d'impression) et `InMemoryLogSink` (ring buffer des 500 derniers logs) sont **écrits** mais rien ne les **lit** côté production. Aucun opérateur ne peut consulter ces données : pas de section de config `Admin`, pas de serveur web, pas d'URL.

**Objectif de ce plan** : rendre ces données **consultables sur place** via un petit hôte web embarqué **optionnel** (opt-in, off par défaut), pour qu'un opérateur ouvre une URL depuis son téléphone (connecté au WiFi de la GoPro) et voie l'état de la borne + les logs + la vraie raison d'un échec d'impression — **sans SSH**. C'est la première fonctionnalité opérateur du système d'admin, et celle qu'on documentera ensuite dans les docs d'installation.

Ce plan est volontairement **lecture seule** : aucune action, aucune écriture de config, aucun privilège système.

## 2. Périmètre

### Dans le périmètre (Plan 2/3)

- Section de config **`Admin`** (opt-in, `Enabled=false` par défaut).
- Hôte web Kestrel embarqué **`AdminWebHost`**, démarré seulement si `Admin.Enabled`, **dégradé jamais fatal**.
- Endpoints **lecture** : `GET /api/status`, `GET /api/logs`.
- **Authentification optionnelle** par PIN (`Admin.Pin`, vide = pas d'auth) : `GET/POST /login`, cookie HttpOnly.
- **Page HTML embarquée** autonome (CSS/JS inline, zéro ressource externe) qui affiche état + logs et se rafraîchit par polling.
- Extension **minimale** de `BoothTelemetry` (état borne + joignabilité GoPro) pour alimenter le dashboard.
- Câblage DI + démarrage/arrêt dans l'app.
- Tests xUnit (options, télémétrie étendue, endpoints en mémoire, gate PIN).

### Hors périmètre (→ Plan 3/3 et Phase 2)

- Édition de config (`PUT /api/config`, écriture FAT32 privilégiée) — **Plan 3/3**.
- Actions système (restart/reboot, `cupsenable`/`cupsaccept`, purge file) — **Plan 3/3** (sudoers).
- Console de commandes arbitraire — **Plan 3/3**.
- Onglet imprimante complet (repro `lpstat`/`lpinfo`, logs CUPS) — **Plan 3/3**.
- Flux logs live (SSE) — **Plan 3/3** (le polling suffit en 2/3).
- Mode AP dédié (`ap0`/hostapd/dnsmasq), mDNS (`photobooth.local`), overlay boot, persistance FAT32 des logs — **Phase 2**.
- `Admin.Exposure` (`ap`/`gopro`/`both`) : non introduit ici ; en 2/3 l'hôte écoute sur `ListenAddress`/`Port` (voir §4). L'énumération `Exposure` arrive avec le mode AP (Phase 2).

### Écart assumé vs design d'ensemble

La spec parente §5 décrit un `BoothTelemetry` riche (état, dernier `SetStatus`, GoPro, imprimante, capture, URL admin) et un flux SSE. Ce plan n'en livre que le **sous-ensemble lecture-seule utile au debug** : état borne, joignabilité GoPro, imprimante (via options), dernière impression. Le **dernier message de statut** (`SetStatus`) est **volontairement écarté** de cette tranche : il y a 14 sites d'appel `_display.SetStatus(...)` dans le workflow et l'info est largement redondante avec `state` + `goProReachable` + `lastPrint.reason` ; ne pas le capturer garde le diff workflow minimal (2 lignes). Réintroductible plus tard via un wrapper `PublishStatus` si un besoin réel apparaît.

## 3. Composants & responsabilités

| Composant | Projet | Rôle | Dépendances |
|---|---|---|---|
| `AdminOptions` | `Photobooth.Core/Options` | Section `Admin` + `Validate()`. | — |
| `BoothTelemetry` (étendu) | `Photobooth.Core/Diagnostics` | Ajoute `State` (BoothState) et `GoProReachable` (bool?) au snapshot existant. | `BoothState` (Core) |
| `AdminWebHost` | `Photobooth.Admin` | Possède le `WebApplication` Kestrel ; `StartAsync`/`StopAsync` ; mappe les endpoints ; sert la page ; applique le gate PIN. | `BoothTelemetry`, `InMemoryLogSink`, `IPrinterAdapter`, `IOptions<PrinterOptions>`, `IOptions<AdminOptions>` |
| `admin.html` | `Photobooth.Admin` (ressource embarquée) | Page autonome status + logs, polling. | — |

`AdminWebHost` vit dans `Photobooth.Admin` (qui référence déjà `Photobooth.Core`) et **ne dépend pas** de `Photobooth.App` ni de `PhotoboothWorkflow` : il lit tout depuis `BoothTelemetry`, `InMemoryLogSink` et les options/adapter injectés. Cela préserve les couches (App → Admin → Core).

### 3.1 `Photobooth.Admin.csproj`

Ajouter la référence framework ASP.NET (le runtime est embarqué en publish self-contained linux-arm64, rien à installer sur le Pi) :

```xml
<ItemGroup>
  <FrameworkReference Include="Microsoft.AspNetCore.App" />
</ItemGroup>
```

## 4. Configuration — section `Admin`

```jsonc
"Admin": {
  "Enabled": false,          // opt-in ; rien n'écoute tant que false
  "ListenAddress": "0.0.0.0",// interface d'écoute ; sur le Pi terrain la seule iface est le WiFi GoPro
  "Port": 8080,              // port Kestrel sur l'IP du Pi (distinct du 8080 de la GoPro 10.5.5.9)
  "Pin": ""                  // vide = pas d'auth ; sinon login requis
}
```

`AdminOptions.Validate()` (même motif que les autres Options, message FR, jamais de crash — renvoyé à `ValidateOptions` qui dégrade) :
- `Port` ∈ [1, 65535], sinon message FR.
- `ListenAddress` non vide (repli `0.0.0.0` documenté ; une valeur ininterprétable par Kestrel sera de toute façon avalée par le démarrage dégradé §7).
- `Pin` : aucune contrainte de format (chaîne libre ; vide accepté).

`Enabled=false` par défaut → **aucune surface d'attaque en production tant que l'admin n'est pas activé** (cohérent avec D10/§10 de la spec parente).

## 5. Endpoints (tous en lecture)

### `GET /api/status` → `200 application/json`

```jsonc
{
  "state": "Idle",                 // BoothState courant (télémétrie)
  "goProReachable": true,          // bool? ; null si jamais sondé
  "printer": { "enabled": true, "type": "cups" },
  "lastPrint": {                   // null si aucune impression tentée
    "succeeded": false,
    "reason": "lp failed with exit code 1: unknown destination",
    "at": "2026-06-23T18:21:09Z"
  },
  "version": "1.0.0",              // version assembly app (best-effort)
  "serverTimeUtc": "2026-06-23T18:21:42Z"
}
```

### `GET /api/logs` → `200 application/json`

Tableau des `LogLine` (`InMemoryLogSink.Snapshot()`), du plus ancien au plus récent :

```jsonc
[ { "timestamp": "…", "level": "Information", "message": "…", "exception": null }, … ]
```

Filtre par niveau : **côté page** (JS), pas de paramètre serveur.

### `GET /` → page HTML embarquée (voir §6).

### Authentification (si `Admin.Pin` non vide)

- `GET /login` → formulaire minimal (un champ `pin`).
- `POST /login` (form-urlencoded `pin`) → comparaison **temps constant** avec `Admin.Pin` ; succès → pose un cookie **HttpOnly** dont la valeur est un **jeton aléatoire opaque** généré une fois au démarrage de l'hôte (le PIN n'est jamais stocké dans le cookie) → `302` vers `/`. Échec → re-affiche `/login` avec message d'erreur.
- Middleware appliqué à toutes les autres routes : cookie absent/≠ jeton → `401` pour `/api/*`, `302 /login` pour `/`. Si `Pin` vide → middleware no-op (aucune auth).
- Pas d'expiration de session pour cette tranche (réseau local de confiance) ; le jeton change à chaque redémarrage de l'app.

## 6. Page HTML embarquée (`admin.html`)

Ressource **embarquée** dans `Photobooth.Admin` (`<EmbeddedResource>`), servie par l'hôte. **Autonome** : tout le CSS et le JS sont **inline**, **aucune** ressource externe (CDN, police, image distante) → fonctionne hors-ligne sur le WiFi GoPro et survit à une CSP stricte.

Contenu :
- **En-tête** : badge état borne (`state`), pastille GoPro (`goProReachable` : vert/rouge/gris si null), état imprimante (`enabled`/`type`).
- **Panneau « Dernière impression »** : succès/échec + **vraie raison** + heure (`lastPrint`) — l'info de debug phare ; mis en évidence si échec.
- **Panneau logs** : liste scrollable (plus récents en bas), boutons de filtre par niveau (All/Info/Warning/Error) appliqués en JS, lignes d'exception dépliables.
- **Rafraîchissement** : polling `fetch` de `/api/status` et `/api/logs` toutes les ~3 s ; indicateur discret « MAJ il y a Xs » ; en cas d'erreur fetch, bandeau « hôte injoignable » sans planter la page.
- **Pied** : `version` + `serverTimeUtc` (support).

## 7. Gestion d'erreurs — dégradé, jamais fatal

- `AdminWebHost.StartAsync` est enveloppé : toute exception (échec de bind, port occupé, adresse invalide) est **loggée (Serilog) puis avalée**. La borne photo continue de fonctionner normalement. Motif identique au mode dégradé de `Program.cs:67` et au try/catch des boutons GPIO dans `App.axaml.cs`.
- Si `Admin.Enabled=false` : l'hôte **n'est pas démarré** (et idéalement pas construit ; `StartAsync` est un no-op si appelé).
- Arrêt propre : `AdminWebHost.StopAsync` appelé dans le chemin de dispose de l'app (avant `Log.CloseAndFlush`).
- Lectures **thread-safe** : `BoothTelemetry` et `InMemoryLogSink` verrouillent déjà (Plan 1/3) ; les nouveaux champs `State`/`GoProReachable` suivent le même verrou.
- Exposition : sur le Pi terrain la seule interface réseau est le WiFi GoPro (`wlan0`), donc `0.0.0.0` n'expose de fait que sur ce réseau de confiance ; combiné à off-par-défaut + PIN optionnel, la surface reste maîtrisée (cohérent §10 spec parente). HTTPS/TLS hors périmètre (réseau local, cf. §13 spec parente).

## 8. Points d'intégration (code existant)

- **`BoothTelemetry`** (`src/Photobooth.Core/Diagnostics/BoothTelemetry.cs`) : ajouter `State` + `RecordState(BoothState)` et `GoProReachable` + `RecordGoProReachable(bool)` (verrou interne existant).
- **`PhotoboothWorkflow`** (`src/Photobooth.Core/Workflow/PhotoboothWorkflow.cs`) :
  - dans `SetState(BoothState s)` (chokepoint unique, ligne ~82) : ajouter `_telemetry.RecordState(s);`.
  - dans `ConnectivityLoopAsync` (là où `reachable` est calculé puis poussé via `_display.SetConnectivity`) : ajouter `_telemetry.RecordGoProReachable(reachable);`.
- **`ServiceConfiguration.AddPhotobooth`** (`src/Photobooth.App/Composition/ServiceConfiguration.cs`) : `services.Configure<AdminOptions>(config.GetSection(AdminOptions.Section));`, chaîner `AdminOptions.Validate()` dans `ValidateOptions`, `services.AddSingleton<AdminWebHost>();`.
- **Démarrage** : dans `App.OnFrameworkInitializationCompleted` (`src/Photobooth.App/App.axaml.cs`), après le `StartAsync()` du workflow, ajouter (sous try/catch dégradé) : `if (adminOpt.Enabled) _ = adminHost.StartAsync();`.
- **Arrêt** : appeler `adminHost.StopAsync()` dans le chemin de dispose (cohérent avec la disposition async du conteneur dans `Program.Main`).
- **Config** : déclarer la section `Admin` dans `src/Photobooth.App/appsettings.json` et dans `deploy/boot-config/photobooth.json` (valeurs par défaut, `Enabled=false`).

## 9. Tests

- **Unitaires** `AdminOptions.Validate()` : port hors borne rejeté, port valide accepté, `ListenAddress` vide → message ; `Pin` libre.
- **Unitaires** `BoothTelemetry` étendu : `RecordState`/`RecordGoProReachable` reflétés dans le snapshot ; thread-safe (lecture/écriture sous verrou).
- **Intégration workflow** : après une séquence, `Telemetry.State` suit l'état ; la boucle connectivité renseigne `GoProReachable` (réutilise/raffine le harness existant).
- **Endpoints en mémoire** via `Microsoft.AspNetCore.TestHost` (pas de vrai socket, ajouter le `PackageReference` au projet de tests) :
  - `GET /api/status` : forme JSON attendue à partir d'une `BoothTelemetry` pré-remplie.
  - `GET /api/logs` : renvoie les lignes bufferisées d'un `InMemoryLogSink` pré-rempli.
  - **Gate PIN** : `Pin` non vide → `GET /api/status` sans cookie ⇒ `401` ; `POST /login` bon PIN ⇒ cookie ; requête avec cookie ⇒ `200` ; mauvais PIN ⇒ refus. `Pin` vide → accès direct `200`.
- **Manuel sur Pi/dev** (hors CI) : `Admin.Enabled=true`, ouvrir `http://<ip-pi>:8080` depuis un autre appareil du réseau, vérifier rafraîchissement et affichage d'un échec d'impression réel.

## 10. Structure de fichiers

- `src/Photobooth.Core/Options/AdminOptions.cs` — **créé**.
- `src/Photobooth.Core/Diagnostics/BoothTelemetry.cs` — **modifié** (State + GoProReachable).
- `src/Photobooth.Core/Workflow/PhotoboothWorkflow.cs` — **modifié** (2 lignes : RecordState, RecordGoProReachable).
- `src/Photobooth.Admin/Photobooth.Admin.csproj` — **modifié** (`FrameworkReference` ASP.NET ; embarquer `admin.html`).
- `src/Photobooth.Admin/AdminWebHost.cs` — **créé**.
- `src/Photobooth.Admin/admin.html` — **créé** (ressource embarquée).
- `src/Photobooth.App/Composition/ServiceConfiguration.cs` — **modifié** (DI + Validate).
- `src/Photobooth.App/App.axaml.cs` — **modifié** (start/stop dégradé).
- `src/Photobooth.App/appsettings.json` + `deploy/boot-config/photobooth.json` — **modifiés** (section `Admin`).
- `src/Photobooth.Tests/Photobooth.Tests.csproj` — **modifié** (`Microsoft.AspNetCore.TestHost`).
- `src/Photobooth.Tests/AdminOptionsTests.cs`, `AdminWebHostTests.cs`, (+ extensions `BoothTelemetryTests`/workflow) — **créés/modifiés**.

## 11. Suite

- **Plan 3/3** : écriture (config FAT32 privilégiée), actions système (sudoers), console, onglet imprimante complet, SSE.
- **Phase 2** : mode AP dédié, mDNS, overlay boot, persistance FAT32 des logs, `Admin.Exposure`.
- **Docs d'installation** : une fois ce plan livré, documenter le visualiseur **optionnel** dans `INSTALLATION_BORNE.md` / `DEPLOY_RASPBERRY_PI.md` / `GUIDE_OPERATEUR.md` (activation `Admin.Enabled`, URL, PIN, « lecture seule, off par défaut »).
