# Photobooth (.NET 8 / Avalonia) — cross-platform

Réécriture cross-platform (Linux / Windows / macOS, cible **Raspberry Pi 3**) de l'ancienne application UWP. L'ancien code UWP reste dans `CS/`, `GoProWifi/`, `RasberryPiLib/` à titre de référence et n'est pas modifié.

## Documentation (quel doc pour qui)

| Vous voulez… | Document | Public |
|---|---|---|
| **Monter et installer une borne** (matériel + câblage GPIO + flasher la carte + 1ᵉʳ boot) | [`INSTALLATION_BORNE.md`](INSTALLATION_BORNE.md) | monteur / installateur |
| **Fabriquer** l'image SD distribuable (reproductible, CI, sans Pi) | [`image-builder/README.md`](image-builder/README.md) + [`RUNBOOK_MAINTENEUR_CARTE_SD.md`](RUNBOOK_MAINTENEUR_CARTE_SD.md) | mainteneur |
| **Exploiter** la borne en événement (noms/fond/Wi-Fi, dépannage sur place) | [`GUIDE_OPERATEUR.md`](GUIDE_OPERATEUR.md) | opérateur |
| **Mettre en route à la main** sur un Pi (dev / debug, `scp` + systemd) | [`DEPLOY_RASPBERRY_PI.md`](DEPLOY_RASPBERRY_PI.md) | dev |
| Comprendre l'architecture, décisions, risques | le plan validé + [`LINUX_MIGRATION_BLOCKERS.md`](LINUX_MIGRATION_BLOCKERS.md) | dev |
| Tester **sans GoPro** | [`TESTING_WITHOUT_GOPRO.md`](TESTING_WITHOUT_GOPRO.md) (+ ci-dessous) | dev |
| Le **kit de déploiement** (units, provisioning, modèles FAT32) | [`deploy/README.md`](deploy/README.md) | mainteneur |

## Structure (`src/`)

