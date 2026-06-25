# AGENTS.md

## Aperçu projet

Borne photo événementielle : un Raspberry Pi pilote l'UI, les boutons GPIO et la lumière ; une GoPro prend les photos/vidéos et expose ses médias via son API HTTP Wi-Fi. L'application est un exécutable **.NET 8 / Avalonia 11** en kiosk plein écran, auto-hébergé (runtime inclus, pas de dépendance runtime sur le Pi).

## Structure `src/`

Cinq projets en couches ; les dépendances ne vont que vers l'intérieur (`Core` ne référence rien d'applicatif) :

| Projet | Rôle |
| --- | --- |
| `Photobooth.Core` | Domaine pur : abstractions, workflow acteur (états Idle/Capturing/Recording/Degraded/ShuttingDown), options, modèles GoPro JSON, télémétrie diagnostic, résilience. |
| `Photobooth.Adapters` | Implémentations concrètes : `HttpGoProClient`/`FakeGoProClient` ; GPIO/I2C Linux + fakes ; adaptateurs d'impression (CUPS / fichier / no-op). |
| `Photobooth.Admin` | Hôte web Kestrel embarqué, opt-in (`Admin.Enabled`), jamais fatal pour la borne. |
| `Photobooth.App` | UI Avalonia (kiosk) + composition root : DI, configuration en couches, Serilog, point d'entrée. |
| `Photobooth.Tests` | Tests xUnit (workflow, observabilité, admin), pilotés par les fakes. |

Pour l'architecture détaillée (workflow acteur, rendu DRM/FBDev, packaging) : [`docs/developper-et-maintenir/architecture.md`](docs/developper-et-maintenir/architecture.md).

## Build / test / run

Voir [`docs/developper-et-maintenir/developpement.md`](docs/developper-et-maintenir/developpement.md) pour les commandes de build, les modes de lancement (bureau dev, Pi DRM, Pi FBDev), et les commandes de test.

## Règles agent

**Langue** : les chaînes de l'UI et les messages visibles par les opérateurs restent en français. Ne pas modifier la langue des affichages de la borne sans instruction explicite.

**Fichiers générés** : ne pas éditer `bin/`, `obj/`, ni les fichiers `*.g.cs` / `*.g.i.cs`. Les modifier est sans effet et peut casser le build.

**`catch` silencieux** : le principe du projet est « dégradé, jamais fatal ». Les blocs `catch` avalent délibérément certaines erreurs pour éviter un crash kiosk. Avant de sortir une exception ou de rendre une erreur visible, s'assurer que cela ne provoquera pas un écran noir sur site. Logguer suffisamment pour le débogage.

**Note macOS — erreur -6661** : lancer `dotnet run --project src/Photobooth.App` uniquement depuis un **terminal de la session graphique** (iTerm2 / Terminal ouvert sur le bureau). Un processus détaché de la session Aqua (SSH, daemon, ou sous-process d'un outil comme Claude Code) provoque l'erreur `Avalonia.Native was not able to start the RenderTimer. Native error code is: -6661` **au démarrage**, sans rapport avec le code applicatif. `dotnet test` n'est pas affecté.

**Déploiement turnkey — architecture 2 couches** :
- **Couche 1 — image SD figée** : publiée par le mainteneur (script `image-builder/build-local.sh`, publication self-contained `linux-arm64`) ; elle ne change qu'à chaque release.
- **Couche 2 — config événement** : fichier `photobooth.json` éditable sur la partition FAT32 `/boot/firmware/photobooth/` (accessible sans booter le Pi). Toute personnalisation événementielle passe par cette couche ; ne jamais modifier l'image pour un réglage ponctuel.
