# Interface web d'admin / debug embarquée — Design

- **Date** : 2026-06-23
- **Branche cible** : `feat/printer` (la feature impression et ses messages — dont « Impression impossible » — y vivent)
- **Statut** : design validé, en attente de revue avant plan d'implémentation

## 1. Contexte & problème

La borne est une app **Avalonia .NET 8** (publiée `--self-contained` linux-arm64) qui tourne en kiosk sur Raspberry Pi. Aujourd'hui, diagnostiquer un souci sur site (ex. « Impression impossible ») impose un accès SSH : on lit `journalctl`, `lpstat`, on rejoue `lp` à la main. La vraie cause d'un échec d'impression est **avalée** : `PhotoboothWorkflow.PrintLastPhotoAsync` (`src/Photobooth.Core/Workflow/PhotoboothWorkflow.cs:348`) logge `LogWarning(ex, "Printing failed.")` mais n'affiche qu'un générique « Impression impossible ».

**Objectif** : une petite interface web d'admin/debug embarquée, accessible depuis un téléphone/PC, qui permet sur site, sans SSH :
- de la **supervision** (état borne, GoPro, file d'impression) ;
- du **debug live** (logs en direct, vraie raison des échecs) ;
- de l'**édition de config** (sans re-flasher la carte SD) ;
- des **actions** (test impression, reprise GoPro, restart app, reboot, purge file CUPS) ;
- une **console de commandes** arbitraire pour aller plus loin.

## 2. Contrainte matérielle déterminante

D'après `src/Photobooth.Core/Options/GoProOptions.cs` (`ControlBaseUrl=http://10.5.5.9`) et `deploy/photobooth-provision.sh` (NetworkManager, `IFACE=wlan0`), la borne se connecte à la **GoPro en tant que client WiFi (STA)** sur `wlan0` ; la GoPro est le point d'accès `10.5.5.9`. La GoPro est AP-only → le Pi **doit** rester sa station, et le Pi n'a **qu'une radio WiFi intégrée**.

« Connecté à la GoPro **+** endpoint admin » est donc résolu par une **exposition configurable** (voir §4), pas par un mode unique.

## 3. Décisions (verrouillées)

| # | Décision | Choix |
|---|---|---|
| D1 | Architecture du serveur | **Embarqué** dans le process app (Kestrel/minimal API), partage les singletons DI existants |
| D2 | Capacités | Dashboard + logs/debug + config + actions + console arbitraire |
| D3 | Exposition réseau | Configurable : `ap` \| `gopro` \| `both` |
| D4 | Mode `ap` | AP+STA concurrent (`ap0` virtuel + hostapd + dnsmasq), **opt-in, `false` par défaut** |
| D5 | Découverte d'IP | mDNS (`photobooth.local`) **+** overlay boot affichant l'URL/IP, **dismiss au 1er appui bouton** puis caché en usage normal |
| D6 | Auth | Mot de passe AP uniquement ; **PIN optionnel** (`Admin.Pin`) recommandé si exposition `gopro`/`both` |
| D7 | Application config | Écrit `photobooth.json` (sur la FAT32, **via chemin privilégié** car app non-root, écriture **atomique** temp+rename) puis **restart du service `photobooth`** (pas reboot système). Voir §14. |
| D8 | Console | Commande **one-shot arbitraire**, sortie streamée, exécutée en **utilisateur app (non-root)**, timeout + kill |
| D9 | Privilèges | Actions système **et écriture config FAT32** via **sudoers en liste blanche** ; jamais de shell root |
| D10 | Flag maître | `Admin.Enabled` (défaut `false`), lu par la couche réseau (script boot) **et** par l'app |
| D11 | Robustesse | Tout échec du bloc admin (AP qui ne monte pas, avahi absent, commande KO) est **dégradé, jamais fatal** |

### Sécurité — note explicite

En mode `gopro`/`both`, la frontière de confiance s'élargit : *quiconque a la clé WiFi de la GoPro* peut administrer/rebooter la borne et ouvrir la console (RCE). Mitigation : `Admin.Pin` (si non vide, exigé avant tout accès) **recommandé** pour ces modes. En mode `ap` seul, la clé WPA2 de l'AP dédié (opt-in) suffit ; l'AP **doit** avoir un mot de passe fort.

## 4. Architecture — trois couches

```
┌─ Couche RÉSEAU (root, hors app) ──────────────────────────────┐
│ photobooth-admin-ap.service (si Admin.Enabled & exposure≠gopro)│
│   iw dev wlan0 interface add ap0 type __ap                     │
│   hostapd (ap0, canal aligné sur wlan0/GoPro) + dnsmasq        │
│   → AP "Photobooth-Admin", 192.168.50.1/24                     │
│ avahi-daemon → annonce photobooth.local                        │
│ wlan0 reste STA GoPro, intouché                                │
└────────────────────────────────────────────────────────────────┘
┌─ Couche WEB (app, non-root) ──────────────────────────────────┐
│ AdminWebHost : Kestrel + minimal API                           │
│   bind selon Admin.Exposure :                                  │
│     ap    → 192.168.50.1                                       │
│     gopro → IP wlan0 (10.5.5.x)                                │
│     both  → les deux                                           │
│ lit BoothTelemetry + InMemoryLogSink ; pousse des BoothCommand │
└────────────────────────────────────────────────────────────────┘
┌─ Couche PRIVILÈGES ───────────────────────────────────────────┐
│ sudoers liste blanche : systemctl restart photobooth | reboot, │
│   cupsenable/cupsaccept, cancel -a, lecture cups/error_log     │
└────────────────────────────────────────────────────────────────┘
```

**Caveat AP+STA isolé en couche réseau** : l'AP partage la radio avec la GoPro et doit suivre son canal (fragile si GoPro en 5 GHz). Si l'AP ne monte pas, le service le logge et sort en succès (`exit 0` façon `photobooth-provision.sh`) — la borne n'est **jamais** dégradée par le mode debug.

## 5. Composants

Nouveau projet **`Photobooth.Admin`** (lib, `net8.0`, `<FrameworkReference Include="Microsoft.AspNetCore.App" />`), référencé par `Photobooth.App`. En publish self-contained linux-arm64, le runtime ASP.NET est embarqué → rien à installer sur le Pi.

| Composant | Projet | Rôle |
|---|---|---|
| `AdminOptions` | Core | Section `Admin` : `Enabled` (false), `Exposure` (`ap`), `Pin` ("", optionnel), `ApSsid`, `ApPassword`, `Port` (8080), `ApAddress` (192.168.50.1), `Subnet`, `PersistLogsToFat` (false, Phase 2). Méthode `Validate()`. |
| `BoothTelemetry` (singleton) | Core | État vivant : `BoothState`, dernier `SetStatus` (texte+niveau+horodatage), GoPro joignable (dernier keepalive OK), imprimante (type/activée), **dernière raison d'échec impression**, dernière capture, URL/IP admin. |
| `InMemoryLogSink` | Adapters | Sink Serilog : ring buffer des N derniers events + flux live (Server-Sent Events). |
| `AdminWebHost` | Photobooth.Admin | Démarre/arrête Kestrel ; mappe les endpoints ; bind selon `Exposure`. |
| `CommandConsoleService` | Photobooth.Admin | Exécute `bash -c <cmd>` en user app, stream stdout/stderr (SSE), timeout + kill. |
| `PrivilegedActions` | Photobooth.Admin | Invoque les commandes sudoers en liste blanche. |
| `AdminInfoOverlay` (UI) | Photobooth.App | Overlay boot avec URL/IP ; dismiss au 1er appui bouton. |

### Points d'intégration (code existant)

- **DI / config** : ajouter `services.Configure<AdminOptions>(config.GetSection(AdminOptions.Section))` dans `ServiceConfiguration.AddPhotobooth` (`src/Photobooth.App/Composition/ServiceConfiguration.cs:27`) et chaîner `AdminOptions.Validate()` dans `ValidateOptions` (ligne 61). Enregistrer `BoothTelemetry`, `AdminWebHost` en singletons.
- **Démarrage** : dans `App.OnFrameworkInitializationCompleted` (`src/Photobooth.App/App.axaml.cs`), après `_ = workflow.StartAsync();`, ajouter `if (adminOpt.Enabled) _ = adminHost.StartAsync();` sous try/catch (même motif dégradé que les boutons GPIO).
- **Logs en mémoire** : ajouter `.WriteTo.Sink(inMemorySink)` dans `Program.ConfigureSerilog` (`src/Photobooth.App/Program.cs:104`) et exposer le buffer via DI.
- **Telemetry** : `BoothTelemetry` est alimentée par le workflow ; en particulier, **capturer l'exception** aujourd'hui avalée en `PhotoboothWorkflow.cs:348` (stocker code/`stderr`/timeout) en plus du log existant.
- **Overlay** : `MainViewModel` (qui EST `IPhotoDisplay`) expose `IsAdminInfoVisible`/`AdminInfoText`. Le routage boutons de `App.axaml.cs` (`buttons.PhotoPressed += …`) appelle d'abord `vm.TryDismissAdminInfo()` : si l'overlay était visible, l'appui est **consommé** (pas de `PhotoRequested`).

## 6. Flux de données

- **Lecture état** : workflow & clients écrivent dans `BoothTelemetry` → `GET /api/status` (JSON) ; `GET /api/logs` (snapshot) et `GET /api/logs/stream` (SSE).
- **Config** : `GET /api/config` lit `photobooth.json` → `PUT /api/config` **valide via les `Validate()` existants** des classes Options (réutilisation, zéro duplication) → écrit le fichier → `PrivilegedActions.RestartApp()`.
- **Actions** : endpoints poussant un `BoothCommand` dans le canal du workflow (`PrintRequested`, `Recovered`) ou appelant directement `IPrinterAdapter` / CUPS / systemd.
- **Console** : `POST /api/console` (commande) → `CommandConsoleService` → SSE stdout/stderr.

## 7. Onglets de l'UI

1. **Dashboard** — état borne, GoPro, imprimante, dernière capture, URL admin.
2. **Logs** — tail live (SSE), filtre par niveau.
3. **Imprimante** — voir §8.
4. **Config** — édition des sections, bouton « Appliquer » (write + restart).
5. **Actions** — test impression, reprise GoPro, restart app, reboot, purge file.
6. **Console** — invite de commande arbitraire, sortie streamée.

## 8. Onglet « Imprimante » (mappé 1:1 sur la procédure de debug)

| Panneau | Contenu | Source / commande |
|---|---|---|
| **Dernier échec** (Étape 1) | La vraie raison : code `lp` + `stderr`, ou « lp introuvable », ou timeout watchdog | `BoothTelemetry` + tail `journalctl -u photobooth` |
| **File CUPS** (Étape 2) | File créée ? état ? | `lpstat -p photobooth-printer`, `lpstat -t`, `journalctl -u photobooth-printer` |
| **État imprimante + repro** (Étape 3) | Badges idle/paused/rejecting ; boutons **Réactiver**, **Accepter**, **Test d'impression**, **Détecter USB** | `lpstat -p -d`, `cupsenable`, `cupsaccept`, `lp -d`, `lpinfo -v` |
| **Config imprimante** (Étape 4) | Section `Printer` (Type/Name/Media/Options), éditable → flux config §6 | `photobooth.json` |
| **Logs CUPS + Purger** (Étape 5) | Tail error_log, `lpq`, **Purger la file** | `/var/log/cups/error_log`, `lpq`, `cancel -a` |

Privilèges : `lpstat`/`lpinfo`/`lp`/`lpq` en user app ; `cupsenable`/`cupsaccept` via groupe `lp` (déjà ajouté par le provisioning imprimante) ou sudoers ; lecture `error_log` via sudoers.

## 9. Gestion d'erreurs

- AP qui ne monte pas, avahi absent, commande qui timeout → **loggés et dégradés**, jamais fatals ; la borne fonctionne même si tout le bloc admin échoue (cohérent avec le « mode dégradé » de `Program.cs:67`).
- `CommandConsoleService` : timeout configurable + kill du process ; pas de blocage du thread UI.
- `AdminWebHost` ne démarre que si `Admin.Enabled` ; toute exception au `StartAsync` est avalée + loggée.

## 10. Sécurité (récap)

- Kestrel **jamais** sur wlan0 sauf `Exposure` = `gopro`/`both` explicite.
- `Admin.Pin` (si non vide) exigé avant tout accès ; recommandé pour `gopro`/`both`.
- sudoers : **liste blanche stricte** de commandes complètes, pas de wildcard, pas de shell root.
- Tout le bloc `false` par défaut → aucune surface d'attaque en prod tant qu'admin n'est pas activé.

## 11. Tests

- **Unit** : `AdminOptions.Validate()`, sérialisation `BoothTelemetry`, parsing/validation config (réutilise les `Validate()` existants), `CommandConsoleService` (timeout/kill, capture stdout/stderr) avec une commande de test.
- **Intégration** : endpoints API en `TestServer` (status, logs, config GET/PUT avec validation rejetant une valeur invalide).
- **Manuel sur Pi réel** (hors CI) : montée de l'AP+STA, mDNS, overlay boot + dismiss, restart après édition config.

## 12. Phasage

- **Phase 1 — valeur immédiate, zéro matériel** : `Photobooth.Admin` + `BoothTelemetry` + `InMemoryLogSink` + tous les onglets (dashboard, logs, **imprimante**, config, actions, console), accessibles en `Exposure=gopro`. Capture de la vraie raison d'échec impression (`PhotoboothWorkflow.cs:348`).
- **Phase 2 — mode AP dédié** : `photobooth-admin-ap.service` + script (ap0/hostapd/dnsmasq), avahi/mDNS, overlay boot + dismiss, `Exposure=ap`/`both`, sudoers + privilèges, **persistance optionnelle des logs sur FAT32** (`Admin.PersistLogsToFat`, défaut `false`).

Chaque phase a son propre plan d'implémentation.

## 13. Hors périmètre (YAGNI)

- Pas de comptes multi-utilisateurs ni de RBAC.
- Pas de hot-reload de config sans restart (D7 tranché : restart service).
- Pas de HTTPS/TLS (réseau local de confiance ; ajoutable plus tard si besoin).
- Pas de terminal interactif PTY (D8 tranché : console one-shot).

## 14. Système de fichiers overlay (root read-only) — contraintes

Architecture 2 couches (RUNBOOK §0/§3, `deploy/README.md`) : root ext4 **figé en overlay read-only** (image « dist », `PHOTOBOOTH_OVERLAY=1`) ; FAT32 `/boot/firmware/photobooth/` **hors overlay**, éditable et persistante. L'app tourne en **`User=pi` non-root** (`deploy/systemd/photobooth.service:25`), WorkingDirectory `/home/pi/photobooth` (sur l'ext4 → overlay).