| Projet | Rôle |
| --- | --- |
| `Photobooth.Core` | Logique pure : interfaces, workflow « acteur » (états Idle/Capturing/Recording/Degraded), options, modèles GoPro JSON, retry borné, télémétrie diagnostic (`Diagnostics/BoothTelemetry` : dernière raison d'échec d'impression). Aucune dépendance UI/hardware. |
| `Photobooth.Adapters` | `HttpGoProClient` (HTTP/UDP réels) + `FakeGoProClient` ; GPIO/I2C Linux (`System.Device.Gpio`) + implémentations fake. |
| `Photobooth.Admin` | Hôte web d'admin/debug embarqué (Kestrel, **opt-in** via `Admin.Enabled`, désactivé par défaut). Lecture : `InMemoryLogSink` (ring buffer Serilog des 500 derniers logs) + flux live (SSE), état borne/imprimante. Écriture : actions imprimante/CUPS, édition de `photobooth.json`, console shell, restart/reboot. Auth PIN optionnel + CSRF = seule frontière réseau↔root. Voir [« Interface web d'admin/debug »](#interface-web-dadmindebug-opt-in) et `docs/superpowers/specs/`. Référencé par `Photobooth.App`. |
| `Photobooth.App` | UI Avalonia (kiosk), composition root (DI + config + Serilog), `appsettings.json`, assets. |
| `Photobooth.Tests` | Tests xUnit du workflow et du socle d'observabilité (pilotés par les fakes). |

## Pré-requis

- SDK **.NET 8**. Si absent : `dotnet-install` (script officiel) installe sans droits admin dans le profil utilisateur.
  - Windows : `irm https://dot.net/v1/dotnet-install.ps1 | iex` puis `./dotnet-install.ps1 -Channel 8.0`
  - macOS/Linux : `curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 8.0`

## Build & test

```bash
dotnet build Photobooth.sln
dotnet test  Photobooth.sln
```

## Lancer en local (sans GoPro, sans hardware)

Par défaut `appsettings.json` est en `Gopro.Mode=fake` + `Hardware.Mode=auto` (→ fake hors Linux/GPIO) :

```bash
dotnet run --project src/Photobooth.App
```
- Déclencheurs clavier : **Espace/Entrée** = photo, **V** = vidéo.
- `--fullscreen` : kiosk plein écran sur le bureau. `--drm` : kiosk Raspberry Pi (DRM/FBDev).

### Contre le simulateur GoPro (HTTP/UDP réels, sans caméra)

Terminal 1 :
```bash
python tools/gopro-simulator/simulator.py --host 127.0.0.1 --port 8080 --udp-port 8554
```
Terminal 2 (surcharge par variables d'environnement préfixées `PHOTOBOOTH_`) :
```bash
# Windows PowerShell
$env:PHOTOBOOTH_Gopro__Mode='http'
$env:PHOTOBOOTH_Gopro__ControlBaseUrl='http://127.0.0.1:8080'
$env:PHOTOBOOTH_Gopro__MediaBaseUrl='http://127.0.0.1:8080'
$env:PHOTOBOOTH_Gopro__KeepAliveHost='127.0.0.1'
dotnet run --project src/Photobooth.App
```
```bash
# macOS/Linux
PHOTOBOOTH_Gopro__Mode=http \
PHOTOBOOTH_Gopro__ControlBaseUrl=http://127.0.0.1:8080 \
PHOTOBOOTH_Gopro__MediaBaseUrl=http://127.0.0.1:8080 \
PHOTOBOOTH_Gopro__KeepAliveHost=127.0.0.1 \
dotnet run --project src/Photobooth.App
```

### Capture d'écran de vérification (sans interaction)

`--screenshot <chemin.png>` déclenche une photo puis enregistre deux PNG (décompte + photo) et quitte. Pratique en CI / sans opérateur.

## Configuration

Tout est dans `src/Photobooth.App/appsettings.json` (sections `Gopro`, `Hardware`, `Timings`, `Theme`, `Admin`, `Logging`), surchargé par les variables `PHOTOBOOTH_Section__Cle`. Le thème par événement (noms, année, fond, police) se change ici sans recompiler.

## Interface web d'admin/debug (opt-in)

`Photobooth.Admin` héberge un petit serveur web (Kestrel) pour le **dépannage terrain**. Il est **désactivé par défaut** : tant que `Admin.Enabled` est `false`, **rien n'écoute** (zéro surface d'attaque).

| Clé (`Admin`) | Défaut | Rôle |
|---|---|---|
| `Enabled` | `false` | active l'hôte web |
| `ListenAddress` | `0.0.0.0` | interface d'écoute Kestrel |
| `Port` | `8080` | port (distinct du 8080 de la GoPro `10.5.5.9`) |
| `Pin` | `""` (vide) | PIN d'accès ; **vide = aucune authentification** |

Activation (dev) :

```bash
PHOTOBOOTH_Admin__Enabled=true PHOTOBOOTH_Admin__Pin=1234 dotnet run --project src/Photobooth.App
# puis http://<ip>:8080  → login PIN → page à onglets (logs, imprimante, config, actions, console)
```

Ce qu'il expose en **écriture** : actions imprimante/CUPS (cupsenable/accept/test/purge, lpinfo), édition de `photobooth.json` (valide → écrit → **redémarre la borne**), **console shell arbitraire** (sudo `NOPASSWD: ALL` sur la borne), restart/reboot, reprise GoPro.

**Modèle de menace (dérogation read-write 2026-06-23)** : le **PIN** (+ la clé WiFi GoPro) est l'**unique frontière réseau↔root**. Contrôles : sortie échappée (`textContent`), CSRF sur toute mutation, cookie `HttpOnly`+`SameSite=Strict`, audit-log de chaque action/commande, warning bruyant si `Enabled && Pin==""`. **N'exposer que sur un réseau de confiance, et toujours définir un PIN.** Détails mainteneur : `RUNBOOK_MAINTENEUR_CARTE_SD.md` §3.5 ; conception : `docs/superpowers/specs/2026-06-23-admin-debug-web-interface-design.md`.
