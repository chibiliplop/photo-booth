#!/usr/bin/env bash
# photobooth-printer.sh — détecte la première imprimante USB et crée la file CUPS
# "photobooth-printer". Lancé en root au démarrage par photobooth-printer.service.
# Idempotent : si la file existe déjà, on s'assure seulement qu'elle est active.
#
# Tests des fonctions de parsing : deploy/test/photobooth-printer-parse.test.sh

QUEUE="photobooth-printer"
log() { echo "[printer-setup] $*"; }

# --- Fonctions pures (testables : entrée via stdin/args, aucune dépendance CUPS) ---

# Lit une sortie `lpinfo -v` sur stdin et imprime l'URI USB à utiliser.
# Accepte les URI préfixées par un backend driver (ex. gutenprint53+usb://) AUSSI
# bien que les URI brutes usb://. C'est le correctif du bug : la Canon CP1300 est
# exposée comme `direct gutenprint53+usb://...`, que l'ancien motif `^direct usb://`
# ne matchait jamais. On préfère une URI déjà liée à un driver (…+usb://).
pick_usb_uri() {
    awk '
        $2 ~ /\+usb:\/\// { if (gut == "") gut = $2; next }
        $2 ~ /usb:\/\//   { if (raw == "") raw = $2 }
        END { print (gut != "" ? gut : raw) }
    '
}

# Déduit un nom de modèle lisible depuis une URI USB ($1).
#  - URI driver  gutenprint53+usb://canon-cp1300/SERIE -> host = modèle (canon cp1300)
#  - URI brute   usb://Canon/SELPHY%20CP1300?serial=.. -> vendor + modèle (sans ?serial=)
model_from_uri() {
    case "$1" in
        *+usb://*)
            printf '%s' "$1" | sed -E 's|^.*\+usb://([^/]+)/.*$|\1|' | tr '-' ' '
            ;;
        *)
            printf '%s' "$1" \
                | sed -E 's|^usb://||; s|\?.*$||; s|/| |g' \
                | sed 's|%20| |g; s|%2C|,|g; s|%28|(|g; s|%29|)|g; s|%2B|+|g; s|+| |g'
            ;;
    esac
}

# Lit une sortie `lpinfo -m` sur stdin et imprime le 1er PPD dont la ligne contient $1.
find_ppd() { grep -i -- "$1" | awk 'NR==1{print $1}'; }

# Sourcer le script (tests) charge les fonctions sans exécuter le flux ci-dessous.
if [ "${PHOTOBOOTH_PRINTER_LIB_ONLY:-}" = "1" ]; then return 0 2>/dev/null || exit 0; fi

# ----------------------------- Flux d'exécution ------------------------------
set -euo pipefail

# Lève un éventuel état "disabled"/"rejecting" persistant (erreur d'impression
# précédente). Best-effort : ne bloque jamais le boot.
ensure_ready() {
    cupsenable "$QUEUE" >/dev/null 2>&1 || true
    cupsaccept "$QUEUE" >/dev/null 2>&1 || true
}

# Attend que CUPS réponde (max 20 s).
for _ in $(seq 1 10); do
    lpstat -r >/dev/null 2>&1 && break
    sleep 2
done
if ! lpstat -r >/dev/null 2>&1; then
    log "CUPS non disponible après 20 s — abandon."
    exit 0
fi

# File déjà configurée ? On la réactive par précaution et on s'arrête.
if lpstat -p "$QUEUE" >/dev/null 2>&1; then
    log "File '$QUEUE' déjà présente — réactivation préventive."
    ensure_ready
    exit 0
fi

# Détecte l'imprimante USB (un seul appel : lpinfo -v est lent).
DEVICES=$(lpinfo -v 2>/dev/null || true)
USB_URI=$(printf '%s\n' "$DEVICES" | pick_usb_uri)
if [ -z "$USB_URI" ]; then
    log "Aucune imprimante USB détectée."
    exit 0
fi
log "URI USB : $USB_URI"

RAW_MODEL=$(model_from_uri "$USB_URI" | tr -s ' ' | sed 's|^ *||; s| *$||')
log "Modèle : $RAW_MODEL"

# Cherche un PPD/driver correspondant (une seule capture de lpinfo -m).
MODELS=$(lpinfo -m 2>/dev/null || true)
MODEL_SLUG=$(printf '%s' "$RAW_MODEL" | tr ' ' '-' | tr '[:upper:]' '[:lower:]')
PPD=$(printf '%s\n' "$MODELS" | find_ppd "$MODEL_SLUG" || true)

# Repli : recherche par les deux premiers mots du modèle (ex. "Canon CP1300").
if [ -z "$PPD" ]; then
    W1=$(printf '%s' "$RAW_MODEL" | awk '{print $1}')
    W2=$(printf '%s' "$RAW_MODEL" | awk '{print $2}')
    if [ -n "$W1" ] && [ -n "$W2" ]; then
        PPD=$(printf '%s\n' "$MODELS" | grep -i -- "$W1" | grep -i -- "$W2" \
            | awk 'NR==1{print $1}' || true)
    fi
fi

if [ -z "$PPD" ]; then
    log "AVERTISSEMENT : aucun PPD trouvé pour '$RAW_MODEL'."
    log "Installez le driver (ex. printer-driver-gutenprint) puis relancez ce service :"
    log "  sudo systemctl restart photobooth-printer"
    log "File NON créée."
    exit 0
fi

log "PPD sélectionné : $PPD"
if lpadmin -p "$QUEUE" -E -v "$USB_URI" -m "$PPD"; then
    ensure_ready
    log "File '$QUEUE' créée avec succès."
else
    log "ERREUR : lpadmin a échoué (URI=$USB_URI, PPD=$PPD). File NON créée."
    exit 0
fi
