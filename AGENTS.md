# AGENTS.md

## Project Overview

This repository contains a Windows UWP photobooth application intended to run on Windows 10 IoT Core, typically on a Raspberry Pi connected to physical buttons, lighting control, and a GoPro over Wi-Fi.

The current product goal is: use a Raspberry Pi and a GoPro as an event photobooth. The Raspberry Pi handles the UI, GPIO inputs, and light output; the GoPro takes photos/videos and exposes media over its Wi-Fi HTTP API.

## Cross-platform migration (.NET 8 / Avalonia) — ACTIVE

A cross-platform rewrite now lives alongside the UWP code in `src/` (solution `Photobooth.sln`), targeting Linux/Windows/macOS with the Raspberry Pi 3 as the primary deployment. **New work should go here, not in the UWP projects.**

- `src/Photobooth.Core` — UI/hardware-free domain: interfaces, an actor-model `PhotoboothWorkflow` (states Idle/Capturing/Recording/Degraded), options, GoPro JSON models, bounded retry.
- `src/Photobooth.Adapters` — `HttpGoProClient`/`FakeGoProClient`; Linux GPIO/I2C (`System.Device.Gpio`) + fake hardware.
- `src/Photobooth.App` — Avalonia kiosk UI + composition root (DI/config/Serilog) + `appsettings.json`.
- `src/Photobooth.Tests` — xUnit workflow tests.

Quick start, simulator, and screenshot mode: `README_NET8.md`. Pi deployment (manuel): `DEPLOY_RASPBERRY_PI.md`. Testing without a camera: `TESTING_WITHOUT_GOPRO.md`. The legacy UWP app below (`CS/`, `GoProWifi/`, `RasberryPiLib/`) is kept as reference and is unchanged.

**Déploiement « turnkey » (plug-and-play non-tech)** : architecture 2 couches (image SD figée par le mainteneur / config événement éditable sur la partition FAT32 `/boot/firmware/photobooth/`). Kit prêt-à-copier dans `deploy/` (script de provisioning Wi-Fi, units systemd, modèles `boot-config/`). Fabrication de l'image : `RUNBOOK_MAINTENEUR_CARTE_SD.md`. Notice non-technique : `GUIDE_OPERATEUR.md`. `Program.cs` charge une surcharge optionnelle `photobooth.json` depuis `/boot/firmware/photobooth/` (override via `PHOTOBOOTH_CONFIG_DIR`, repli dev `./config/`). ⚠️ Rendu : `AVALONIA_RENDERER=software` est un no-op en Avalonia 11 ; `--drm` est toujours accéléré (GPU VC4) → forcer le GL logiciel via `LIBGL_ALWAYS_SOFTWARE=1`+`GALLIUM_DRIVER=llvmpipe`, ou replier sur `StartLinuxFbDev`. **À valider sur Pi 3.**

## Repository Layout

- `CS/` is the main UWP app project.
  - `CS/PushButton.sln` is the solution to open/build.
  - `CS/PushButton.csproj` is the main app project.
  - `CS/MainPage.xaml` defines the photobooth UI.
  - `CS/MainPage.xaml.cs` orchestrates GPIO events, GoPro commands, countdowns, photo display, video state, and light control.
  - `CS/Assets/` contains UI images, logos, font assets, and photobooth visual resources.
- `GoProWifi/` is a UWP class library wrapping the GoPro HTTP API.
  - It currently assumes the GoPro is reachable at `10.5.5.9`.
  - It controls shutter/modes through `gpControl` and downloads media through port `8080`.
- `RasberryPiLib/` is a UWP class library for Raspberry Pi hardware access.
  - It wraps GPIO buttons, output pins, shift registers, seven-segment display support, and the MAX44009 light sensor.
- `RasberryLib/` appears to be an older or experimental .NET Core library. Do not treat it as the main hardware library unless the task explicitly concerns it.
- `testvlcsharp/` appears to be a separate UWP VLC/video experiment. Do not modify it for normal photobooth work unless the task explicitly requires it.

Generated folders such as `bin/` and `obj/` are present in this checkout. Avoid editing or reviewing them as source.

## Runtime Architecture

The main app flow lives in `CS/MainPage.xaml.cs`:

- GPIO pins:
  - Photo button: GPIO `18`
  - Video button: GPIO `20`
  - Light output: GPIO `17`
- Photo button flow:
  - Debounced falling-edge GPIO event triggers `PhotoLaunch()`.
  - UI shows a French countdown.
  - Light output is switched on.
  - GoPro is set to single-photo mode, then shutter is triggered.
  - Latest GoPro media is downloaded and shown in the UI.
  - Slideshow resumes after the displayed photo delay.
