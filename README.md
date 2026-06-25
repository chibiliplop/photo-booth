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

## Ce que ça fait

- Kiosk **plein écran** au démarrage, **relance automatique** en cas de crash.
- Pilotage **GoPro Wi-Fi** : photo / vidéo via **boutons GPIO**, reconnexion auto.
- **Lumière** optionnelle (s'allume pendant la prise), broches GPIO **configurables**.
- **Config par événement sur la carte SD** (noms, année, fond, Wi-Fi) — éditable
  depuis un PC, sans recompiler.
- Image SD **reproductible** (construite sans Pi physique, en CI).

## Documentation

### Monter et utiliser une borne
| Étape | Document |
|---|---|
| Construire le matériel (câblage, lumière) | [`docs/monter-et-utiliser/1-electronique.md`](docs/monter-et-utiliser/1-electronique.md) |
| Installer l'image (flasher, 1er démarrage) | [`docs/monter-et-utiliser/2-installation.md`](docs/monter-et-utiliser/2-installation.md) |
| Préparer un événement (avant, à la maison) | [`docs/monter-et-utiliser/3-preparer-un-evenement.md`](docs/monter-et-utiliser/3-preparer-un-evenement.md) |
| Le jour J (sur place) | [`docs/monter-et-utiliser/4-le-jour-j.md`](docs/monter-et-utiliser/4-le-jour-j.md) |
| Référence des fichiers de config | [`docs/monter-et-utiliser/config-reference.md`](docs/monter-et-utiliser/config-reference.md) |

### Développer ou maintenir
| Sujet | Document |
|---|---|
| Développer (build, test, simulateur) | [`docs/developper-et-maintenir/developpement.md`](docs/developper-et-maintenir/developpement.md) |
| Architecture & décisions | [`docs/developper-et-maintenir/architecture.md`](docs/developper-et-maintenir/architecture.md) |
| Fabriquer l'image SD | [`docs/developper-et-maintenir/fabrication-image.md`](docs/developper-et-maintenir/fabrication-image.md) |
| Déploiement manuel (dev/debug) | [`docs/developper-et-maintenir/deploiement-manuel.md`](docs/developper-et-maintenir/deploiement-manuel.md) |
| Interface web d'admin/debug | [`docs/developper-et-maintenir/admin-debug.md`](docs/developper-et-maintenir/admin-debug.md) |

## Pour les développeurs (en bref)

```bash
dotnet build Photobooth.sln
dotnet test  Photobooth.sln
dotnet run --project src/Photobooth.App   # local, mode démo (Espace/Entrée = photo, V = vidéo)
```

Détails (simulateur GoPro, captures d'écran, configuration) : [`docs/developper-et-maintenir/developpement.md`](docs/developper-et-maintenir/developpement.md).
