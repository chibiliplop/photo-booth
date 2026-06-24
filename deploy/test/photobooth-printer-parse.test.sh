#!/usr/bin/env bash
# Test des fonctions de parsing de photobooth-printer.sh.
# Aucune dépendance à CUPS : on source le script en mode "lib only" et on
# alimente les fonctions avec des sorties `lpinfo` factices.
#
#   bash deploy/test/photobooth-printer-parse.test.sh
set -uo pipefail

HERE=$(cd "$(dirname "$0")" && pwd)
# shellcheck disable=SC1091
PHOTOBOOTH_PRINTER_LIB_ONLY=1 . "$HERE/../photobooth-printer.sh"

fail=0
check() { # $1=libellé  $2=attendu  $3=obtenu
    if [ "$2" = "$3" ]; then
        echo "ok   - $1"
    else
        echo "FAIL - $1"
        echo "         attendu : [$2]"
        echo "         obtenu  : [$3]"
        fail=1
    fi
}

# --- Cas réel terrain : Canon CP1300 exposée UNIQUEMENT via gutenprint53+usb ---
# (c'est exactement le bug : aucune ligne `direct usb://`, seulement gutenprint.)
DEV_CP1300='file cups-brf:/
network beh
direct gutenprint53+usb://canon-cp1300/B220091014068235
network ipps
network ipp
network lpd'
uri=$(printf '%s\n' "$DEV_CP1300" | pick_usb_uri)
check "URI CP1300 (gutenprint)" "gutenprint53+usb://canon-cp1300/B220091014068235" "$uri"
check "modèle CP1300"           "canon cp1300" "$(model_from_uri "$uri")"

# --- Régression : l'ANCIEN motif ^direct usb:// ratait ce cas (doit rester vide) ---
old=$(printf '%s\n' "$DEV_CP1300" | awk '/^direct usb:\/\// { print $2; exit }')
check "ancien motif rate CP1300 (vide = bug d'origine)" "" "$old"

# --- Préférence gutenprint quand les deux formes sont présentes ---
DEV_BOTH='direct usb://Canon/SELPHY%20CP1300?serial=B22
direct gutenprint53+usb://canon-cp1300/B22'
check "préfère gutenprint si dispo" "gutenprint53+usb://canon-cp1300/B22" \
    "$(printf '%s\n' "$DEV_BOTH" | pick_usb_uri)"

# --- URI brute seule (repli) : on strippe bien le ?serial= ---
DEV_RAW='network beh
direct usb://Canon/SELPHY%20CP1300?serial=B22
network ipp'
uri2=$(printf '%s\n' "$DEV_RAW" | pick_usb_uri)
check "URI brute seule"          "usb://Canon/SELPHY%20CP1300?serial=B22" "$uri2"
check "modèle depuis URI brute"  "Canon SELPHY CP1300" "$(model_from_uri "$uri2")"

# --- Aucune imprimante USB ---
DEV_NONE='network beh
network ipp'
check "aucune USB -> vide" "" "$(printf '%s\n' "$DEV_NONE" | pick_usb_uri)"

# --- Sélection du PPD depuis `lpinfo -m` ---
MODELS_SAMPLE='drv:///sample.drv/generic.ppd Generic Text-Only
gutenprint.5.3://canon-cp1300/expert Canon SELPHY CP1300 - CUPS+Gutenprint v5.3.4
gutenprint.5.3://canon-cp910/expert Canon SELPHY CP910 - CUPS+Gutenprint v5.3.4'
check "PPD pour canon-cp1300" "gutenprint.5.3://canon-cp1300/expert" \
    "$(printf '%s\n' "$MODELS_SAMPLE" | find_ppd canon-cp1300)"
check "PPD introuvable -> vide" "" \
    "$(printf '%s\n' "$MODELS_SAMPLE" | find_ppd nexiste-pas)"

if [ "$fail" -eq 0 ]; then
    echo "--- TOUS LES TESTS PASSENT ---"
else
    echo "--- ECHECS DETECTES ---"
fi
exit "$fail"
