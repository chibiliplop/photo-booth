#!/usr/bin/env bash
#
# build-local.sh — fabrique l'image localement (WSL2 / Linux + Docker Desktop).
# Reproduit le pipeline CI à la main. Utile pour DÉRISQUER l'overlay (cf. README)
# avant de basculer sur GitHub Actions.
#
# Usage :
#   ./build-local.sh                      # image « dist » (overlay ON)
#   PHOTOBOOTH_OVERLAY=0 ./build-local.sh # image « dev » (root inscriptible)
#   PI_PASSWORD=secret ./build-local.sh
#
# Prérequis : dotnet SDK 8, docker, xz, curl, sudo (pour PiShrink).
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/.." && pwd)"
PHOTOBOOTH_OVERLAY="${PHOTOBOOTH_OVERLAY:-1}"
PI_PASSWORD="${PI_PASSWORD:-raspberry}"
IMG_XZ="$HERE/rpios-lite-arm64.img.xz"

echo "==> 1/5  dotnet publish (linux-arm64, self-contained, ReadyToRun)"
dotnet publish "$ROOT/src/Photobooth.App/Photobooth.App.csproj" -c Release \
    -r linux-arm64 --self-contained true -p:PublishReadyToRun=true \
    -o "$ROOT/publish"

echo "==> 2/5  Image Raspberry Pi OS Lite 64-bit (officielle)"
if [ ! -f "$HERE/input.img" ]; then
    [ -f "$IMG_XZ" ] || curl -fL -o "$IMG_XZ" \
        https://downloads.raspberrypi.com/raspios_lite_arm64_latest
    xz -dvk "$IMG_XZ"
    mv "${IMG_XZ%.xz}" "$HERE/input.img"
fi

echo "==> 3/5  Staging deploy/ + publish/ dans scripts/files/"
rm -rf "$HERE/scripts/files/deploy" "$HERE/scripts/files/publish"
cp -r "$ROOT/deploy"  "$HERE/scripts/files/deploy"
cp -r "$ROOT/publish" "$HERE/scripts/files/publish"

echo "==> 4/5  CustoPiZer (chroot QEMU/ARM) — overlay=$PHOTOBOOTH_OVERLAY"
docker run --rm --privileged \
    -v /dev:/dev \
    -v "$HERE":/CustoPiZer/workspace \
    -v "$HERE/config.local":/CustoPiZer/config.local \
    -e PHOTOBOOTH_OVERLAY="$PHOTOBOOTH_OVERLAY" \
    -e PI_PASSWORD="$PI_PASSWORD" \
    ghcr.io/octoprint/custopizer:latest

echo "==> 5/5  PiShrink -acZ (auto-expand + régénération clés SSH + xz)"
[ -f "$HERE/pishrink.sh" ] || curl -fL -o "$HERE/pishrink.sh" \
    https://raw.githubusercontent.com/Drewsif/PiShrink/master/pishrink.sh
chmod +x "$HERE/pishrink.sh"
sudo "$HERE/pishrink.sh" -acZ "$HERE/output.img" "$HERE/photobooth-dist.img"

echo
echo "OK -> $HERE/photobooth-dist.img.xz"
echo "Flasher avec Raspberry Pi Imager (SANS customisation OS) ou Balena Etcher."
