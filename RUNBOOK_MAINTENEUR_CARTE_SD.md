# RUNBOOK MAINTENEUR — Fabriquer la carte SD turnkey

> **Pour qui** : le mainteneur (vous), sous Windows. **Pas** l'opérateur événementiel.
> **But** : produire une image `photobooth-dist.img.xz` distribuable, validée en conditions réelles, qui démarre seule sur la borne dès qu'on branche HDMI + boutons + GoPro + alim.
> **Méthode retenue** : **golden master clone + PiShrink**, en architecture 2 couches (OS figé / config événement éditable sur FAT32). Option de secours **CustoPiZer** en §10.
> **Principe directeur** : ce qui est distribué doit être, bit pour bit, ce qui a été validé sur le terrain. On fabrique l'image **une fois**, on la fige, on ne la refait que si l'OS ou le code change.

---

## Architecture en 2 couches (rappel)

| Couche | Contenu | Où physiquement | Modifiée par |
|---|---|---|---|
| **OS + système (figée)** | OS Lite 64-bit Bookworm, paquets, service systemd durci, overlay FS, provisioning Wi-Fi, le binaire `publish/` | Partition `ext4` (invisible sous Windows) | Mainteneur, rarement |
| **Config événement (éditable)** | `wifi.txt`, `photobooth.json`, `fond.jpg` | Partition **FAT32** `/boot/firmware/photobooth/` (visible Windows/Mac) | Opérateur, à chaque événement |

> Les fichiers prêts à copier (script de provisioning, units systemd, modèles `boot-config/`) sont versionnés dans **`deploy/`**. Ce runbook en reproduit le contenu inline pour le suivi pas-à-pas.

Tout ce qui change par événement vit sur la FAT32, **hors overlay**. Tout le reste est figé et protégé par l'overlay read-only.

---

## PHASE 0 — Code (sous Windows, AVANT de toucher au Pi)

> ✅ **Ces 2 diffs sont DÉJÀ appliqués** dans `src/Photobooth.App/Program.cs` (ce dépôt). Ils sont reproduits ici pour mémoire et pour les rejouer si besoin sur un autre checkout.

### 0.1 — Diff #1 : charger `photobooth.json` externe depuis la FAT32

Fichier : `src/Photobooth.App/Program.cs`. Le bloc de construction de configuration est désormais :

```csharp
var baseDir = AppContext.BaseDirectory;

// PHOTOBOOTH_CONFIG_DIR permet de pointer ailleurs (tests). Repli dev: ./config.
var externalConfigDir = Environment.GetEnvironmentVariable("PHOTOBOOTH_CONFIG_DIR")
    ?? "/boot/firmware/photobooth";

var configBuilder = new ConfigurationBuilder()
    .SetBasePath(baseDir)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);

// On n'ajoute le fichier que s'il EXISTE : un dossier/fichier absent ne lève
// jamais d'exception (identique sur le Pi et sur un poste de dev Windows).
foreach (var dir in new[] { externalConfigDir, Path.Combine(baseDir, "config") })
{
    var overridePath = Path.Combine(dir, "photobooth.json");
    if (File.Exists(overridePath))
        configBuilder.AddJsonFile(overridePath, optional: true, reloadOnChange: false);
}

var config = configBuilder
    .AddEnvironmentVariables(prefix: "PHOTOBOOTH_") // e.g. PHOTOBOOTH_Gopro__Mode=http
    .Build();
```

Ordre essentiel : l'`appsettings.json` embarqué fournit les valeurs par défaut, `photobooth.json` les surcharge (thème de l'événement), les env-vars restent prioritaires.

> **Pourquoi le garde `File.Exists` plutôt qu'`AddJsonFile(chemin, optional:true)` direct** : avec un chemin absolu dont le **dossier parent n'existe pas** (cas du poste Windows où `/boot/firmware/photobooth` est absent), le provider peut lever une exception au démarrage — `optional:true` ne couvre que le fichier absent, pas le dossier absent. Le `File.Exists` lève l'ambiguïté sur toutes les plateformes, sans dépendance supplémentaire.

### 0.2 — Diff #2 : ne jamais crasher en boucle sur config invalide

Fichier : `src/Photobooth.App/Program.cs`. Le `return 2` sur config invalide est remplacé par une dégradation gracieuse :