- Video button flow:
  - Toggles GoPro video recording.
  - Shows/hides the red `Rec` indicator.
  - Stops automatically after about 10 seconds.
- Background slideshow:
  - Periodically sends the GoPro UDP keepalive packet.
  - Lists GoPro media and displays random non-MP4 files.

## Build And Run

Preferred workflow:

1. Open `CS/PushButton.sln` in Visual Studio with UWP tooling installed.
2. Restore NuGet packages.
3. Build the `PushButton` project.
4. For Raspberry Pi / Windows IoT deployment, use the `ARM` configuration.
5. For local desktop checks, use `x86` or `x64`, but expect GPIO/I2C code to fail or be unavailable without compatible hardware.

Command-line builds may require Visual Studio MSBuild with UWP workloads installed. Use the solution under `CS/`, not a root-level solution.

The app manifest uses:

- `internetClient` for GoPro HTTP access.
- `lowLevel` for GPIO/I2C hardware access.

Do not remove these capabilities unless replacing the hardware/network approach.

## Hardware And Network Assumptions

Treat the following as behaviorally significant:

- The GoPro IP is hardcoded as `10.5.5.9` in `GoProWifi/GoproWifi.cs` and in the UDP keepalive code in `CS/MainPage.xaml.cs`.
- The GoPro media API path is hardcoded around `/videos/DCIM/`.
- Button inputs use pull-up mode when available and react on `GpioPinEdge.FallingEdge`.
- The light output is active-high through `SwitchPin.On()` and `SwitchPin.Off()`.
- The MAX44009 light sensor is addressed at `0x4A` on I2C bus `1`.

If you make any of these configurable, preserve the existing defaults unless the user asks for a hardware rewiring or a different GoPro network.

## Coding Guidelines

- These guidelines apply to the **legacy UWP tree** (`CS/`, `GoProWifi/`, `RasberryPiLib/`). For new cross-platform work use `src/` (.NET 8 / Avalonia) per the migration section above.
- Within the legacy UWP tree: keep it UWP C#; do not migrate frameworks or modernize those project files. The framework migration itself now lives in `src/`.
- Prefer small, targeted changes in `CS/MainPage.xaml.cs`, `CS/MainPage.xaml`, `GoProWifi/`, or `RasberryPiLib/`.
- Preserve the current French booth-facing text unless the task asks for copy changes.
- Keep UI-thread work on the UWP dispatcher when changing UI from async or hardware event paths.
- Avoid blocking calls on the UI thread. Existing code contains older async patterns; improve carefully and only in the touched flow.
- Do not edit generated `*.g.cs`, `*.g.i.cs`, `.xbf`, `bin/`, or `obj/` files.
- Do not commit or modify certificate files such as `CS/PushButton_TemporaryKey.pfx` unless the task is specifically about packaging/signing.
- Be careful with silent `catch` blocks. If changing error handling, avoid noisy user-facing failures during booth operation, but leave enough diagnostics for troubleshooting.

## Testing Guidance

There are no obvious automated tests in this repository.

For code changes:

- Build `CS/PushButton.sln` in the relevant platform configuration.
- For hardware-facing changes, validate on the target Raspberry Pi or a compatible Windows IoT device.
- For GoPro changes, test while connected to the GoPro Wi-Fi network and confirm:
  - mode switching works,
  - shutter start/stop works,
  - media list retrieval works,
  - image download works,
  - UDP keepalive still prevents stream/session timeout.
- For XAML changes, verify the full-screen photobooth UI visually at the target display resolution.

When hardware is unavailable, state that validation was limited to static review or build-only checks.

## Common Pitfalls

- `GpioController.GetDefault()` can return `null` on non-IoT desktop machines.
- The GoPro API calls may hang, return `ServiceUnavailable`, or return empty media while the camera is busy writing a file.
- The code currently assumes the last media item is the newly captured photo. Be cautious when changing ordering or filtering.
- The slideshow deliberately avoids `.MP4` entries.
- Video recording state is tracked locally by `_rec`; keep it consistent with GoPro stop/start calls.
- The folder name uses the misspelling `RasberryPiLib`. Keep existing namespaces and project references unless doing a coordinated rename.

## Agent Working Rules

- First inspect the relevant source files before editing; this is an older UWP project with generated artifacts mixed into the tree.
- Keep edits scoped to source files and assets required by the task.
- If changing build configuration, update the `.csproj` or `.sln` intentionally and explain why.
- If adding new dependencies, verify they support UWP and the target Windows 10 SDK range used here.
- If replacing hardcoded hardware constants, provide a compatibility path for the current pinout and GoPro address.
- Summarize any untested hardware assumptions in the final response.