1. **Écriture config** : `photobooth.json` est sur la FAT32 → les écritures **persistent**. Mais la FAT32 est montée **root** et l'app est `pi` → écriture **via chemin privilégié** (helper root en liste blanche sudoers), en **atomique** (temp + rename) pour résister à une coupure secteur (FAT n'a pas de journal).
2. **Artefacts système de la feature** (`photobooth-admin-ap.sh`, `photobooth-admin-ap.service`, fichier sudoers, confs hostapd/dnsmasq, conf avahi) → **bakés dans l'image** via `image-builder/scripts/00-photobooth.sh` + versionnés dans `deploy/`. **Jamais créés au runtime** (sinon perdus au reboot). Pattern identique au provisioning existant.
3. **Changements runtime éphémères** (par design overlay) : `cupsenable`/`cupsaccept`/`lpadmin` depuis l'onglet imprimante sont réinitialisés au reboot (raison d'être de `photobooth-printer.service` qui recrée la file à chaque boot). L'UI doit indiquer « temporaire » ; un correctif durable passe par la config FAT32 ou l'image.
4. **Logs** : Serilog écrit dans `/home/pi/photobooth/logs/` (overlay) et journald est probablement en RAM → **perdus au reboot**. Le buffer mémoire (`InMemoryLogSink`) est volatil aussi. Pour débuguer un crash **après reboot**, écriture optionnelle des logs sur la **FAT32** (hors overlay) — **Phase 2**, flag `Admin.PersistLogsToFat` (**défaut `false`** : si désactivé, aucun log n'est écrit sur la FAT32).
5. **Flags & params** (`Admin.Enabled`, SSID/mot de passe AP) dans `photobooth.json` (FAT32) → persistent, éditables, lus par le script boot comme `wifi.txt`.
6. **Dev vs dist** : l'image **dev** (`PHOTOBOOTH_OVERLAY=0`, root inscriptible) permet de développer/tester toute la **Phase 1** sans ces contraintes ; elles ne s'appliquent qu'à l'image **dist** et à la Phase 2.
