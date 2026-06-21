# Déploiement sur Raspberry Pi 3 (kiosk photobooth) — install MANUELLE / dev

> **Quel document utiliser ?**
> - **Installer une borne à partir de l'image distribuable** (carte SD turnkey) →
>   [`INSTALLATION_BORNE.md`](INSTALLATION_BORNE.md) (flashage Pi Imager, etc.).
> - **Fabriquer l'image distribuable** (mainteneur) →
>   [`RUNBOOK_MAINTENEUR_CARTE_SD.md`](RUNBOOK_MAINTENEUR_CARTE_SD.md) + [`image-builder/`](image-builder/README.md).
> - **Ce document** = **mise en route MANUELLE** sur un Pi (copie `scp`, install à
>   la main, systemd) — utile pour le **développement / debug** ou comprendre les
>   briques. Ce n'est **pas** le chemin de distribution. Les valeurs « système »
>   (paquets, service, rendu) restent la référence ; pour une borne livrable,
>   préférez l'image et le runbook.

Ce guide déploie la nouvelle application **Photobooth.App** (.NET 8 / Avalonia) sur un Raspberry Pi 3 en mode kiosk plein écran, orientée **stabilité**. Il remplace l'ancienne app UWP (qui reste dans `CS/` à titre de référence).

> Résumé des choix (voir `LINUX_MIGRATION_BLOCKERS.md` et le plan) : **Raspberry Pi OS Lite 64-bit**, **rendu logiciel** (le GPU VC4 du Pi 3 est une source documentée de plantages EGL/écran noir), **self-contained** `linux-arm64`, **trimming désactivé**, service **systemd `Restart=always`**.

---

## 0. Pré-requis matériel

- Raspberry Pi 3 (1 Go RAM).
- Écran HDMI (la sortie kiosk).
- Boutons (et lumière **optionnelle**) câblés sur le **header BCM** — broches par défaut, modifiables dans la config (`Hardware`) :
  - bouton photo → **GPIO 18**
  - bouton vidéo → **GPIO 20**
  - sortie lumière → **GPIO 17** (active-high) — **optionnelle** : `Hardware.LightEnabled=false` désactive la lumière et la broche n'est jamais ouverte.
  - **Pull-up externe 10 kΩ recommandé** sur GPIO 18 et 20 (pull-up interne du BCM2837 peu fiable). Bouton entre la GPIO et GND.
- (Optionnel, désactivé par défaut) capteur MAX44009 sur I2C bus 1 (GPIO2/SDA, GPIO3/SCL), adresse `0x4A`.
- GoPro en Wi-Fi (réseau `10.5.5.9`).

---

## 1. Système

1. Flasher **Raspberry Pi OS Lite 64-bit** (Raspberry Pi Imager). Vérifier ensuite :
   ```bash
   uname -m      # doit afficher: aarch64
   ```
   (Si `armv7l`, c'est un OS 32-bit → publier en `linux-arm` au lieu de `linux-arm64`, voir §4.)

2. Paquets nécessaires :
   ```bash
   sudo apt update
   sudo apt install -y \
     gpiod libgpiod3 \
     i2c-tools \
     libgbm1 libgl1-mesa-dri libegl1 libegl-mesa0 libinput10 fontconfig
   ```
   - `gpiod`/`libgpiod3` : accès GPIO (caractère device `/dev/gpiochip*`). Sur RPi OS Bullseye/Bookworm, le paquet s'appelle encore `libgpiod2`.
   - `libgbm1 libgl1-mesa-dri libegl1 libegl-mesa0 libinput10` : pile d'affichage DRM/FBDev + entrées. Sur Bookworm/antérieur, `libegl1 libegl-mesa0` peut être remplacé par le paquet transitionnel `libegl1-mesa`.
   - `fontconfig` : la police est embarquée dans l'app, mais fontconfig aide au rendu texte.

3. Activer I2C (seulement si vous utilisez le capteur de lumière) :
   ```bash
   sudo raspi-config   # Interface Options -> I2C -> Enable   (ajoute dtparam=i2c_arm=on)
   sudo reboot
   sudo i2cdetect -y 1 # doit montrer 0x4a si le capteur est branché
   ```

4. Vérifier la pile d'affichage **avant** .NET (sanity check DRM) :
   ```bash
   sudo apt install -y kmscube && kmscube   # doit afficher un cube animé puis Ctrl+C
   ```

---

## 2. Permissions (service non-root)

Créer un utilisateur de service (ou réutiliser `pi`) et lui donner accès GPIO/I2C/affichage :
```bash
sudo usermod -aG gpio,i2c,video,input,render,tty pi
# se reconnecter (ou redémarrer le service) pour appliquer les groupes
```
Sur Raspberry Pi OS les règles udev (`/etc/udev/rules.d/99-com.rules`) assignent déjà `/dev/gpiochip*` au groupe `gpio` et `/dev/i2c-*` au groupe `i2c`. Sur une distro minimale, ajouter :
```
# /etc/udev/rules.d/99-gpio-i2c.rules
SUBSYSTEM=="gpio", KERNEL=="gpiochip*", GROUP="gpio", MODE="0660"
SUBSYSTEM=="i2c-dev", GROUP="i2c", MODE="0660"
```
puis `sudo udevadm control --reload-rules && sudo udevadm trigger`.

> Le DRM en mode kiosk a besoin du "DRM master" : le plus simple sur un appareil dédié est `tty1` + appartenance aux groupes `video`/`render`/`input`. Le service systemd ci-dessous lance l'app sur `tty1`.

---

## 3. Configuration de l'app

L'app lit `appsettings.json` (à côté de l'exécutable). Surcharge possible par variables d'environnement préfixées `PHOTOBOOTH_` (ex. `PHOTOBOOTH_Gopro__Mode=http`).

