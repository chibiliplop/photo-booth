# Interface web d'admin/debug (opt-in)

`Photobooth.Admin` embarque un petit serveur web (Kestrel) dans le process de l'app. Il sert au **dépannage terrain à distance** — depuis un téléphone ou un PC sur le même réseau WiFi — sans avoir à ouvrir un SSH.

> **Opt-in strict.** Tant que `Admin.Enabled` vaut `false` (défaut), aucun socket n'est ouvert et la surface d'attaque est nulle. L'hôte ne démarre que si `Enabled=true` est explicitement posé dans `photobooth.json`.

---

## Clés de configuration `Admin`

La table complète (défaut / qui édite / effet / validation) se trouve dans
[`../monter-et-utiliser/config-reference.md`](../monter-et-utiliser/config-reference.md#admin--interface-de-diagnostic-web).

Ce qui suit décrit chaque clé sous l'angle **sécurité / exposition réseau**, sans dupliquer la table.

| Clé | Rôle opérationnel / exposition |
|---|---|
| `Enabled` | Maître absolu : `false` → rien n'écoute. Ne passer à `true` que sur un réseau de confiance et avec un `Pin` non vide. |
| `ListenAddress` | Par défaut `0.0.0.0` : l'hôte répond sur **toutes les interfaces**, y compris l'IP du Pi visible sur le LAN GoPro. Restreindre à une IP spécifique pour limiter l'exposition si la borne est sur plusieurs réseaux. |
| `Port` | Défaut `8080`. Distinct du `8080` de la GoPro (`10.5.5.9`) qui n'est pas joint depuis cet adressage. |
| `Pin` | **Seule frontière réseau↔root.** Vide = aucune authentification : l'hôte démarre avec un warning bruyant et expose la surface complète (console root anonyme). En pratique : **toujours définir un PIN.** |
| `ShowAddressOnStartup` | Affiche l'URL (`http://<ip>:<port>`) à l'écran au démarrage ; le 1er appui bouton photo ferme l'overlay. Sans effet si `Enabled=false`. |

---

## Ce que l'interface expose

### Lecture (sans écriture d'état)

- **Logs live** : flux Server-Sent Events (`InMemoryLogSink`, ring buffer des 500 derniers events Serilog) + snapshot.
- **État borne** : `BoothTelemetry` — dernière raison d'échec d'impression, état workflow, GoPro, imprimante.
- **File d'impression CUPS** : `lpstat -p`, `lpstat -t`, `lpq`.
- **Logs CUPS** : tail de `/var/log/cups/error_log` (lecture via `sudo`).

### Écriture (actions avec effets permanents ou privilégiés)

- **Actions imprimante/CUPS** : `cupsenable`, `cupsaccept`, test d'impression (`lp`), purge file (`cancel -a`), détection USB (`lpinfo -v`).
- **Édition de `photobooth.json`** : GET/PUT via `/api/config` — valide les options existantes, écrit atomiquement sur la FAT32, puis **redémarre le service `photobooth`** (pas reboot système).
- **Console shell arbitraire** : commande one-shot, sortie streamée (SSE), timeout + kill. Via `sudo NOPASSWD: ALL` → **console root de fait**.
- **Restart app / reboot système** : via `sudo systemctl restart photobooth` / `sudo reboot`.
- **Reprise GoPro**.

> Les changements CUPS (`cupsenable`/`cupsaccept`) sont **éphémères** sur une image avec overlay FS actif (root en lecture seule) : ils sont réinitialisés au reboot par `photobooth-printer.service` qui recrée la file. L'UI doit l'indiquer ; un correctif durable passe par la config FAT32 ou l'image.

---

## Modèle de menace

**Dérogation read-write actée le 2026-06-23** (voir le design complet :
`docs/superpowers/specs/2026-06-23-admin-debug-web-interface-design.md`, §3 et §10).

Le **PIN** (combiné à la clé WiFi GoPro) est l'**unique frontière réseau↔root**. Quiconque a la clé WiFi de la GoPro et connaît (ou devine) le PIN peut administrer la borne, éditer la config, rebootter et exécuter des commandes root.

Contrôles compensatoires obligatoires côté Plan 3/3 :

| Contrôle | Détail |
|---|---|
| **CSRF** | Header `X-Admin-CSRF` exigé sur tout endpoint mutant. |
| **Cookie `HttpOnly` + `SameSite=Strict`** | Cookie de session non accessible en JS, refusé cross-site. |
| **Sortie échappée** | `textContent` (jamais `innerHTML` avec contenu dynamique) — fin du compromis XSS read-only du Plan 2/3. |
| **Audit-log** | Log `Information` de chaque action et commande console **avant** exécution. |
| **Warning si `Enabled && Pin==""`** | L'hôte démarre mais logue un warning bruyant et expose la surface complète (root anonyme sur le LAN GoPro — risque accepté, opérateur averti). |

**Règle opérationnelle : n'activer l'interface que sur un réseau de confiance et toujours définir `Admin.Pin`.**

---

## Privilèges (dérogation D9)

Les actions root passent par `sudo`. L'image installe deux artefacts via
`image-builder/scripts/00-photobooth.sh` (bloc « 3.4bis »), versionnés dans `deploy/` :

| Artefact | Destination | Perms | Rôle |
|---|---|---|---|
| `sudoers.d/photobooth` | `/etc/sudoers.d/photobooth` | `0440 root:root` | `pi ALL=(ALL) NOPASSWD: ALL` — liste blanche abandonnée ; la seule frontière est `Admin.Pin`. Validé par `visudo -c` à l'install (retiré si invalide). |
| `photobooth-write-config.sh` | `/usr/local/sbin/photobooth-write-config.sh` | `0755` | Écriture **atomique** (`temp + rename`) de `photobooth.json` sur la FAT32 root depuis stdin. Appelé en `sudo` par l'hôte quand l'écriture directe (user `pi`) est refusée. Résiste à une coupure secteur (FAT32 sans journal). |

Vérif manuelle sur le Pi :

```bash
sudo -n true          # doit réussir sans mot de passe
sudo visudo -c        # /etc/sudoers.d/photobooth: parsed OK
```

Pour les détails de déploiement des artefacts, voir
[`../../deploy/README.md`](../../deploy/README.md) (point 1, colonne « avancé ») et
[`../../image-builder/README.md`](../../image-builder/README.md).

---

## Activation sur une borne déployée

Pas besoin de rebuilder l'image. Éditer `photobooth.json` sur la partition FAT32
(`/boot/firmware/photobooth/`) depuis n'importe quel PC, puis rebrancher la borne :

```jsonc
{
  "Admin": {
    "Enabled": true,
    "Pin": "1234",          // OBLIGATOIRE en pratique — vide = aucune authentification
    "Port": 8080,           // défaut 8080 ; distinct du 8080 de la GoPro (10.5.5.9)
    "ListenAddress": "0.0.0.0"
  }
}
```

Accès : `http://<ip-du-Pi>:8080/` → login PIN → page à onglets (dashboard, logs, imprimante, config, actions, console).

### Activation en développement local (sans Pi)

```bash
PHOTOBOOTH_Admin__Enabled=true PHOTOBOOTH_Admin__Pin=1234 dotnet run --project src/Photobooth.App
# puis http://localhost:8080
```

---

## Onglets de l'interface

| Onglet | Contenu |
|---|---|
| **Dashboard** | État borne, GoPro, imprimante, dernière capture, URL admin |
| **Logs** | Tail live (SSE), filtre par niveau |
| **Imprimante** | Dernier échec (vraie raison), file CUPS, état + actions (Réactiver/Accepter/Test/Détecter USB), config `Printer`, logs CUPS + Purger |
| **Config** | Édition des sections de `photobooth.json`, bouton « Appliquer » (write + restart) |
| **Actions** | Test impression, reprise GoPro, restart app, reboot, purge file |
| **Console** | Commande one-shot arbitraire, sortie streamée |

---

## Hors périmètre Phase 1 (à venir)

Phase 2 apportera : point d'accès WiFi autonome (`ap0` virtuel + hostapd + dnsmasq), mDNS/avahi (`photobooth.local`), persistance optionnelle des logs sur FAT32 (`Admin.PersistLogsToFat`, défaut `false`).

> Note : `Admin.ShowAddressOnStartup` (overlay boot + dismiss) est livré depuis le 2026-06-24 et appartient à la Phase 1.

Voir le design complet : `docs/superpowers/specs/2026-06-23-admin-debug-web-interface-design.md`.
