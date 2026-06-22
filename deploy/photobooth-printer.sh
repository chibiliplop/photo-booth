#!/usr/bin/env bash
# photobooth-printer.sh — détecte la première imprimante USB et crée la file CUPS
# "photobooth-printer". Lancé en root au démarrage par photobooth-printer.service.
# Idempotent : si la file existe déjà, ne fait rien.
set -euo pipefail

QUEUE="photobooth-printer"
log() { echo "[printer-setup] $*"; }

# Attend que CUPS réponde (max 20 s).
for i in $(seq 1 10); do
    lpstat -r >/dev/null 2>&1 && break
    sleep 2
done
if ! lpstat -r >/dev/null 2>&1; then
    log "CUPS non disponible après 20 s — abandon."
    exit 0
fi

# File déjà configurée ?
if lpstat -p "$QUEUE" >/dev/null 2>&1; then
    log "File '$QUEUE' déjà présente — rien à faire."
    exit 0
fi

# Détecte la première imprimante USB connectée.
USB_URI=$(lpinfo -v 2>/dev/null | awk '/^direct usb:\/\// { print $2; exit }')
if [ -z "$USB_URI" ]; then
    log "Aucune imprimante USB détectée."
    exit 0
fi
log "URI USB : $USB_URI"

# Décode le nom de modèle depuis l'URI (usb://Vendor/Model%20Name -> Model Name).
RAW_MODEL=$(echo "$USB_URI" \
    | sed 's|.*/||; s|%20| |g; s|%2C|,|g; s|%28|(|g; s|%29|)|g; s|%2B|+|g; s|+| |g')
log "Modèle : $RAW_MODEL"

# Cherche un PPD gutenprint correspondant (deux passes : exact puis large).
find_ppd() {
    lpinfo -m 2>/dev/null | grep -i "$1" | awk 'NR==1{print $1}'
}
MODEL_SLUG=$(echo "$RAW_MODEL" | tr ' ' '-' | tr '[:upper:]' '[:lower:]')
PPD=$(find_ppd "$MODEL_SLUG")
if [ -z "$PPD" ]; then
    W1=$(echo "$RAW_MODEL" | awk '{print $1}')
    W2=$(echo "$RAW_MODEL" | awk '{print $2}')
    if [ -n "$W1" ] && [ -n "$W2" ]; then
        PPD=$(lpinfo -m 2>/dev/null | grep -i "$W1" | grep -i "$W2" | awk 'NR==1{print $1}') || true
    fi
fi

if [ -n "$PPD" ]; then
    log "PPD sélectionné : $PPD"
    lpadmin -p "$QUEUE" -E -v "$USB_URI" -m "$PPD"
    log "File '$QUEUE' créée avec succès."
else
    log "AVERTISSEMENT : aucun PPD trouvé pour '$RAW_MODEL' (gutenprint ne supporte pas ce modèle)."
    log "Printer.Type=cups ne fonctionnera pas sans driver. Installez le driver puis relancez ce service."
    log "File NON créée."
    exit 0
fi