```csharp
            var error = ServiceConfiguration.ValidateOptions(Services);
            if (error is not null)
            {
                // Un opérateur non-tech peut saisir une valeur invalide dans photobooth.json.
                // Ne JAMAIS renvoyer un code non nul ici : avec systemd Restart=always, cela
                // crée un crash-loop écran noir impossible à diagnostiquer sur site. On dégrade :
                // log + on continue (GoPro injoignable et GPIO absent s'auto-réparent déjà).
                Log.Error("Configuration invalide, démarrage en mode dégradé : {Error}", error);
            }
```

> **Portée réelle (défense en profondeur)** : `ValidateOptions` ne valide que `Gopro`/`Hardware`/`Timing`, **pas** `Theme` (vérifié dans `ServiceConfiguration.cs` / `*Options.Validate()`). Or l'opérateur n'édite que `Theme` + `Gopro:Mode`. Le risque de crash-loop par sa config est donc faible — mais ce diff ferme le dernier chemin (valeur exotique sur `Gopro:Mode`, futur champ validé) et coûte une ligne. À garder.

### 0.3 — (Recommandé) Bandeau de statut GoPro plein écran

Câbler `IsReachableAsync` (`HttpGoProClient.cs`) → `SetStatus` (`MainViewModel.cs`) en boucle, avec un visuel grand et coloré : **vert** « GoPro connectée — prête », **orange** « Recherche GoPro… », **rouge** « GoPro perdue ». C'est ce qui permet à l'opérateur de savoir « ça marche / ça ne marche pas » sans terminal. Non bloquant pour la fabrication de l'image, mais fortement conseillé avant de figer.

### 0.4 — Vérifier sous Windows puis publier

```powershell
# Test local : pointer la config externe sur un dossier contenant photobooth.json
$env:PHOTOBOOTH_CONFIG_DIR = "D:\tmp\photobooth-config"
dotnet run --project src\Photobooth.App --fullscreen
```

Build de distribution (self-contained, **trimming désactivé**, ReadyToRun activé) :

```powershell
dotnet publish src\Photobooth.App\Photobooth.App.csproj -c Release `
  -r linux-arm64 --self-contained true -p:PublishReadyToRun=true -o publish
```

> Si le Pi de référence affiche `armv7l` (OS 32-bit) au lieu de `aarch64`, publier en `-r linux-arm`. La cible recommandée est **64-bit** (`linux-arm64`).

Le dossier `publish/` produit ici est l'artefact « couche OS » que l'on déposera sur le Pi en §2.

---

## PHASE 1 — Préparer le Pi de référence (« doré »), une fois

### 1.1 — Flasher l'OS

Avec Raspberry Pi Imager : **Raspberry Pi OS Lite (64-bit)**, base **Bookworm**.
Dans les options avancées de l'Imager, fixer : utilisateur `pi`, activer SSH (mot de passe ou clé), hostname `photobooth`. **Ne pas** y configurer le Wi-Fi de la GoPro — il sera géré par le provisioning (§3) pour rester éditable sur le terrain.

Premier boot, en SSH, vérifier l'architecture :

```bash
uname -m      # doit afficher: aarch64
```

### 1.2 — Paquets système

```bash
sudo apt update
sudo apt install -y \
  gpiod libgpiod3 \
  i2c-tools \
  libgbm1 libgl1-mesa-dri libegl1 libegl-mesa0 libinput10 fontconfig