Réglages clés pour le Pi (extrait `appsettings.json`) :
```jsonc
{
  "Gopro":    { "Mode": "http", "ControlBaseUrl": "http://10.5.5.9", "MediaBaseUrl": "http://10.5.5.9:8080",
                "KeepAliveHost": "10.5.5.9", "KeepAlivePort": 8554, "CaptureDeadlineSeconds": 15 },
  "Hardware": { "Mode": "auto", "PhotoButtonPin": 18, "VideoButtonPin": 20, "LightEnabled": true, "LightPin": 17,
                "ButtonDebounceMs": 80, "LightSensorEnabled": false },
  "Timings":  { "PoseMs": 2000, "CountdownStepMs": 1000, "PhotoDisplayMs": 5000, "VideoCountdownSeconds": 3, "VideoMaxSeconds": 10 },
  "Theme":    { "Names": "Camille & Yann", "Year": "2020", "BackgroundImage": "avares://Photobooth.App/Assets/801x410_rick_et_morty_saison_4.jpg" }
}
```
- `Hardware.Mode=auto` : utilise le GPIO réel si `/dev/gpiochip0` existe, sinon repli **clavier** (mode démo). `linux` force le GPIO, `fake` force le mode clavier.
- `Hardware.LightEnabled=false` : borne **sans lumière** → la broche `LightPin` n'est jamais ouverte et les commandes lumière deviennent des no-ops (la borne fonctionne normalement). `PhotoButtonPin`/`VideoButtonPin`/`LightPin` sont validés au démarrage (0–27, pas de doublon) ; une valeur invalide affiche un **bandeau rouge** à l'écran au lieu de planter.
- `Gopro.Mode=http` parle à la vraie GoPro **ou** au simulateur Python (mettre `127.0.0.1`). `fake` = photos locales, sans réseau.
- **Thème par événement** : changer `Names`/`Year`/`BackgroundImage` (+ remplacer les assets) suffit à reconfigurer la borne.

---

## 4. Publier (depuis le poste de dev Windows/Mac/Linux)

`dotnet` croise-compile sans souci. Trimming **désactivé** (stabilité), ReadyToRun activé (démarrage plus rapide) :
```bash
# 64-bit (recommandé)
dotnet publish src/Photobooth.App/Photobooth.App.csproj -c Release \
  -r linux-arm64 --self-contained true -p:PublishReadyToRun=true -o publish

# 32-bit (seulement si uname -m = armv7l)
dotnet publish src/Photobooth.App/Photobooth.App.csproj -c Release \
  -r linux-arm --self-contained true -p:PublishReadyToRun=true -o publish
```
Copier le dossier `publish/` sur le Pi (ex. `/home/pi/photobooth`) et rendre l'exe exécutable :
```bash
scp -r publish pi@raspberrypi:/home/pi/photobooth
ssh pi@raspberrypi 'chmod +x /home/pi/photobooth/Photobooth.App'
```

### Lancer en kiosk FBDev (plein écran, sans bureau)
```bash
sudo /home/pi/photobooth/Photobooth.App --fbdev
```
`--fbdev` est le backend logiciel recommandé pour l'image turnkey sur Pi 3. Si vous voulez comparer avec DRM, gardez en tête que `--drm`/`StartLinuxDrm` est *toujours* accéléré matériellement en Avalonia 11 et peut donner un écran noir avec le GPU VC4. Dans ce cas seulement, testez :
```bash
LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe /home/pi/photobooth/Photobooth.App --drm
```

