#!/bin/bash
#
# photobooth-provision.sh  —  installer en  /usr/local/sbin/photobooth-provision.sh
# -----------------------------------------------------------------------------
# Provisionne le Wi-Fi de la borne au démarrage, AVANT le lancement de l'app.
#
# Lit  /boot/firmware/photobooth/wifi.txt  (édité par l'opérateur sous Windows,
# donc potentiellement en CRLF + espaces parasites) et crée/met à jour les
# profils NetworkManager de façon IDEMPOTENTE :
#   - GoPro    (obligatoire) : autoconnect, priorité HAUTE, réessais infinis.
#   - Wi-Fi 2  (optionnel)   : autoconnect, priorité plus basse.
#
# Format attendu de wifi.txt (clé=valeur, # = commentaire) :
#     GOPRO_SSID=GP12345678
#     GOPRO_PASSWORD=motdepasse-de-ma-gopro
#     # Réseau secondaire facultatif (ex. box maison pour les tests) :
#     #WIFI_SSID=Livebox-1234
#     #WIFI_PASSWORD=cleboxsecrete
#
# Lancé par photobooth-provision.service (oneshot) à CHAQUE boot.
# Logs : journalctl -u photobooth-provision
#
# NOTE : on utilise `set -u` mais PAS `set -e`. C'est un script « best-effort »
# qui ne doit JAMAIS empêcher la borne de démarrer : on log les échecs et on
# continue. La connexion réelle est de toute façon assurée par l'autoconnect
# de NetworkManager (réessais infinis), pas par le `nmcli up` immédiat.

set -u

readonly CONF="/boot/firmware/photobooth/wifi.txt"
readonly IFACE="wlan0"
readonly CONN_GOPRO="photobooth-gopro"
readonly CONN_WIFI2="photobooth-wifi2"
readonly CONN_IMAGER="preconfigured"   # profil éventuellement injecté par Raspberry Pi Imager
readonly PRIO_GOPRO=100   # la GoPro doit toujours gagner
readonly PRIO_WIFI2=10

log()  { echo "[photobooth-provision] $*"; }
warn() { echo "[photobooth-provision] AVERTISSEMENT: $*" >&2; }

