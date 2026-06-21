# Borne photo (Photobooth) — Raspberry Pi

Logiciel de **borne photo** pour Raspberry Pi : une application kiosk plein écran
(.NET 8 / Avalonia) qui pilote une **GoPro en Wi-Fi** pour prendre photos et
vidéos au bouton, affiche le résultat à l'écran, avec une **lumière optionnelle**.
Pensé pour tourner **tout seul et de façon stable** sur un Raspberry Pi 3 : la
carte SD démarre seule en plein écran, se reconnecte à la GoPro et se relance en
cas de pépin — sans clavier ni terminal.

> **Ce projet distribue un LOGICIEL, pas un produit matériel.** Vous montez votre
> propre borne (Raspberry Pi, écran, boutons, GoPro, câblage) et y installez
> l'image fournie.

## 🚀 Démarrer — installer une borne

➡️ **[Guide d'installation pas à pas → `INSTALLATION_BORNE.md`](INSTALLATION_BORNE.md)**

C'est le point de départ : matériel requis et **câblage GPIO**, obtenir l'image,
**flasher la carte SD avec Raspberry Pi Imager**, **régler le Wi-Fi de la GoPro et
le thème sur la carte** (avant le 1ᵉʳ démarrage), puis brancher → la borne arrive
**prête (bandeau vert)**.

## Ce que ça fait

- Kiosk **plein écran** au démarrage, **relance automatique** en cas de crash.
- Pilotage **GoPro Wi-Fi** : photo / vidéo via **boutons GPIO**, reconnexion auto.
- **Lumière** optionnelle (s'allume pendant la prise), broches GPIO **configurables**.
- **Config par événement sur la carte SD** (noms, année, fond, Wi-Fi) — éditable
  depuis un PC, sans recompiler.
- Image SD **reproductible** (construite sans Pi physique, en CI).

## Documentation

| Vous voulez… | Document |
|---|---|
| **Monter et installer** une borne (matériel, câblage, flashage, 1ᵉʳ boot) | **[`INSTALLATION_BORNE.md`](INSTALLATION_BORNE.md)** |
| **Exploiter** la borne en événement (noms/fond/Wi-Fi, dépannage sur place) | [`GUIDE_OPERATEUR.md`](GUIDE_OPERATEUR.md) |
| **Fabriquer** l'image SD distribuable (reproductible, CI, sans Pi) | [`image-builder/README.md`](image-builder/README.md) + [`RUNBOOK_MAINTENEUR_CARTE_SD.md`](RUNBOOK_MAINTENEUR_CARTE_SD.md) |
| **Développer** (build, test, lancer en local, sans GoPro) | [`README_NET8.md`](README_NET8.md) |
| Mise en route **manuelle** sur un Pi (dev / debug) | [`DEPLOY_RASPBERRY_PI.md`](DEPLOY_RASPBERRY_PI.md) |
| Architecture, décisions, risques | [`LINUX_MIGRATION_BLOCKERS.md`](LINUX_MIGRATION_BLOCKERS.md) |

## Pour les développeurs (en bref)

```bash
dotnet build Photobooth.sln
dotnet test  Photobooth.sln
dotnet run --project src/Photobooth.App   # local, mode démo (Espace/Entrée = photo, V = vidéo)
```

Détails (simulateur GoPro, captures d'écran, configuration) : [`README_NET8.md`](README_NET8.md).
