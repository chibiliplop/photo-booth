#!/usr/bin/env bash
#
# 00-photobooth.sh — personnalisation CustoPiZer de l'image borne photo
# =============================================================================
# Exécuté EN ROOT, dans le chroot QEMU/ARM monté par CustoPiZer sur l'image
# Raspberry Pi OS Lite 64-bit (Bookworm). Reproduit, de façon REPRODUCTIBLE et
# versionnée, ce que le RUNBOOK fait à la main sur le « Pi doré » :
#   - §1.2  paquets système
#   - §1.3  utilisateur + permissions service non-root
#   - PHASE 2  dépose du binaire .NET (publish/)
#   - §3.1  dossier de config éditable sur la FAT32 (modèles)
#   - §3.2  service de provisioning Wi-Fi (oneshot, à chaque boot)
#   - §3.3  service kiosk durci + libération de tty1
#   - §3.4  optimisation boot + robustesse
#   - PHASE 5  overlay FS (en option, derrière un flag) + nettoyage d'identité
#
# Ce que ce script NE fait PAS (et ne PEUT pas faire sous émulation) : la
# Phase 4 du runbook (endurance sur vrai matériel : rendu DRM/GPU, FPS, GPIO,
# Wi-Fi GoPro, coupures secteur). Cette validation reste à faire sur un Pi réel,
# PAR MODÈLE que l'on veut certifier. CustoPiZer remplace la FABRICATION, pas la
# VALIDATION.
#
# Entrées (montées par CustoPiZer / passées par la CI) :
#   /files/deploy/   = copie du dossier deploy/ du dépôt (units, provisioning, modèles)
#   /files/publish/  = sortie de `dotnet publish -r linux-arm64 --self-contained`
#
# Variables d'environnement (passées via `docker run -e ...`) :
#   PHOTOBOOTH_OVERLAY  1|true|yes (défaut) = active l'overlay read-only (image « dist »)
#                       0|false|no          = laisse le root inscriptible (image « dev »)
#   PI_PASSWORD         mot de passe du compte pi (défaut: raspberry — À CHANGER)
#   PI_SSH              1|true (défaut) = active SSH ; 0 = désactive
# =============================================================================

set -euo pipefail

# common.sh de CustoPiZer fournit echo_green / echo_red (si dispo).
if [ -f /common.sh ]; then
    # shellcheck disable=SC1091
    source /common.sh || true
fi
say()  { echo "[photobooth] $*"; }
warn() { echo "[photobooth] AVERTISSEMENT: $*" >&2; }
die()  { echo "[photobooth] ERREUR FATALE: $*" >&2; exit 1; }

PI_PASSWORD="${PI_PASSWORD:-raspberry}"

case "${PHOTOBOOTH_OVERLAY:-1}" in
    1|true|yes|on|TRUE|Yes|On) WANT_OVERLAY=1 ;;
    *)                         WANT_OVERLAY=0 ;;
esac
case "${PI_SSH:-1}" in
    1|true|yes|on|TRUE|Yes|On) WANT_SSH=1 ;;
    *)                         WANT_SSH=0 ;;
esac

# -----------------------------------------------------------------------------
# 0. Garde-fous : les sources doivent être présentes, et la partition FAT (boot)
#    doit être réellement montée dans le chroot. config.txt est LE marqueur
#    certain de la FAT (il y est toujours) : on déduit BOOT_DIR de sa présence,
#    pour que la couche éditable atterrisse bien sur la FAT (qui devient
#    /boot/firmware au runtime), jamais par erreur sur l'ext4.
# -----------------------------------------------------------------------------
[ -d /files/deploy ]  || die "/files/deploy absent — la CI doit copier deploy/ dans scripts/files/."
[ -d /files/publish ] || die "/files/publish absent — lancer 'dotnet publish' et le copier dans scripts/files/."
[ -x /files/publish/Photobooth.App ] || [ -f /files/publish/Photobooth.App ] \
    || die "/files/publish/Photobooth.App introuvable — publish incomplet ?"

if   [ -f /boot/firmware/config.txt ]; then BOOT_DIR=/boot/firmware
elif [ -f /boot/config.txt ];          then BOOT_DIR=/boot
else die "Partition boot/FAT non montée dans le chroot (config.txt introuvable). Vérifier le montage CustoPiZer."
fi
say "Partition boot/FAT détectée : $BOOT_DIR"

export DEBIAN_FRONTEND=noninteractive

# -----------------------------------------------------------------------------
# 1.3 — Utilisateur 'pi' + permissions service non-root
#       (Bookworm Lite ne livre plus d'utilisateur par défaut : on le crée.)
# -----------------------------------------------------------------------------
say "Configuration de l'utilisateur 'pi'."
if ! id -u pi >/dev/null 2>&1; then
    useradd -m -d /home/pi -s /bin/bash pi