# Nettoie une valeur : retire les CR (CRLF Windows), trim, ôte des guillemets entourants.
clean() {
    local v="$1"
    v="${v//$'\r'/}"
    v="${v#"${v%%[![:space:]]*}"}"   # trim gauche
    v="${v%"${v##*[![:space:]]}"}"   # trim droite
    if [[ "$v" == \"*\" || "$v" == \'*\' ]]; then v="${v:1:${#v}-2}"; fi
    printf '%s' "$v"
}

# Lit la valeur d'une clé (insensible à la casse) dans $CONF. Vide si absente.
read_key() {
    local key="$1" line rk rv
    while IFS= read -r line || [[ -n "$line" ]]; do
        line="${line//$'\r'/}"
        [[ -z "${line//[[:space:]]/}" ]] && continue       # ligne vide
        [[ "$line" =~ ^[[:space:]]*# ]] && continue          # commentaire
        [[ "$line" == *"="* ]] || continue
        rk="$(clean "${line%%=*}")"
        rv="${line#*=}"
        if [[ "${rk,,}" == "${key,,}" ]]; then clean "$rv"; return 0; fi
    done < "$CONF"
    printf '%s'
}

# Crée (ou recrée) un profil Wi-Fi de façon idempotente.
#   $1=nom de connexion  $2=ssid  $3=password  $4=priorité autoconnect
provision_wifi() {
    local conn="$1" ssid="$2" psk="$3" prio="$4"
    [[ -z "$ssid" ]] && { log "Profil '$conn' ignoré : SSID vide."; return 0; }

    # Idempotence : on efface l'ancien profil de ce nom puis on recrée.
    nmcli connection delete "$conn" >/dev/null 2>&1 || true

    log "Création du profil '$conn' (SSID='$ssid', priorité=$prio)."
    # autoconnect-retries 0 = réessais INFINIS (GoPro rallumée en soirée -> reconnexion seule).
    if [[ -n "$psk" ]]; then
        nmcli connection add type wifi con-name "$conn" ifname "$IFACE" ssid "$ssid" \
            wifi-sec.key-mgmt wpa-psk wifi-sec.psk "$psk" \
            connection.autoconnect yes \
            connection.autoconnect-priority "$prio" \
            connection.autoconnect-retries 0 \
            ipv4.method auto ipv6.method ignore >/dev/null 2>&1 \
            || warn "Échec de création du profil '$conn'."
    else
        warn "Profil '$conn' sans mot de passe : configuration en réseau OUVERT."
        nmcli connection add type wifi con-name "$conn" ifname "$IFACE" ssid "$ssid" \
            connection.autoconnect yes \
            connection.autoconnect-priority "$prio" \
            connection.autoconnect-retries 0 \
            ipv4.method auto ipv6.method ignore >/dev/null 2>&1 \
            || warn "Échec de création du profil '$conn'."
    fi
}

main() {
    # Garde : pas de fichier => rien à faire, sortie OK (jamais de blocage au boot).
    if [[ ! -f "$CONF" ]]; then
        log "Aucun fichier de configuration ($CONF) : rien à provisionner."
        exit 0
    fi
    log "Lecture de la configuration depuis $CONF"

    # --- Neutralise tout profil Wi-Fi injecté par Raspberry Pi Imager --------
    # La customisation OS d'Imager (custom.toml / firstrun, Bookworm) crée un
    # profil NetworkManager nommé "preconfigured". Ni ce script ni le nettoyage
    # de clonage (RUNBOOK §5.2) ne le ciblaient historiquement : il pouvait donc
    # survivre dans l'image distribuée (fuite du SSID/clé de fabrication) ou faire
    # camper la radio sur le mauvais réseau quand la GoPro est éteinte au boot.
    # La borne ne se connecte QUE via wifi.txt -> on le supprime à chaque boot,
    # quel que soit le mode de flashage de la carte.
    if nmcli -t -f NAME connection show 2>/dev/null | grep -Fxq "$CONN_IMAGER"; then
        log "Suppression du profil Wi-Fi '$CONN_IMAGER' (injecté par Raspberry Pi Imager)."
        nmcli connection delete "$CONN_IMAGER" >/dev/null 2>&1 || true
    fi

    local gopro_ssid gopro_psk wifi2_ssid wifi2_psk
    gopro_ssid="$(read_key GOPRO_SSID)"
    gopro_psk="$(read_key GOPRO_PASSWORD)"
    wifi2_ssid="$(read_key WIFI_SSID)"
    wifi2_psk="$(read_key WIFI_PASSWORD)"

    if [[ -n "$gopro_ssid" ]]; then
        provision_wifi "$CONN_GOPRO" "$gopro_ssid" "$gopro_psk" "$PRIO_GOPRO"
    else
        warn "GOPRO_SSID absent ou vide dans $CONF : aucun profil GoPro créé."
    fi

    if [[ -n "$wifi2_ssid" ]]; then
        provision_wifi "$CONN_WIFI2" "$wifi2_ssid" "$wifi2_psk" "$PRIO_WIFI2"
    else
        log "Aucun réseau secondaire (WIFI_SSID absent) : étape ignorée."
    fi

    # Tentative de connexion immédiate (réduit la fenêtre d'attente ; autoconnect rattrape sinon).
    if nmcli -t -f NAME connection show 2>/dev/null | grep -Fxq "$CONN_GOPRO"; then
        log "Tentative de connexion immédiate au réseau GoPro."
        nmcli connection up "$CONN_GOPRO" >/dev/null 2>&1 \
            || warn "Connexion GoPro non établie pour l'instant (autoconnect réessaiera)."
    fi

    log "Provisionnement terminé."
}

main "$@"
