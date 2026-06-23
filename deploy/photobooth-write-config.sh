#!/usr/bin/env bash
# Écrit photobooth.json sur la FAT32 (root) de façon atomique (temp + rename), depuis stdin.
# Appelé en root via sudo par l'hôte admin (pi) quand l'écriture directe est refusée (overlay/FAT32 root).
# La FAT n'a pas de journal -> temp + rename pour résister à une coupure secteur (§14.1).
set -euo pipefail

DEST="/boot/firmware/photobooth/photobooth.json"
DIR="$(dirname "$DEST")"
mkdir -p "$DIR"

TMP="$(mktemp "$DIR/.photobooth.json.XXXXXX")"
cat > "$TMP"            # stdin -> fichier temporaire
sync "$TMP" 2>/dev/null || true
mv -f "$TMP" "$DEST"    # rename atomique sur le même volume
sync "$DIR" 2>/dev/null || true
echo "config écrite: $DEST"