fi
echo "pi:${PI_PASSWORD}" | chpasswd
# Ce mot de passe n'est qu'un DÉFAUT : chaque carte peut le surcharger via
# /boot/firmware/photobooth/admin.txt (réappliqué à chaque boot, persiste sous
# overlay). Inutile donc d'en faire un secret CI.
say "Mot de passe pi (défaut) = '${PI_PASSWORD}'. Surchargeable par carte via admin.txt."

# N'ajoute que les groupes qui existent (gpio/render/i2c arrivent avec les paquets).
for grp in sudo gpio i2c video input render tty spi; do
    if getent group "$grp" >/dev/null 2>&1; then
        usermod -aG "$grp" pi || warn "Ajout du groupe '$grp' échoué."
    fi
done

# -----------------------------------------------------------------------------
# 1.2 — Paquets système (pile DRM/GL logicielle + GPIO + I2C + polices)
# -----------------------------------------------------------------------------
say "Installation des paquets système."
apt-get update
# NB noms de paquets (RPi OS Trixie / Debian 13, base de raspios_lite_arm64_latest) :
#   - libgpiod2  -> libgpiod3       (bump ABI libgpiod 1.x -> 2.x ; compatible
#                                     avec System.Device.Gpio 3.2.0 qui sait
#                                     piloter libgpiod.so.3 / API v2).
#   - libegl1-mesa (paquet transitionnel, SUPPRIMÉ depuis Bookworm) -> on installe
#                  le loader GLVND 'libegl1' + l'implémentation Mesa 'libegl-mesa0'.
# Un paquet introuvable fait sortir apt en code 100 et casse tout le build.
apt-get install -y --no-install-recommends \
    gpiod libgpiod3 \
    i2c-tools \
    libgbm1 libgl1-mesa-dri libegl1 libegl-mesa0 libinput10 fontconfig

# 'render' et 'gpio' peuvent n'apparaître qu'après l'install : on (re)tente l'ajout.
for grp in gpio render i2c; do
    getent group "$grp" >/dev/null 2>&1 && usermod -aG "$grp" pi || true
done

# -----------------------------------------------------------------------------
# PHASE 2 — Dépose du binaire .NET self-contained
# -----------------------------------------------------------------------------
say "Dépose du binaire .NET dans /home/pi/photobooth."
rm -rf /home/pi/photobooth
mkdir -p /home/pi/photobooth
cp -a /files/publish/. /home/pi/photobooth/
chmod +x /home/pi/photobooth/Photobooth.App
chown -R pi:pi /home/pi/photobooth

# -----------------------------------------------------------------------------
# 3.2 — Service de provisioning Wi-Fi (script + unit)
# -----------------------------------------------------------------------------
say "Installation du provisioning Wi-Fi."
install -m 0755 /files/deploy/photobooth-provision.sh /usr/local/sbin/photobooth-provision.sh
# Sécurité fins de ligne : le shebang casse en CRLF. On force LF.
sed -i 's/\r$//' /usr/local/sbin/photobooth-provision.sh
install -m 0644 /files/deploy/systemd/photobooth-provision.service /etc/systemd/system/

# -----------------------------------------------------------------------------
# 3.3 — Service kiosk durci
# -----------------------------------------------------------------------------
say "Installation du service kiosk."
install -m 0644 /files/deploy/systemd/photobooth.service /etc/systemd/system/

# Active les unités (offline : systemctl enable crée les symlinks sans bus ;
# repli manuel si la commande refuse dans le chroot).
enable_unit() {
    local unit="$1"
    if systemctl enable "$unit" >/dev/null 2>&1; then
        say "Unité activée : $unit"
    else
        ln -sf "/etc/systemd/system/$unit" \
            "/etc/systemd/system/multi-user.target.wants/$unit"
        say "Unité activée (symlink manuel) : $unit"
    fi
}
mkdir -p /etc/systemd/system/multi-user.target.wants
enable_unit photobooth-provision.service
enable_unit photobooth.service

# Libère tty1 : multi-user par défaut, getty@tty1 désactivé + masqué.
systemctl set-default multi-user.target >/dev/null 2>&1 || \
    ln -sf /lib/systemd/system/multi-user.target /etc/systemd/system/default.target
systemctl disable getty@tty1.service >/dev/null 2>&1 || true
systemctl mask    getty@tty1.service >/dev/null 2>&1 || \
    ln -sf /dev/null /etc/systemd/system/getty@tty1.service

