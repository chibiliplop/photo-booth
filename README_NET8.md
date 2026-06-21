# Photobooth (.NET 8 / Avalonia) — cross-platform

Réécriture cross-platform (Linux / Windows / macOS, cible **Raspberry Pi 3**) de l'ancienne application UWP. L'ancien code UWP reste dans `CS/`, `GoProWifi/`, `RasberryPiLib/` à titre de référence et n'est pas modifié.

## Documentation (quel doc pour qui)

| Vous voulez… | Document | Public |
|---|---|---|
| **Installer une borne** à partir de l'image (flasher la carte SD, premier démarrage) | [`INSTALLATION_BORNE.md`](INSTALLATION_BORNE.md) | installateur |
| **Fabriquer** l'image SD distribuable (reproductible, CI, sans Pi) | [`image-builder/README.md`](image-builder/README.md) + [`RUNBOOK_MAINTENEUR_CARTE_SD.md`](RUNBOOK_MAINTENEUR_CARTE_SD.md) | mainteneur |
| **Exploiter** la borne en événement (noms/fond/Wi-Fi, dépannage sur place) | [`GUIDE_OPERATEUR.md`](GUIDE_OPERATEUR.md) | opérateur |
| **Mettre en route à la main** sur un Pi (dev / debug, `scp` + systemd) | [`DEPLOY_RASPBERRY_PI.md`](DEPLOY_RASPBERRY_PI.md) | dev |
| Comprendre l'architecture, décisions, risques | le plan validé + [`LINUX_MIGRATION_BLOCKERS.md`](LINUX_MIGRATION_BLOCKERS.md) | dev |
| Tester **sans GoPro** | [`TESTING_WITHOUT_GOPRO.md`](TESTING_WITHOUT_GOPRO.md) (+ ci-dessous) | dev |
| Le **kit de déploiement** (units, provisioning, modèles FAT32) | [`deploy/README.md`](deploy/README.md) | mainteneur |

## Structure (`src/`)

| Projet | Rôle |
| --- | --- |
| `Photobooth.Core` | Logique pure : interfaces, workflow « acteur » (états Idle/Capturing/Recording/Degraded), options, modèles GoPro JSON, retry borné. Aucune dépendance UI/hardware. |
| `Photobooth.Adapters` | `HttpGoProClient` (HTTP/UDP réels) + `FakeGoProClient` ; GPIO/I2C Linux (`System.Device.Gpio`) + implémentations fake. |
| `Photobooth.App` | UI Avalonia (kiosk), composition root (DI + config + Serilog), `appsettings.json`, assets. |
| `Photobooth.Tests` | Tests xUnit du workflow (pilotés par les fakes). |

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

Tout est dans `src/Photobooth.App/appsettings.json` (sections `Gopro`, `Hardware`, `Timings`, `Theme`, `Logging`), surchargé par les variables `PHOTOBOOTH_Section__Cle`. Le thème par événement (noms, année, fond, police) se change ici sans recompiler.
