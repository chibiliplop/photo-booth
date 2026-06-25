# Kit de déploiement « turnkey » — borne photo

Ce dossier regroupe tout ce qui rend la borne **plug-and-play** : l'opérateur
non-technique ne touche qu'à la couche éditable (`boot-config/`), le reste est
figé dans l'image SD par le mainteneur.

## Architecture en 2 couches

| Couche | Contenu | Modifiée par | Où |
|---|---|---|---|
| **OS figé** | OS Lite 64-bit + app .NET + services systemd + provisioning + overlay read-only | mainteneur, rarement | partition `ext4` (invisible sous Windows) |
| **Config événement** | `wifi.txt`, `photobooth.json`, `fond.jpg` | opérateur, à chaque événement | partition **boot FAT32** `/boot/firmware/photobooth/` (visible sous Windows) |

## Contenu du kit

| Fichier | Destination sur le Pi | Rôle |
|---|---|---|
| `photobooth-provision.sh` | `/usr/local/sbin/photobooth-provision.sh` | applique le Wi-Fi GoPro au boot (idempotent, gère les CRLF Windows) |
| `systemd/photobooth-provision.service` | `/etc/systemd/system/` | lance le script ci-dessus avant l'app |
| `systemd/photobooth.service` | `/etc/systemd/system/` | service kiosk durci (rendu logiciel, Restart=always) |
| `boot-config/wifi.txt` | `/boot/firmware/photobooth/` | **modèle** Wi-Fi (clés `GOPRO_SSID` / `GOPRO_PASSWORD`) |
| `boot-config/photobooth.json` | `/boot/firmware/photobooth/` | **modèle** thème (noms, année, fond, mode démo) |
| `boot-config/LISEZ-MOI.txt` | `/boot/firmware/photobooth/` | notice opérateur (sur la carte) |
| `boot-config/admin.txt` | `/boot/firmware/photobooth/` | **avancé** : override optionnel du mot de passe SSH `pi` (réappliqué au boot) |
| `sudoers.d/photobooth` | `/etc/sudoers.d/photobooth` (`0440 root`) | **avancé** : `pi ALL=(ALL) NOPASSWD: ALL` — privilèges root de l'hôte web d'admin/debug (opt-in). Validé par `visudo -c` à l'install. |
| `photobooth-write-config.sh` | `/usr/local/sbin/` (`0755`) | **avancé** : écriture atomique de `photobooth.json` sur la FAT32 root, appelée en `sudo` par l'hôte d'admin. |
| `fond.jpg` (à ajouter) | `/boot/firmware/photobooth/` | image de fond modèle |

## Procédure complète

La fabrication de l'image SD distribuable est décrite pas à pas dans
[`../docs/developper-et-maintenir/fabrication-image.md`](../docs/developper-et-maintenir/fabrication-image.md).
Le guide destiné à l'opérateur non-technique est
[`../GUIDE_OPERATEUR.md`](../GUIDE_OPERATEUR.md).

> **Build reproductible sans Pi** : ce dossier `deploy/` est consommé tel quel
> par [`../image-builder/`](../image-builder/README.md) (CustoPiZer + GitHub
> Actions), qui produit `photobooth-dist.img.xz` à partir de l'image Lite
> officielle. C'est la source de vérité unique — ne rien dupliquer ailleurs.

## ⚠️ Trois points de vigilance

1. **Hôte web d'admin/debug** : `Photobooth.Admin` est **désactivé par défaut** (`Admin.Enabled=false` → rien n'écoute). Une fois activé (section `Admin` dans `photobooth.json`), il expose une console root via `sudo` gardée **uniquement** par `Admin.Pin` (+ CSRF/SameSite). Le `sudoers` ci-dessus accorde `NOPASSWD: ALL`. **Conséquence : n'activer que sur un réseau de confiance et toujours définir un PIN.** Détails : `../RUNBOOK_MAINTENEUR_CARTE_SD.md` §3.5.

2. **Rendu logiciel** : en Avalonia 11, `--drm` (`StartLinuxDrm`) est *toujours*
   accéléré matériellement (GPU VC4), et `AVALONIA_RENDERER=software` est un
   **no-op** (vestige d'Avalonia 0.10). Le service force donc le GL logiciel au
   niveau Mesa (`LIBGL_ALWAYS_SOFTWARE=1` + `GALLIUM_DRIVER=llvmpipe`).
   **À valider sur le vrai Pi 3** ; repli garanti = backend FBDev. Détails dans le runbook.
3. **Fins de ligne** : `photobooth-provision.sh` doit rester en **LF** (Unix), pas
   CRLF, sinon le `#!/bin/bash` casse. Les fichiers de `boot-config/` peuvent être
   en CRLF (le script et `System.Text.Json` les tolèrent).