# Neutralise l'ASSISTANT DE PREMIÈRE CONFIGURATION de Raspberry Pi OS (Bookworm/
# Trixie). Sinon le 1er boot ouvre un wizard interactif sur tty1 qui réclame la
# création d'un utilisateur (username + mot de passe) AVANT de rendre la main au
# kiosk. L'utilisateur 'pi' existe déjà (cf. §1.3), on coupe donc le wizard :
#   1) userconf.txt sur la FAT = mécanisme OFFICIEL headless : présent, le
#      firstboot configure l'utilisateur SANS rien demander.
#   2) masquage du service interactif (ceinture + bretelles).
say "Neutralisation de l'assistant de première configuration (userconf)."
PB_PW_HASH=""
if command -v openssl >/dev/null 2>&1; then
    PB_PW_HASH="$(printf '%s' "$PI_PASSWORD" | openssl passwd -6 -stdin 2>/dev/null || true)"
fi
if [ -n "$PB_PW_HASH" ]; then
    printf 'pi:%s\n' "$PB_PW_HASH" > "$BOOT_DIR/userconf.txt"
    chmod 0644 "$BOOT_DIR/userconf.txt"
    say "userconf.txt écrit : utilisateur 'pi' pré-configuré, aucun prompt au 1er boot."
else
    warn "openssl absent : userconf.txt non écrit (on compte sur le masquage du service)."
fi
systemctl mask userconfig.service >/dev/null 2>&1 \
    || ln -sf /dev/null /etc/systemd/system/userconfig.service
systemctl disable userconfig.service >/dev/null 2>&1 || true
say "userconfig.service masqué."

# Activation SSH (maintenance). RPi OS régénère les clés d'hôte au 1er boot.
if [ "$WANT_SSH" = "1" ]; then
    systemctl enable ssh >/dev/null 2>&1 || systemctl enable sshd >/dev/null 2>&1 || \
        warn "Activation SSH non confirmée."
    say "SSH activé."
else
    systemctl disable ssh >/dev/null 2>&1 || true
    say "SSH désactivé (PI_SSH=0)."
fi

# -----------------------------------------------------------------------------
# 3.1 — Couche éditable sur la FAT32 (modèles pré-créés, valeurs NEUTRES)
#       L'opérateur ÉDITE, il ne crée jamais (piège de l'extension .txt cachée).
# -----------------------------------------------------------------------------
say "Dépose de la config éditable sur la FAT ($BOOT_DIR/photobooth)."
PB_CFG="$BOOT_DIR/photobooth"
mkdir -p "$PB_CFG"
install -m 0644 /files/deploy/boot-config/wifi.txt        "$PB_CFG/wifi.txt"
install -m 0644 /files/deploy/boot-config/photobooth.json "$PB_CFG/photobooth.json"
install -m 0644 /files/deploy/boot-config/LISEZ-MOI.txt   "$PB_CFG/LISEZ-MOI.txt"
install -m 0644 /files/deploy/boot-config/admin.txt       "$PB_CFG/admin.txt"
# Image de fond modèle : prend deploy/boot-config/fond.jpg si fourni, sinon
# dépose un placeholder 1x1 (l'opérateur remplacera). On n'échoue jamais ici.
if [ -f /files/deploy/boot-config/fond.jpg ]; then
    install -m 0644 /files/deploy/boot-config/fond.jpg "$PB_CFG/fond.jpg"
else
    # JPEG minimal valide (placeholder) encodé en base64.
    base64 -d > "$PB_CFG/fond.jpg" <<'B64' || warn "Placeholder fond.jpg non créé."
/9j/4AAQSkZJRgABAQEASABIAAD/2wBDAP//////////////////////////////////////////
////////////////////////////////////////////////////wgARCAABAAEDASIAAhEBAxEB
/8QAFAABAAAAAAAAAAAAAAAAAAAAAv/EABQBAQAAAAAAAAAAAAAAAAAAAAD/2gAMAwEAAhADEAAAAUf/
xAAUEAEAAAAAAAAAAAAAAAAAAAAA/9oACAEBAAEFAn//xAAUEQEAAAAAAAAAAAAAAAAAAAAA/9oACAED
AQE/AX//xAAUEQEAAAAAAAAAAAAAAAAAAAAA/9oACAECAQE/AX//xAAUEAEAAAAAAAAAAAAAAAAAAAAA
/9oACAEBAAY/An//xAAUEAEAAAAAAAAAAAAAAAAAAAAA/9oACAEBAAE/IX//2gAMAwEAAgADAAAAEPf/
xAAUEQEAAAAAAAAAAAAAAAAAAAAA/9oACAEDAQE/EH//xAAUEQEAAAAAAAAAAAAAAAAAAAAA/9oACAEC
AQE/EH//xAAUEAEAAAAAAAAAAAAAAAAAAAAA/9oACAEBAAE/EH//2Q==
B64
fi