### Test rapide en fenêtre (si vous gardez le bureau)
```bash
/home/pi/photobooth/Photobooth.App --fullscreen
```

---

## 5. Démarrage automatique (systemd)

`/etc/systemd/system/photobooth.service` :
```ini
[Unit]
Description=Photobooth kiosk
After=multi-user.target

[Service]
Type=simple
User=pi
SupplementaryGroups=gpio i2c video input render
WorkingDirectory=/home/pi/photobooth
ExecStart=/home/pi/photobooth/Photobooth.App --fbdev
Environment=LIBGL_ALWAYS_SOFTWARE=1
Environment=GALLIUM_DRIVER=llvmpipe
Environment=DOTNET_GCHeapHardLimit=0x18000000
Restart=always
RestartSec=3
StandardInput=tty
TTYPath=/dev/tty1

[Install]
WantedBy=multi-user.target
```
```bash
sudo systemctl daemon-reload
sudo systemctl enable --now photobooth.service
journalctl -u photobooth -f        # suivre les logs
```
- `Restart=always` + `RestartSec=3` : tout crash se soigne en 3 s — **le principal levier de stabilité**.
- `DOTNET_GCHeapHardLimit=0x18000000` (~384 Mo) : plafonne le tas .NET pour éviter de faire swapper le Pi (1 Go) ; ajuster si besoin.
- L'app coupe **toujours** la lumière à l'arrêt (fin normale, exception ou SIGTERM).
- Logs applicatifs : `WorkingDirectory/logs/booth-*.log` (rolling, plafonné 5 Mo × 3).

---

## 6. Wi-Fi GoPro

Le Pi n'a qu'une interface Wi-Fi : connecté à la GoPro, il **n'a pas Internet** (c'est normal en exploitation). Connexion au réseau GoPro :
```bash
sudo nmcli dev wifi connect "<SSID_GoPro>" password "<motdepasse>"
# vérifier l'accès:
curl -s http://10.5.5.9:8080/gp/gpMediaList | head -c 200
```
Le keepalive UDP de l'app (toutes les 5 s) empêche la GoPro de se mettre en veille — il tourne sur sa propre boucle, indépendante des photos/vidéos.

---

## 7. Checklist de validation sur le Pi

**Phase 0 — spikes (à faire en premier, ils dé-risquent le reste) :**
- [ ] `kmscube` affiche un cube → la pile DRM fonctionne.
- [ ] `Photobooth.App --drm` (et au besoin `+ LIBGL_ALWAYS_SOFTWARE=1 GALLIUM_DRIVER=llvmpipe`) affiche la borne plein écran à un FPS correct, RAM stable (`free -m`, pas de montée continue), pas d'écran noir.
- [ ] `gpiodetect` montre `gpiochip0` ; appuyer sur les boutons déclenche bien la séquence (logs).
- [ ] (si `LightEnabled=true`) La lumière (GPIO 17) s'allume/s'éteint pendant la capture.
- [ ] (si capteur) `i2cdetect -y 1` montre `0x4a`.
- [ ] Mesurer le délai réel GoPro entre `Trigger` et disponibilité du fichier → ajuster `CaptureDeadlineSeconds`.

**Phase 4/5 — exploitation :**
- [ ] `systemctl enable --now photobooth` ; après `sudo systemctl kill photobooth`, le service **redémarre seul** (Restart=always) et revient plein écran.
- [ ] **Test d'acceptation stabilité** : laisser tourner 1–2 h ; couper l'alimentation de la GoPro en cours → l'UI reste vivante, statut « GoPro indisponible », puis **reprise auto** quand la GoPro revient ; aucune fuite mémoire ; la lumière n'est **jamais** restée allumée.
- [ ] Double-appui rapide sur le bouton photo → **une seule** photo.
- [ ] Vidéo : démarre, indicateur `Rec`, s'arrête seule après ~10 s.

---

## 8. Durcissement GPIO optionnel (si l'auto-détection pose souci)

L'app utilise `new GpioController()` (auto-détection), correct pour le **chip 0** d'un Pi 3. Sur Raspberry Pi OS Bookworm récent, si vous rencontrez des soucis de détection de chip, vous pouvez passer à `System.Device.Gpio` 4.x et un driver explicite `LibGpiodDriver(gpioChip: 0, version: LibGpiodDriverVersion.V2)` (voir `src/Photobooth.Adapters/Hardware/Linux/`). Ce n'est pas nécessaire par défaut sur un Pi 3.
