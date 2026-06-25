# Photobooth (.NET 8 / Avalonia) — développement

## Structure (`src/`)

| Projet | Rôle |
| --- | --- |
| `Photobooth.Core` | Logique pure : interfaces, workflow « acteur » (états Idle/Capturing/Recording/Degraded/ShuttingDown), options, modèles GoPro JSON, retry borné, télémétrie diagnostic (`Diagnostics/BoothTelemetry` : dernière raison d'échec d'impression). Aucune dépendance UI/hardware. |
| `Photobooth.Adapters` | `HttpGoProClient` (HTTP/UDP réels) + `FakeGoProClient` ; GPIO/I2C Linux (`System.Device.Gpio`) + implémentations fake. |
| `Photobooth.Admin` | Hôte web d'admin/debug embarqué (Kestrel, **opt-in** via `Admin.Enabled`, désactivé par défaut). Lecture : `InMemoryLogSink` (ring buffer Serilog des 500 derniers logs) + flux live (SSE), état borne/imprimante. Écriture : actions imprimante/CUPS, édition de `photobooth.json`, console shell, restart/reboot. Auth PIN optionnel + CSRF = seule frontière réseau↔root. Voir [`admin-debug.md`](admin-debug.md). Référencé par `Photobooth.App`. |
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

⚠️ **macOS — lancer depuis une session graphique** (iTerm/Terminal ouvert sur le bureau). Lancé depuis un process détaché de la session Aqua (SSH, daemon launchd, ou sous-process d'un agent/outil), le backend `Avalonia.Native` échoue au démarrage avec `System.InvalidOperationException: Avalonia.Native was not able to start the RenderTimer. Native error code is: -6661`. `dotnet test` n'est pas affecté (les tests n'initialisent jamais la couche graphique).

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

## Tester sans GoPro

Deux niveaux de test sont disponibles sans caméra physique :

1. **Mode fake (par défaut)** : `Gopro.Mode=fake` dans `appsettings.json` — `FakeGoProClient` retourne des images locales sans réseau. Idéal pour le développement UI et les tests de workflow (`dotnet test`).

2. **Simulateur GoPro** : `tools/gopro-simulator/simulator.py` simule les endpoints HTTP GoPro (`gpControl`, listing media, téléchargement d'images) et le listener UDP keepalive. Utile pour valider les appels réseau réels sans caméra (voir la section « Contre le simulateur GoPro » ci-dessus).

## Configuration

Tout est dans `src/Photobooth.App/appsettings.json` (sections `Gopro`, `Hardware`, `Timings`, `Theme`, `Admin`, `Logging`), surchargé par les variables `PHOTOBOOTH_Section__Cle`. Le thème par événement (noms, année, fond, police) se change ici sans recompiler.