# -----------------------------------------------------------------------------
# 3.4 — Optimisation boot + robustesse
# -----------------------------------------------------------------------------
say "Optimisation boot et robustesse."

# Swap OFF (le swap sur SD la tue). En chroot on ne peut pas swapoff : on
# désactive le service et on retire le paquet pour qu'aucun swapfile ne soit créé.
systemctl disable dphys-swapfile.service >/dev/null 2>&1 || true
apt-get -y purge dphys-swapfile >/dev/null 2>&1 || true

# Boot silencieux : append idempotent sur la ligne unique de cmdline.txt.
CMDLINE="$BOOT_DIR/cmdline.txt"
if [ -f "$CMDLINE" ]; then
    for opt in quiet splash loglevel=3 logo.nologo vt.global_cursor_default=0; do
        grep -qw -- "$opt" "$CMDLINE" || sed -i "1 s|\$| $opt|" "$CMDLINE"
    done
fi

# config.txt : append idempotent.
CONFIG_TXT="$BOOT_DIR/config.txt"
if [ -f "$CONFIG_TXT" ]; then
    grep -q '^disable_splash=1'      "$CONFIG_TXT" || echo 'disable_splash=1'      >> "$CONFIG_TXT"
    grep -q '^dtoverlay=disable-bt'  "$CONFIG_TXT" || echo 'dtoverlay=disable-bt'  >> "$CONFIG_TXT"
fi

# Services inutiles désactivés (gain RAM + boot). NetworkManager-wait-online est
# important : sans Internet (réseau GoPro) il ferait traîner le boot.
for svc in bluetooth hciuart ModemManager avahi-daemon triggerhappy \
           NetworkManager-wait-online.service; do
    systemctl disable "$svc" >/dev/null 2>&1 || true
done

# I2C (capteur de lumière optionnel). Non-bloquant.
if command -v raspi-config >/dev/null 2>&1; then
    raspi-config nonint do_i2c 0 >/dev/null 2>&1 || warn "Activation I2C non confirmée (optionnel)."
fi

# Hostname.
echo "photobooth" > /etc/hostname
sed -i 's/^127\.0\.1\.1.*/127.0.1.1\tphotobooth/' /etc/hosts 2>/dev/null || \
    echo -e "127.0.1.1\tphotobooth" >> /etc/hosts

# -----------------------------------------------------------------------------
# PHASE 5 — Nettoyage d'identité
#   « Image jamais bootée » => identité largement propre par construction.
#   On vide machine-id (régénéré au boot) par ceinture+bretelles. Les clés
#   d'hôte SSH sont (re)générées au 1er boot par RPi OS (regenerate-ssh-host-keys).
# -----------------------------------------------------------------------------
say "Nettoyage d'identité."
: > /etc/machine-id || true
rm -f /var/lib/dbus/machine-id || true
ln -sf /etc/machine-id /var/lib/dbus/machine-id || true
apt-get clean || true
rm -rf /var/lib/apt/lists/* || true

# -----------------------------------------------------------------------------
# PHASE 5 — Overlay FS (en DERNIER, derrière un flag)
#   ⚠️ POINT À DÉRISQUER EN PREMIER (cf. RUNBOOK / README) : l'overlay s'appuie
#   sur un initramfs ; sa génération (update-initramfs) sous QEMU est la chose
#   la plus susceptible d'échouer. On TENTE, on NE BLOQUE PAS le build, et on
#   trace clairement le résultat. Image « dev » (PHOTOBOOTH_OVERLAY=0) => root
#   inscriptible pour itérer la Phase 4 sur un vrai Pi.
# -----------------------------------------------------------------------------
if [ "$WANT_OVERLAY" = "1" ]; then
    say "Activation de l'overlay FS (read-only root)."
    if command -v raspi-config >/dev/null 2>&1 \
       && { raspi-config nonint do_overlayfs 0 >/dev/null 2>&1 \
            || raspi-config nonint enable_overlayfs >/dev/null 2>&1; }; then
        say "Overlay FS activé."
    else
        warn "OVERLAY NON APPLIQUÉ dans le chroot (update-initramfs/raspi-config a échoué)."
        warn "=> Valider/activer l'overlay sur un Pi réel, ou rebâtir une fois le pb résolu."
        echo "OVERLAY_NON_APPLIQUE" > /home/pi/photobooth/OVERLAY_STATUS.txt || true
    fi
else
    say "Overlay FS NON activé (PHOTOBOOTH_OVERLAY=0, image « dev »)."
fi

say "Personnalisation terminée."