```

> RPi OS **Trixie/Debian 13** (base de `raspios_lite_arm64_latest`) : `libgpiod2` est devenu `libgpiod3` et `libegl1-mesa` (transitionnel) a disparu → `libegl1 libegl-mesa0`. Sur une base Bookworm/antérieure, gardez `libgpiod2` / `libegl1-mesa`.

(Capteur de lumière optionnel uniquement : `sudo raspi-config` → Interface Options → I2C → Enable, puis `sudo i2cdetect -y 1` doit montrer `0x4a`.)

Sanity check de la pile DRM **avant** .NET :

```bash
sudo apt install -y kmscube && kmscube   # cube animé attendu, puis Ctrl+C
```

### 1.3 — Permissions service non-root

```bash
sudo usermod -aG gpio,i2c,video,input,render,tty pi
```

---

## PHASE 2 — Déposer le binaire .NET sur le Pi

Depuis Windows, copier le `publish/` produit en §0.4 :

```powershell
scp -r publish pi@photobooth:/home/pi/photobooth
ssh pi@photobooth "chmod +x /home/pi/photobooth/Photobooth.App"
```

Test manuel en kiosk FBDev. **Voir d'abord la section « Rendu » ci-dessous** : sur Pi 3, FBDev est le backend logiciel le plus prévisible.

```bash
# Chemin recommandé image turnkey :
/home/pi/photobooth/Photobooth.App --fbdev
# Comparaison DRM uniquement si besoin :
LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe /home/pi/photobooth/Photobooth.App --drm
```

La borne doit s'afficher plein écran, FPS correct, RAM stable (`free -m`, pas de montée continue).

#### Rendu (à valider impérativement sur le Pi 3 avant de figer l'image)

- `AVALONIA_RENDERER=software` était une variable d'**Avalonia 0.10** : elle est **ignorée en Avalonia 11** (no-op). Ne pas s'y fier.
- D'après les mainteneurs Avalonia, le backend **DRM (`StartLinuxDrm` / `--drm`) est *toujours* accéléré matériellement** ; le backend **FBDev est *toujours* logiciel**.
- **Donc le service turnkey lance `--fbdev` par défaut**. `--drm` reste disponible pour comparaison, mais ne doit pas être le défaut tant que le Pi 3 affiche un écran noir/EGL.

---

## PHASE 3 — Couche système : provisioning, config FAT32, systemd durci

### 3.1 — Créer le dossier de config éditable sur la FAT32 (avec modèles pré-créés)

> **Bookworm = `/boot/firmware/`**, pas `/boot/`. Les vieux tutos qui disent `/boot/` sont faux ici.
> Les fichiers sont **pré-créés** : l'opérateur **édite**, il ne crée jamais (évite le piège de l'extension cachée `.txt` sous Windows).

```bash
sudo mkdir -p /boot/firmware/photobooth
```

`/boot/firmware/photobooth/wifi.txt` :

```ini
# Reseau Wi-Fi de la GoPro. Editer puis rebrancher la borne.
GOPRO_SSID=GP12345678
GOPRO_PASSWORD=motdepasse-de-ma-gopro
WIFI_COUNTRY=FR
```

> **Clés exactes** : `GOPRO_SSID` / `GOPRO_PASSWORD` (c'est ce que lit `photobooth-provision.sh`). Le modèle complet, avec réseau secondaire optionnel, est dans `deploy/boot-config/wifi.txt`.

`/boot/firmware/photobooth/photobooth.json` :

```jsonc
{
  "Theme": {
    "Names": "Camille & Yann",
    "Year": "2026",
    "BackgroundImage": "/boot/firmware/photobooth/fond.jpg"
  },
  "Gopro": {
    // "http" = vraie GoPro (jour J) ; "fake" = mode démo sans GoPro (tests).
    "Mode": "http"
  }
}
```

> Le parser de config .NET tolère les commentaires `//` et les virgules finales. Modèle versionné : `deploy/boot-config/photobooth.json`.

