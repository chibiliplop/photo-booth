# image-builder — fabrication reproductible de l'image SD (CustoPiZer)

Construit l'image distribuable **`photobooth-dist.img.xz`** *sans Pi physique*, de
façon **reproductible et versionnée**, en partant de l'image Raspberry Pi OS Lite
64-bit officielle. C'est la mise en œuvre de l'**option §10 du
[RUNBOOK](../RUNBOOK_MAINTENEUR_CARTE_SD.md)**, désormais méthode **principale**
(distribution « à n'importe qui » → le déclencheur du §10 est franchi).

## Ce que ça remplace (et ce que ça ne remplace PAS)

| | Méthode | Outil |
|---|---|---|
| **Fabriquer** la couche OS (PHASES 1-3, 5) | reproductible, en CI, sans Pi | **CustoPiZer + ce dossier** |
| **Valider** en conditions réelles (PHASE 4) | **toujours sur un vrai Pi, PAR MODÈLE** | un Pi 3 / 4 / 5 réel |

> CustoPiZer tourne sous **émulation QEMU** : il sait faire `apt`, copier le
> binaire, poser les services. Il **ne peut pas** tester le rendu GPU/DRM, les
> boutons GPIO, le Wi-Fi GoPro ni l'endurance. Pour écrire « validé Pi 4 », il
> faut un Pi 4 réel. **Les fixes vont dans les scripts, jamais sur le Pi** :
> si la Phase 4 échoue → corriger `00-photobooth.sh` (ou `deploy/`) → rebuild.

## Disposition

```
image-builder/
├── README.md            ce fichier
├── build-local.sh       build complet en local (WSL2/Linux + Docker)
├── config.local         surcharges CustoPiZer (agrandit le root avant build)
├── input.img            (généré) image Lite officielle — ignoré par git
├── output.img           (généré) image personnalisée — ignoré par git
└── scripts/
    ├── 00-photobooth.sh  personnalisation (rejoue §1.2/1.3/3.1-3.4 + overlay)
    └── files/            (peuplé au build) monté sur /files dans le chroot
        ├── deploy/        <- copie de ../../deploy/
        └── publish/       <- sortie de dotnet publish
```

**Source de vérité unique = `../deploy/`.** On ne duplique rien : `build-local.sh`
et la CI y copient `deploy/` + `publish/` dans `scripts/files/` le temps du build.

## Build en local (recommandé pour la 1ʳᵉ fois)

```bash
# WSL2 ou Linux, avec dotnet 8, docker, xz, curl :
cd image-builder
./build-local.sh                       # image « dist » (overlay ON)
PHOTOBOOTH_OVERLAY=0 ./build-local.sh  # image « dev » (root inscriptible, pour itérer)
```

Produit `photobooth-dist.img.xz`.

## Build en CI (GitHub Actions)

`.github/workflows/build-image.yml` : `dotnet publish` → QEMU → CustoPiZer →
PiShrink → artefact (+ Release sur tag `v*`). C'est exactement le modèle de
construction d'OctoPi.

- **Lancer** : onglet *Actions* → *Build SD image* → *Run workflow* (case
  `overlay` décochable pour une image dev). Ou pousser un tag `vX.Y.Z`.
- **Aucun secret CI** : l'image part en **modèles neutres** (`wifi.txt`,
  `photobooth.json`), le vrai config est ajouté par l'opérateur sur la FAT32.
- **Mot de passe pi (SSH)** : un **défaut** (`raspberry`) est baké, modifiable
  au build via `PI_PASSWORD`. Surtout, il est **surchargeable par carte** via
  `/boot/firmware/photobooth/admin.txt` (réappliqué à chaque boot → **persiste
  sous overlay**, contrairement à un `passwd` en SSH). Donc pas de « secret » à
  gérer en CI. SSH activable/désactivable au build via `PI_SSH` (1 par défaut).

## ⚠️ Le point à dérisquer EN PREMIER : l'overlay dans le chroot

L'overlay read-only repose sur un **initramfs** ; sa génération
(`update-initramfs`) **sous QEMU** est la chose la plus susceptible d'échouer.
`00-photobooth.sh` la **tente sans bloquer le build** et trace le résultat ; si
l'overlay n'a pas pu être appliqué, il dépose `/home/pi/photobooth/OVERLAY_STATUS.txt`
et l'avertit dans les logs.

**Donc : valide d'abord en local** (`build-local.sh`, flash, boot sur Pi,
`mount | grep overlay`). Si l'overlay ne « prend » pas via la CI :
1. build en `PHOTOBOOTH_OVERLAY=0`, puis activer l'overlay manuellement sur le
   Pi de référence (RUNBOOK §5.1) avant un clone ponctuel ; ou
2. corriger l'étape overlay du script une fois la cause initramfs comprise.

## Distribuer (PHASE 6) — le piège Imager

L'image est turnkey, sans secret embarqué (jamais bootée → pas de `machine-id`,
clés SSH régénérées par PiShrink `-c`). Au flashage :

- **Ne JAMAIS activer la « customisation OS » de Raspberry Pi Imager** (Wi-Fi /
  SSH / hostname) sur l'image dist : Imager **mémorise** et **ré-applique
  silencieusement** ses réglages → profil NM `preconfigured` injecté (fuite de
  SSID + comportement imprévisible sous overlay), hostname/user écrasés.
- Flasher **sans customisation**, ou utiliser **Balena Etcher** (qui ne customise
  pas). Filet de sécurité : `photobooth-provision.sh` **purge `preconfigured`
  à chaque boot**, quoi qu'il arrive.

## Portabilité matériel (rappel)

Le même `.img.xz` boote sur **Pi 3 / 3B+ / 3A+, Pi 4 / 400, Zero 2 W** (OS
multi-modèles + binaire `linux-arm64` + rendu logiciel forcé). **Pi 5** = cible
distincte (boot RP1, firmware récent) à valider séparément. **Ne tourne pas** sur
Pi 1 / Zero / Zero W (ARMv6).