`/boot/firmware/photobooth/fond.jpg` : déposer une image de fond modèle (l'opérateur la remplacera).

`/boot/firmware/photobooth/admin.txt` (**avancé, mainteneur**) : override **optionnel** du mot de passe SSH du compte `pi`. Le mot de passe baké à la fabrication n'est qu'un **défaut** (`raspberry`) ; décommenter `PI_PASSWORD=...` ici le change **par carte**, sans rebuild. `photobooth-provision.sh` le réapplique à **chaque boot** : c'est volontaire, car sous overlay un `passwd` en SSH ne persiste pas. L'opérateur d'événement n'y touche pas.

**Mode démo (test sans GoPro)** : il n'y a **pas** de fichier-sentinelle `MODE_DEMO.txt` — aucun code ne le lit (vérifié dans `ServiceConfiguration.cs`). Le mode démo se règle par `"Gopro": { "Mode": "fake" }` dans `photobooth.json`. L'opérateur remet `"http"` avant le vrai événement.

### 3.2 — Service de provisioning Wi-Fi (oneshot, idempotent, à chaque boot)

> Le Wi-Fi relève de NetworkManager (niveau OS), pas de la config .NET — d'où ce `.txt` + script, et non un `photobooth.json`.

Le script complet (commenté, lit `GOPRO_SSID`/`GOPRO_PASSWORD`, gère le réseau secondaire optionnel, `set -u` pour ne jamais bloquer le boot) est versionné dans **`deploy/photobooth-provision.sh`**, et son unit dans **`deploy/systemd/photobooth-provision.service`**. Installation :

```bash
sudo cp deploy/photobooth-provision.sh /usr/local/sbin/photobooth-provision.sh
sudo chmod 755 /usr/local/sbin/photobooth-provision.sh
sudo cp deploy/systemd/photobooth-provision.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable photobooth-provision.service
```

Points clés : `tr -d '\r'` neutralise les CRLF Windows ; `autoconnect-priority 100` fait gagner la GoPro ; `autoconnect-retries 0` = réessais **infinis** (GoPro rallumée en cours de soirée → reconnexion seule). ⚠️ Vérifier que `photobooth-provision.sh` reste en fins de ligne **LF** (Unix), sinon le `#!/bin/bash` casse.

### 3.3 — Service kiosk durci

`/etc/systemd/system/photobooth.service` :

```ini
[Unit]
Description=Photobooth kiosk
After=multi-user.target photobooth-provision.service
Conflicts=getty@tty1.service

[Service]
Type=simple
User=pi
SupplementaryGroups=gpio i2c video input render
WorkingDirectory=/home/pi/photobooth
ExecStart=/home/pi/photobooth/Photobooth.App --fbdev
# Backend logiciel par défaut sur Pi 3 (voir section « Rendu » en §2).
# NE PAS utiliser AVALONIA_RENDERER=software : no-op en Avalonia 11.
Environment=LIBGL_ALWAYS_SOFTWARE=1
Environment=GALLIUM_DRIVER=llvmpipe
Environment=DOTNET_GCHeapHardLimit=0x18000000
Restart=always
RestartSec=3
StandardInput=tty
TTYPath=/dev/tty1
TTYReset=yes
TTYVTDisallocate=yes

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl daemon-reload
sudo systemctl enable photobooth.service
```

Masquer le getty pour libérer tty1 et éviter un curseur de login derrière le kiosk :

```bash
sudo systemctl set-default multi-user.target
sudo systemctl disable getty@tty1.service
sudo systemctl mask getty@tty1.service
```

- `Restart=always` + `RestartSec=3` : tout crash se soigne en 3 s — principal levier de stabilité.
- `DOTNET_GCHeapHardLimit=0x18000000` (~384 Mo) : plafonne le tas .NET (Pi 1 Go).
- L'app coupe **toujours** la lumière à l'arrêt (fin normale, exception, SIGTERM).

### 3.4 — Optimisation boot + robustesse

Désactiver le swap (le swap sur carte SD la tue) :

```bash
sudo dphys-swapfile swapoff
sudo dphys-swapfile uninstall
sudo systemctl disable dphys-swapfile
```

Boot silencieux. Dans `/boot/firmware/cmdline.txt` (une seule ligne, ajouter en fin) :

```
quiet splash loglevel=3 logo.nologo vt.global_cursor_default=0
```

Dans `/boot/firmware/config.txt` :

```
disable_splash=1
dtoverlay=disable-bt
```

Désactiver les services inutiles (gain RAM + boot) :

```bash
sudo systemctl disable bluetooth hciuart ModemManager avahi-daemon triggerhappy NetworkManager-wait-online.service
```

> `NetworkManager-wait-online` est important à désactiver : sans Internet (réseau GoPro), il ferait traîner le boot.

---

## PHASE 4 — Valider en conditions réelles (AVANT de figer)

Tant que cette phase n'est pas verte, **ne pas figer l'image**. Corriger sur le Pi, re-tester.

```bash
sudo nmcli dev wifi connect "<SSID_GoPro>" password "<motdepasse>"   # ou via wifi.txt + reboot
curl -s http://10.5.5.9:8080/gp/gpMediaList | head -c 200            # la GoPro repond
systemd-analyze blame                                                # temps de boot
free -m                                                              # RAM stable dans le temps
```

Checklist d'endurance :

- [ ] Reboot → la borne arrive seule en plein écran, bons noms/année/fond (depuis `photobooth.json` de la FAT32).
- [ ] `wifi.txt` édité depuis un PC + reboot → la borne se connecte à la nouvelle GoPro.
- [ ] Bouton photo → séquence + vraie photo affichée. Double-appui rapide → **une seule** photo (anti-rebond).
- [ ] Bouton vidéo → `Rec`, arrêt auto ~10 s. Lumière (GPIO 17) ON/OFF pendant la capture.
- [ ] Couper l'alim de la GoPro en cours → bandeau orange/rouge, UI vivante, **reprise auto** quand la GoPro revient.
- [ ] `sudo systemctl kill photobooth` → redémarre seul en 3 s.
- [ ] Coupures de courant brutales répétées → pas d'écran noir persistant, pas de corruption.
- [ ] Mettre une valeur fausse dans `photobooth.json` → la borne démarre quand même (mode dégradé, pas de crash-loop). **Test du diff #2.**

---

## PHASE 5 — Figer, nettoyer, cloner

### 5.1 — Activer l'overlay FS (en dernier)

```bash
sudo raspi-config   # Performance Options -> Overlay File System -> Enable -> reboot
```

> Conséquence assumée : logs/photos locales en RAM → éphémères. C'est acceptable : les masters photo restent sur la carte SD de la GoPro. La config événement reste éditable car la FAT32 est **hors overlay**.

Vérifier après reboot que le système est bien en read-only (`mount | grep overlay`), puis **désactiver temporairement l'overlay** pour faire le nettoyage de clonage (sinon les modifs partent en RAM et sont perdues) :

```bash
sudo raspi-config   # Overlay File System -> Disable -> reboot
```

### 5.2 — Nettoyage des identifiants AVANT clonage (critique pour une flotte)

Sur le Pi, identité unique à neutraliser pour ne pas dupliquer des clés sur toutes les cartes :

```bash
# machine-id : VIDER, ne pas supprimer (un fichier vide est regenere au boot ;
# un fichier absent peut casser des services)
sudo truncate -s 0 /etc/machine-id
sudo rm -f /var/lib/dbus/machine-id
sudo ln -sf /etc/machine-id /var/lib/dbus/machine-id

# Clés d'hôte SSH : on les supprime ; RPi OS les régénère au 1er boot.
sudo rm -f /etc/ssh/ssh_host_*

# Connexion Wi-Fi de test (l'image ne doit pas embarquer un SSID de labo)
sudo nmcli connection delete photobooth-gopro 2>/dev/null || true
# Profil éventuellement laissé par Raspberry Pi Imager (customisation OS).
# Ceinture+bretelles : photobooth-provision.sh le purge aussi à chaque boot.
sudo nmcli connection delete preconfigured 2>/dev/null || true

# Purge logs / cache apt / historique
sudo rm -rf /home/pi/photobooth/logs/*
sudo journalctl --rotate && sudo journalctl --vacuum-time=1s
sudo apt clean
cat /dev/null > ~/.bash_history && history -c
```

> **Important** : vérifier que `wifi.txt`, `photobooth.json` et `fond.jpg` modèles sont bien **présents** dans `/boot/firmware/photobooth/` (couche éditable livrée), mais avec des valeurs neutres/modèles, pas les identifiants d'un vrai événement de test.

### 5.3 — Réactiver l'overlay puis arrêter proprement

```bash
sudo raspi-config   # Overlay File System -> Enable -> reboot
sudo shutdown -h now
```

Retirer la carte SD du Pi, l'insérer dans le PC Windows.

### 5.4 — Cloner l'image (deux options)

**Option A — WSL2 / Linux (`dd`)** — identifier le disque puis cloner :

```bash
lsblk                       # reperer la carte (ex. /dev/sdX)
sudo dd if=/dev/sdX of=photobooth-golden.img bs=4M status=progress conv=fsync
```

**Option B — Windows** : Win32DiskImager → « Read » → écrire `photobooth-golden.img`.

### 5.5 — Réduire + sécuriser avec PiShrink (sous WSL2/Linux)

```bash
wget https://raw.githubusercontent.com/Drewsif/PiShrink/master/pishrink.sh
chmod +x pishrink.sh
sudo ./pishrink.sh photobooth-golden.img photobooth-dist.img
```

- L'**auto-expand** de la partition au premier boot (remplit la carte cible quelle que soit sa taille) est le **comportement par défaut** de PiShrink ; `-s` le désactiverait.
- Les **clés d'hôte SSH** sont régénérées au premier boot par Raspberry Pi OS (service `regenerate-ssh-host-keys`), pas par PiShrink. ⚠️ PiShrink n'a **pas** de flag `-c` (getopts `":adnhrsvzZ"`) : le lui passer le fait sortir en erreur.
- PiShrink compresse en `.img.xz` si on ajoute `-Z` (xz) ou `-z` (gzip) ; `-a` parallélise la compression. Pour distribuer :

```bash
sudo ./pishrink.sh -aZ photobooth-golden.img photobooth-dist.img
# produit photobooth-dist.img.xz
```

L'artefact distribuable est **`photobooth-dist.img.xz`**.

---

## PHASE 6 — Distribuer

- Flasher `photobooth-dist.img.xz` directement avec Raspberry Pi Imager (il décompresse le `.xz` à la volée) ou Balena Etcher, sur les cartes à livrer ; ou livrer les cartes déjà flashées.
- ⚠️ **Avec Raspberry Pi Imager : répondre « Non » à la personnalisation de l'OS** (sinon profil `preconfigured` injecté → fuite Wi-Fi + override). Etcher n'a pas ce piège.
- Le **pas-à-pas complet d'installation** (flashage détaillé, premier démarrage, config, dépannage), destiné à celui qui installe une borne, est dans **[`INSTALLATION_BORNE.md`](INSTALLATION_BORNE.md)**.
- Joindre les **2 fiches plastifiées** destinées à l'opérateur (préparation à la maison / jour J + dépannage) — voir le guide opérateur, séparé de ce runbook.

---

## MISE À JOUR DE L'APP (cas courant, léger — sans re-cloner)

Tant que **l'OS ne change pas**, une nouvelle version de l'app = remplacer `publish/`, pas refaire l'image.

1. Sous Windows, rebuild :

   ```powershell
   dotnet publish src\Photobooth.App\Photobooth.App.csproj -c Release `
     -r linux-arm64 --self-contained true -p:PublishReadyToRun=true -o publish
   ```

2. Sur le Pi cible, **désactiver l'overlay** (sinon la copie part en RAM et disparaît au reboot) :

   ```bash
   sudo raspi-config   # Overlay File System -> Disable -> reboot
   ```

3. Copier le nouveau binaire et l'arrêter/relancer :

   ```bash
   sudo systemctl stop photobooth
   scp -r publish pi@photobooth:/home/pi/photobooth        # depuis Windows
   ssh pi@photobooth "chmod +x /home/pi/photobooth/Photobooth.App"
   ```

4. Tester (`--drm`), puis **réactiver l'overlay** :

   ```bash
   sudo raspi-config   # Overlay File System -> Enable -> reboot
   ```

5. Si la mise à jour doit être propagée à la flotte : repasser par PHASE 5 (nettoyage + clone + PiShrink) pour produire un nouveau `.img.xz`. Sinon, mise à jour unité par unité par la procédure ci-dessus.

> **À prévoir (amélioration future)** : placer `publish/` sur une zone hors overlay rafraîchie au boot, pour mettre à jour l'app sans le cycle désactiver/réactiver overlay.

---

## CHECKLIST « IMAGE VALIDÉE » (à cocher avant de figer / distribuer)

**Code**
- [ ] Diff #1 appliqué : `photobooth.json` externe chargé depuis `/boot/firmware/photobooth/` (override de `PHOTOBOOTH_CONFIG_DIR` respecté).
- [ ] Diff #2 appliqué : config invalide → log + démarrage dégradé, **jamais** de `return 2`.
- [ ] (Recommandé) Bandeau statut GoPro vert/orange/rouge fonctionnel.
- [ ] Publish `linux-arm64` self-contained, trimming OFF, ReadyToRun ON.

**Système**
- [ ] `uname -m` = `aarch64`.
- [ ] `kmscube` OK ; kiosk plein écran stable (rendu validé : `--drm` seul, ou `LIBGL_ALWAYS_SOFTWARE=1`+`GALLIUM_DRIVER=llvmpipe`, ou repli FBDev), FPS correct, pas d'écran noir.
- [ ] `photobooth.service` enable, durci (Conflicts getty, TTYReset/VTDisallocate), Restart=always.
- [ ] getty@tty1 disabled + masked ; default target = multi-user.
- [ ] `photobooth-provision.service` enable ; `wifi.txt` appliqué au boot (CRLF gérés, retry infini).
- [ ] Swap OFF ; boot silencieux (cmdline/config) ; bluetooth/avahi/ModemManager/triggerhappy/NetworkManager-wait-online disabled.

**Couche éditable (FAT32)**
- [ ] `/boot/firmware/photobooth/` contient `wifi.txt`, `photobooth.json`, `fond.jpg` **modèles** (valeurs neutres, pas d'identifiants d'un vrai événement).
- [ ] `photobooth.json` modèle en `"Gopro": { "Mode": "http" }` (pas laissé en `"fake"`).
- [ ] Les fichiers sont éditables depuis l'explorateur Windows/Mac une fois la carte insérée.

**Endurance (Phase 4 verte)**
- [ ] Reboot → borne seule, bons thème/fond. `wifi.txt` modifié + reboot → connexion à la nouvelle GoPro.
- [ ] Photo/vidéo OK (+ lumière si `LightEnabled=true`) ; double-appui = une seule photo.
- [ ] GoPro coupée → bandeau orange/rouge, reprise auto.
- [ ] `systemctl kill` → relance en 3 s ; coupures brutales répétées → pas de corruption.
- [ ] `photobooth.json` volontairement faux → démarre quand même (mode dégradé).

**Avant clonage**
- [ ] Overlay activé puis vérifié, désactivé pour le nettoyage.
- [ ] `machine-id` vidé (truncate), `dbus machine-id` relié, clés SSH supprimées.
- [ ] Connexion Wi-Fi de test supprimée ; logs/journal/apt/bash_history purgés.
- [ ] Overlay réactivé, arrêt propre.

**Distribution**
- [ ] `dd`/Win32DiskImager → `photobooth-golden.img`.
- [ ] PiShrink `-aZ` → `photobooth-dist.img.xz` (auto-expand par défaut ; clés SSH régénérées par RPi OS au 1er boot).
- [ ] Test final : flasher une carte neuve avec le `.img.xz`, premier boot → borne opérationnelle sans aucune intervention terminal.

---

## §10 — CustoPiZer : fabrication reproductible (IMPLÉMENTÉ — méthode principale)

> **Statut** : implémenté dans **[`image-builder/`](image-builder/README.md)** +
> CI **[`.github/workflows/build-image.yml`](.github/workflows/build-image.yml)**.
> Dès lors qu'on **distribue à des tiers** (ou que le clone manuel dérive), c'est
> la voie à privilégier : la *fabrication* devient reproductible, traçable en Git,
> et **sans Pi physique**.

Principe : au lieu de cloner un Pi physique, on rejoue les PHASES 1-3 (et l'overlay
+ nettoyage de la PHASE 5) sous forme de **scripts versionnés**, dans un conteneur
Docker, en partant de l'image Lite officielle. Même architecture 2 couches, même
mécanisme FAT32. Éprouvé en production (OctoPi).

Ce qui est livré :

1. **`image-builder/scripts/00-photobooth.sh`** — porte §1.2, §1.3, §3.1-3.4 :
   création user `pi`, paquets, dépose `publish/`, unités systemd + provisioning,
   modèles FAT32, durcissement boot, et overlay FS derrière le flag
   `PHOTOBOOTH_OVERLAY` (image *dist* vs *dev*). Conversions `raspi-config nonint`
   incluses (pas d'interactif en chroot).
2. **`image-builder/build-local.sh`** — pipeline complet en local (WSL2/Linux + Docker).
3. **`.github/workflows/build-image.yml`** — `dotnet publish` → QEMU → CustoPiZer
   → PiShrink `-aZ` → artefact / Release. **Aucun secret embarqué** (modèles neutres).

Source de vérité unique = **`deploy/`** : la CI/le script y copient `deploy/` +
`publish/` dans `scripts/files/` le temps du build (zéro duplication).

> **⚠️ Deux invariants** (cf. `image-builder/README.md`) : (a) CustoPiZer remplace
> la *fabrication*, **pas la validation** — la PHASE 4 reste à faire sur un **Pi
> réel, par modèle** ; (b) **l'overlay dans le chroot** (initramfs sous QEMU) est
> le point à **dérisquer en premier** — le script le tente sans bloquer et trace
> l'échec éventuel. Le reste du runbook (config FAT32, mise à jour app, checklist)
> est inchangé.
