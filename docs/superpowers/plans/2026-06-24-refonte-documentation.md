# Refonte de la documentation — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remplacer une doc tentaculaire (12 fichiers plats, 3 index divergents, 3 docs obsolètes, faits dupliqués, mentions UWP fantômes) par une arborescence `docs/` par public avec une information = un endroit.

**Architecture:** Deux branches `docs/monter-et-utiliser/` (DIYer) et `docs/developper-et-maintenir/` (dev=mainteneur), un `README.md` index unique, `AGENTS.md` réécrit src/-only. Sources de vérité uniques ; les autres docs *lient* au lieu de copier. Découpage de la branche DIYer par moment de vie (construire / installer / préparer-à-l'avance / jour-J).

**Tech Stack:** Markdown uniquement. Aucune modification de code `src/` / `deploy/` / `image-builder/`. Outils : `git mv`, `grep`, éditeur.

**Référence permanente :** le spec validé `docs/superpowers/specs/2026-06-24-refonte-documentation-design.md` (table de migration §6, purge UWP §7, liens §8, critères §10). Chaque tâche consomme une ou plusieurs **sources existantes** : l'implémenteur les **lit** avant transformation.

## Global Constraints

- **Une information = un endroit.** Un fait partagé (pins GPIO 18/20/17, rendu DRM/FBDev, piège Imager, `dotnet publish`, units systemd) a UNE source ; les autres docs y renvoient par lien.
- **Zéro UWP.** Aucune mention de `uwp`, `CS/`, `GoProWifi`, `RasberryPiLib`, `RasberryLib`, `testvlcsharp`, `appxmanifest`, `Windows 10 IoT` ne doit subsister dans un `*.md` **hors `docs/superpowers/`**.
- **Rien d'inventé.** Pas de schéma de câblage ni de BOM avec composants/prix (gaps #4/#30 du `BACKLOG.md` → *signalés*, pas fabriqués). `architecture.md` et `config-reference.md` reflètent le **code réel** (`src/`, `appsettings.json`), lu avant rédaction.
- **Langue :** français, ton existant conservé (opérateur = sans jargon ; dev = technique).
- **Branche :** tout le travail sur `docs/refonte` (créée en Task 1), commits fréquents (1 par tâche minimum).
- **Préservation d'historique git :** un fichier déplacé se fait par `git mv` *puis* édition (jamais delete+create).

**Convention de vérification (toutes tâches) :** les globs sont quotés pour zsh (`--include='*.md'`).

---

### Task 1: Branche + `config-reference.md` (source unique de config)

**Files:**
- Create: `docs/monter-et-utiliser/config-reference.md`
- Read first: `src/Photobooth.App/appsettings.json`, `deploy/boot-config/photobooth.json`, `deploy/boot-config/wifi.txt`

**Interfaces:**
- Produces: `docs/monter-et-utiliser/config-reference.md` — cible de liens depuis `2-installation.md`, `3-preparer-un-evenement.md`, `admin-debug.md`.

- [ ] **Step 1: Créer la branche**

```bash
cd /Users/tnabet/Dev/custom/photo-booth
git checkout -b docs/refonte
```
Expected: `Switched to a new branch 'docs/refonte'`

- [ ] **Step 2: Lire les sources de vérité de la config**

Lire `src/Photobooth.App/appsettings.json` (sections réelles + valeurs par défaut), `deploy/boot-config/photobooth.json` et `deploy/boot-config/wifi.txt`. Relever EXACTEMENT les sections présentes et leurs champs. Ne pas inventer de champ absent du code.

- [ ] **Step 3: Écrire `config-reference.md`**

Squelette (remplir chaque ligne à partir des valeurs LUES au Step 2) :

```markdown
# Référence de configuration

> Source unique des fichiers éditables sur la carte SD (partition FAT32
> `/boot/firmware/photobooth/`). Les autres docs renvoient ici.

## Fichiers de la carte
| Fichier | Rôle | Édité par |
|---|---|---|
| `wifi.txt` | réseau Wi-Fi de la GoPro | opérateur |
| `photobooth.json` | thème + comportement | opérateur (Theme/Gopro) / avancé (Hardware/Admin/Printer) |
| `fond.jpg` | image de fond | opérateur |
| `admin.txt` | mot de passe SSH `pi` (override) | mainteneur |

## `wifi.txt`
Clés exactes : `GOPRO_SSID`, `GOPRO_PASSWORD`, `WIFI_COUNTRY` (+ `WIFI_SSID`/`WIFI_PASSWORD` réseau secondaire optionnel).

## `photobooth.json` — sections
<!-- une sous-section par section RÉELLE de appsettings.json : Theme, Gopro,
     Hardware, Timings, Printer, Admin, Logging, (ScreenResolution).
     Pour chaque champ : nom · défaut · qui édite · effet · validation. -->

## Comportement en cas de valeur invalide
Une valeur invalide (pins hors 0-27, doublon, résolution malformée) n'empêche pas
le démarrage : la borne affiche un **bandeau rouge** et démarre en mode dégradé.
```

- [ ] **Step 4: Vérifier la présence des sections réelles**

```bash
grep -cE '^## |^### ' docs/monter-et-utiliser/config-reference.md
grep -iE 'GOPRO_SSID|GOPRO_PASSWORD' docs/monter-et-utiliser/config-reference.md
```
Expected: compte de titres ≥ 6 ; les 2 clés wifi présentes. Vérifier manuellement que chaque section listée existe bien dans `appsettings.json` (aucune section inventée).

- [ ] **Step 5: Commit**

```bash
git add docs/monter-et-utiliser/config-reference.md
git commit -m "docs: ajoute config-reference.md (source unique des champs de config)"
```

---

### Task 2: `1-electronique.md` (consolidation électronique)

**Files:**
- Create: `docs/monter-et-utiliser/1-electronique.md`
- Read first: `INSTALLATION_BORNE.md` §1 et §1.1/§1.2, `DEPLOY_RASPBERRY_PI.md` §0, `BACKLOG.md` (#4, #30)

**Interfaces:**
- Produces: `docs/monter-et-utiliser/1-electronique.md` — lié depuis `2-installation.md` et `config-reference.md` (bloc Hardware).

- [ ] **Step 1: Écrire `1-electronique.md`** en consolidant les faits électroniques EXISTANTS

Contenu (repris des sources, aucune invention) : matériel requis (Pi, microSD, écran, alim officielle) ; câblage GPIO en BCM — **photo GPIO 18, vidéo GPIO 20, lumière GPIO 17 (active-high)** ; **pull-up externe ~10 kΩ** sur 18/20 ; **avertissement : un GPIO 3,3 V ne pilote pas du secteur → relais/MOSFET** ; MAX44009 optionnel (I2C bus 1, `0x4A`) ; comment changer les pins (bloc `Hardware`, renvoi vers `config-reference.md`).

Terminer par une section explicite, sans rien fabriquer :

```markdown
## À compléter (suivi BACKLOG)
- Schéma de câblage détaillé + nomenclature chiffrée (BOM) : non encore rédigés
  (voir `BACKLOG.md` #30).
- Circuit relais 5 V documenté + avertissement sécurité 220 V : voir `BACKLOG.md` #4.
```

- [ ] **Step 2: Vérifier (faits présents, rien d'inventé)**

```bash
grep -E 'GPIO ?18|GPIO ?20|GPIO ?17' docs/monter-et-utiliser/1-electronique.md
grep -iE 'relais|mosfet' docs/monter-et-utiliser/1-electronique.md
grep -iE 'À compléter|BACKLOG' docs/monter-et-utiliser/1-electronique.md
```
Expected: pins présents ; avertissement relais présent ; section « À compléter » présente. Relecture manuelle : **aucun prix ni schéma inventé**.

- [ ] **Step 3: Commit**

```bash
git add docs/monter-et-utiliser/1-electronique.md
git commit -m "docs: ajoute 1-electronique.md (câblage consolidé, gaps #4/#30 signalés)"
```

---

### Task 3: `2-installation.md` (ex-INSTALLATION_BORNE, sans l'électronique)

**Files:**
- Move: `INSTALLATION_BORNE.md` → `docs/monter-et-utiliser/2-installation.md`
- Read first: le fichier lui-même (déjà lu au Step 1 de Task 2)

**Interfaces:**
- Consumes: `1-electronique.md`, `config-reference.md` (liens).
- Produces: `docs/monter-et-utiliser/2-installation.md` — **source unique du piège « Imager → répondre Non »**.

- [ ] **Step 1: Déplacer**

```bash
git mv INSTALLATION_BORNE.md docs/monter-et-utiliser/2-installation.md
```

- [ ] **Step 2: Éditer** — retirer ce qui part ailleurs, ajouter les renvois

Supprimer §1.1 et §1.2 (électronique → `1-electronique.md`) et l'encart « autres docs » d'en-tête (l'index est dans `README.md`). Remplacer la table des fichiers de config (§4) par un **renvoi à `config-reference.md`**. Conserver : obtenir l'image, flasher (Imager, **§3.4 piège « répondre Non » — reste ici**), 1er démarrage. Terminer §5 par un **test d'acceptation en mode test (sans GoPro)** : écran + boutons + lumière OK avant de quitter l'installation (renvoyer à `3-preparer-un-evenement.md` pour le détail du mode test).

- [ ] **Step 3: Vérifier (électronique partie, renvois en place, Imager conservé)**

```bash
grep -ciE 'pull-up|MAX44009|active-high' docs/monter-et-utiliser/2-installation.md   # attendu: 0
grep -iE 'config-reference|1-electronique' docs/monter-et-utiliser/2-installation.md  # liens présents
grep -iE 'personnalisation|répondre.*non|clear settings' docs/monter-et-utiliser/2-installation.md  # Imager conservé
```
Expected: 0 pour l'électronique ; liens présents ; piège Imager conservé.

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "docs: 2-installation.md (ex-INSTALLATION_BORNE, électronique extraite, renvois config)"
```

---

### Task 4: `3-preparer-un-evenement.md` (ex-GUIDE_OPERATEUR, partie « avant »)

**Files:**
- Move: `GUIDE_OPERATEUR.md` → `docs/monter-et-utiliser/3-preparer-un-evenement.md`

**Interfaces:**
- Consumes: `config-reference.md` (lien).
- Produces: `docs/monter-et-utiliser/3-preparer-un-evenement.md`.

- [ ] **Step 1: Déplacer**

```bash
git mv GUIDE_OPERATEUR.md docs/monter-et-utiliser/3-preparer-un-evenement.md
```

- [ ] **Step 2: Éditer** — ne garder que la préparation « à la maison »

Garder : « changer noms/année/fond » (renvoi `config-reference.md` pour le détail des champs), `wifi.txt` si autre GoPro, **mode test sans GoPro (dry-run jusqu'au bandeau vert)**, règle « modifier sans créer/renommer » + piège extensions cachées Windows. **Déplacer** vers `4-le-jour-j.md` : branchement, lecture des bandeaux sur place, ranger/éteindre, dépannage terrain (Task 5 les reprendra). Cadrer en tête : « à faire AVANT, à la maison ».

- [ ] **Step 3: Vérifier**

```bash
grep -iE 'mode test|fake' docs/monter-et-utiliser/3-preparer-un-evenement.md     # dry-run présent
grep -iE 'config-reference' docs/monter-et-utiliser/3-preparer-un-evenement.md   # renvoi config
```
Expected: mode test présent ; renvoi config présent.

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "docs: 3-preparer-un-evenement.md (préparation + test à l'avance)"
```

---

### Task 5: `4-le-jour-j.md` (sur place, ZÉRO config)

**Files:**
- Create: `docs/monter-et-utiliser/4-le-jour-j.md`
- Read first: le contenu retiré de l'ex-GUIDE_OPERATEUR (branchement, bandeaux, dépannage, admin)

**Interfaces:**
- Consumes: `developper-et-maintenir/admin-debug.md` (pointeur dépannage avancé).
- Produces: `docs/monter-et-utiliser/4-le-jour-j.md`.

- [ ] **Step 1: Écrire `4-le-jour-j.md`**

Contenu repris du GUIDE : **ordre de branchement** (GoPro allumée → écran HDMI → boutons → alim en DERNIER), lecture **bandeau vert/orange/rouge**, dépannage rapide niveaux 1-3 (power-cycle GoPro / borne), ranger/éteindre proprement. Section « dépannage avancé » = **pointeur** vers `../developper-et-maintenir/admin-debug.md` (ne recopie PAS la procédure d'activation ni de bloc JSON). **Aucune étape de configuration.**

- [ ] **Step 2: Vérifier l'absence de configuration**

```bash
grep -cE '"Admin"|"Theme"|"Mode"' docs/monter-et-utiliser/4-le-jour-j.md          # attendu: 0
grep -ciE 'ouvrez .*\.json|remplacez .*guillemets|enregistrez et fermez' docs/monter-et-utiliser/4-le-jour-j.md  # attendu: 0
grep -iE 'admin-debug' docs/monter-et-utiliser/4-le-jour-j.md                       # pointeur présent
```
Expected: 0 bloc de config ; 0 instruction d'édition ; pointeur admin présent.

- [ ] **Step 3: Commit**

```bash
git add docs/monter-et-utiliser/4-le-jour-j.md
git commit -m "docs: 4-le-jour-j.md (sur place, zéro config, pointeur admin)"
```

---

### Task 6: `developpement.md` (ex-README_NET8, dé-UWP + absorbe TESTING)

**Files:**
- Move: `README_NET8.md` → `docs/developper-et-maintenir/developpement.md`
- Delete: `TESTING_WITHOUT_GOPRO.md` (noyau absorbé)
- Read first: `README_NET8.md`, `TESTING_WITHOUT_GOPRO.md`, `AGENTS.md` (note macOS -6661)

**Interfaces:**
- Produces: `docs/developper-et-maintenir/developpement.md`.

- [ ] **Step 1: Déplacer**

```bash
git mv README_NET8.md docs/developper-et-maintenir/developpement.md
```

- [ ] **Step 2: Éditer**

Retirer : le préambule UWP (l.3 « réécriture de l'ancienne app UWP / code dans CS/… »), la **table d'index doc** (l'index est dans `README.md`), et la **section admin** (l.82-103 → part dans `admin-debug.md`). Garder : pré-requis .NET 8, build/test, lancer en local (fake + variables `PHOTOBOOTH_`), **simulateur GoPro**, `--screenshot`, structure `src/`. Ajouter un court paragraphe « Tester sans GoPro » (noyau de TESTING : mode fake + simulateur). Conserver la note macOS « lancer depuis une session graphique (Avalonia.Native -6661) ».

- [ ] **Step 3: Supprimer TESTING (obsolète)**

```bash
git rm TESTING_WITHOUT_GOPRO.md
```

- [ ] **Step 4: Vérifier**

```bash
grep -ciE 'uwp|CS/|GoProWifi|index|Vous voulez' docs/developper-et-maintenir/developpement.md  # attendu: 0
grep -iE 'simulator|simulateur|--screenshot' docs/developper-et-maintenir/developpement.md       # contenu dev présent
test ! -f TESTING_WITHOUT_GOPRO.md && echo "TESTING supprimé"
```
Expected: 0 UWP/index ; contenu dev présent ; TESTING supprimé.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "docs: developpement.md (ex-README_NET8, dé-UWP, absorbe TESTING, admin sorti)"
```

---

### Task 7: `admin-debug.md` (consolidation des 4 sources)

**Files:**
- Create: `docs/developper-et-maintenir/admin-debug.md`
- Read first: la section admin retirée de README_NET8 (Task 6), `RUNBOOK_MAINTENEUR_CARTE_SD.md` §3.5, `deploy/README.md` point 1, `docs/superpowers/specs/2026-06-23-admin-debug-web-interface-design.md`

**Interfaces:**
- Consumes: `config-reference.md` (clés `Admin`).
- Produces: `docs/developper-et-maintenir/admin-debug.md` — cible des pointeurs de `4-le-jour-j.md` et `fabrication-image.md`.

- [ ] **Step 1: Écrire `admin-debug.md`**

Contenu consolidé : rôle (hôte web Kestrel, **opt-in `Admin.Enabled=false` par défaut → rien n'écoute**), table des clés `Admin` (Enabled/ListenAddress/Port/Pin/ShowAddressOnStartup), lecture (logs SSE, état borne/imprimante) vs écriture (CUPS, édition `photobooth.json`+restart, console shell, restart/reboot), **modèle de menace** (PIN = unique frontière réseau↔root ; CSRF ; cookie HttpOnly+SameSite=Strict ; audit-log ; warning si Enabled && Pin==""), **privilèges** (`sudoers.d/photobooth` NOPASSWD, `photobooth-write-config.sh`), activation sur borne déployée (section `Admin` dans `photobooth.json`). Renvoyer à `deploy/README.md` et `image-builder/README.md` pour les artefacts (ne pas les recopier).

- [ ] **Step 2: Vérifier**

```bash
grep -iE 'modèle de menace|PIN|CSRF|NOPASSWD' docs/developper-et-maintenir/admin-debug.md
grep -iE 'opt-in|Enabled.*false|désactivé par défaut' docs/developper-et-maintenir/admin-debug.md
```
Expected: modèle de menace + opt-in présents.

- [ ] **Step 3: Commit**

```bash
git add docs/developper-et-maintenir/admin-debug.md
git commit -m "docs: admin-debug.md (interface web admin consolidée, 4 sources → 1)"
```

---

### Task 8: `architecture.md` (RÉÉCRITURE depuis le code réel)

**Files:**
- Delete: `LINUX_MIGRATION_BLOCKERS.md`
- Create: `docs/developper-et-maintenir/architecture.md`
- Read first: `src/Photobooth.Core/`, `src/Photobooth.Adapters/`, `src/Photobooth.App/`, `src/Photobooth.Admin/`, `src/Photobooth.Tests/` (structure réelle), + `LINUX_MIGRATION_BLOCKERS.md` (uniquement pour récupérer les décisions de rendu/packaging encore valides)

**Interfaces:**
- Produces: `docs/developper-et-maintenir/architecture.md` — **source unique** des décisions de rendu (DRM/FBDev) et packaging.

- [ ] **Step 1: Lire le code réel** pour décrire l'état présent (pas l'ancien plan).

- [ ] **Step 2: Écrire `architecture.md`** (état réel au présent)

Plan : projets en couches (`Core` domaine pur / `Adapters` HTTP+GPIO+fakes / `Admin` hôte web / `App` UI Avalonia + composition root / `Tests` xUnit) ; **workflow acteur** (états Idle/Capturing/Recording/Degraded) ; abstraction `IGoProClient` + `HttpGoProClient`/`FakeGoProClient` ; couche hardware Linux + fakes ; télémétrie diagnostic. **Décisions** (reformulées en faits présents) : pourquoi Avalonia ; **rendu DRM (accéléré) vs FBDev (logiciel), `AVALONIA_RENDERER=software` no-op en Avalonia 11** ; self-contained `linux-arm64`, trimming off, ReadyToRun on ; `systemd Restart=always`.

- [ ] **Step 3: Supprimer l'ancien plan**

```bash
git rm LINUX_MIGRATION_BLOCKERS.md
```

- [ ] **Step 4: Vérifier (zéro UWP, zéro plan de migration)**

```bash
grep -ciE 'uwp|CS/|GoProWifi|RasberryPiLib|migration|phase 1|blocage|à faire' docs/developper-et-maintenir/architecture.md  # attendu: 0
grep -iE 'IGoProClient|Idle/Capturing|DRM|FBDev|Avalonia' docs/developper-et-maintenir/architecture.md                       # archi réelle présente
test ! -f LINUX_MIGRATION_BLOCKERS.md && echo "ancien plan supprimé"
```
Expected: 0 UWP/migration ; archi réelle présente ; ancien fichier supprimé.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "docs: architecture.md (état réel du code, remplace LINUX_MIGRATION_BLOCKERS)"
```

---

### Task 9: `fabrication-image.md` (ex-RUNBOOK, dé-duplication vers `deploy/`)

**Files:**
- Move: `RUNBOOK_MAINTENEUR_CARTE_SD.md` → `docs/developper-et-maintenir/fabrication-image.md`
- Read first: `RUNBOOK_MAINTENEUR_CARTE_SD.md`, `deploy/README.md`, `image-builder/README.md`

**Interfaces:**
- Consumes: `deploy/`, `image-builder/`, `architecture.md`, `2-installation.md` (liens).
- Produces: `docs/developper-et-maintenir/fabrication-image.md`.

- [ ] **Step 1: Déplacer**

```bash
git mv RUNBOOK_MAINTENEUR_CARTE_SD.md docs/developper-et-maintenir/fabrication-image.md
```

- [ ] **Step 2: Éditer** — remplacer les copies inline par des renvois

Remplacer les **blocs reproduits depuis `deploy/`** (unit `photobooth.service`, `photobooth-provision.service`, script de provisioning, modèles `boot-config/`, table sudoers §3.5) par des **renvois à `deploy/README.md`** (source de vérité). Renvoyer la fabrication CI à `image-builder/README.md`, le rendu/packaging à `architecture.md`, le piège Imager à `2-installation.md`, l'admin à `admin-debug.md`. Garder le squelette des PHASES (0→6), la validation Phase 4 sur Pi réel, PiShrink, la checklist « image validée ».

- [ ] **Step 3: Vérifier (les units ne sont plus reproduits inline)**

```bash
grep -c 'ExecStart=/home/pi/photobooth/Photobooth.App' docs/developper-et-maintenir/fabrication-image.md  # attendu: 0 (renvoi à deploy/)
grep -iE 'deploy/|image-builder/' docs/developper-et-maintenir/fabrication-image.md                        # renvois présents
grep -iE 'PHASE 4|Pi réel|PiShrink' docs/developper-et-maintenir/fabrication-image.md                      # squelette conservé
```
Expected: 0 unit systemd inline ; renvois présents ; squelette conservé.

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "docs: fabrication-image.md (ex-RUNBOOK, renvois vers deploy/ au lieu de copies)"
```

---

### Task 10: `deploiement-manuel.md` (ex-DEPLOY_RASPBERRY_PI, dé-UWP)

**Files:**
- Move: `DEPLOY_RASPBERRY_PI.md` → `docs/developper-et-maintenir/deploiement-manuel.md`

**Interfaces:**
- Consumes: `architecture.md`, `fabrication-image.md` (liens).
- Produces: `docs/developper-et-maintenir/deploiement-manuel.md`.

- [ ] **Step 1: Déplacer**

```bash
git mv DEPLOY_RASPBERRY_PI.md docs/developper-et-maintenir/deploiement-manuel.md
```

- [ ] **Step 2: Éditer** — retirer UWP, renvoyer les faits partagés

Retirer la mention UWP (l.14 « Il remplace l'ancienne app UWP qui reste dans CS/… » → « Application .NET 8 / Avalonia »). Renvoyer le rendu à `architecture.md`, le chemin de distribution à `fabrication-image.md`, le piège Imager à `2-installation.md`. Garder l'install manuelle dev/debug (scp + systemd à la main, paquets, imprimante).

- [ ] **Step 3: Vérifier**

```bash
grep -ciE 'uwp|CS/' docs/developper-et-maintenir/deploiement-manuel.md   # attendu: 0
grep -iE 'scp|systemctl|--drm' docs/developper-et-maintenir/deploiement-manuel.md  # contenu dev présent
```
Expected: 0 UWP ; contenu présent.

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "docs: deploiement-manuel.md (ex-DEPLOY, dé-UWP, renvois)"
```

---

### Task 11: `AGENTS.md` (réécriture 100 % src/)

**Files:**
- Modify (réécriture intégrale): `AGENTS.md`
- Read first: `AGENTS.md` (l'actuel), `architecture.md` (Task 8), structure `src/`

**Interfaces:**
- Consumes: `architecture.md`, `developpement.md` (liens).
- Produces: `AGENTS.md` (reste à la racine).

- [ ] **Step 1: Réécrire `AGENTS.md`** — sans aucune trace UWP

Contenu : aperçu projet (borne photo Pi + GoPro, app .NET 8 / Avalonia kiosk) ; structure `src/` (5 projets, renvoi `architecture.md`) ; build/test/run (renvoi `developpement.md`) ; règles agent (français borne conservé, ne pas éditer `bin/`/`obj/`, attention aux `catch` silencieux, note macOS -6661, déploiement turnkey 2 couches). **Supprimer** : Repository Layout UWP, Runtime Architecture UWP, Build And Run VS/UWP, Hardware assumptions UWP, Coding Guidelines UWP, Common Pitfalls UWP, Agent Working Rules UWP.

- [ ] **Step 2: Vérifier (zéro UWP)**

```bash
grep -ciE 'uwp|CS/|GoProWifi|RasberryPiLib|RasberryLib|testvlcsharp|appxmanifest|Windows 10 IoT' AGENTS.md  # attendu: 0
grep -iE 'src/|Avalonia|architecture\.md|developpement\.md' AGENTS.md  # src/-first présent
```
Expected: 0 UWP ; contenu src/-first présent.

- [ ] **Step 3: Commit**

```bash
git add AGENTS.md && git commit -m "docs: AGENTS.md réécrit 100% src/, purge totale UWP"
```

---

### Task 12: `README.md` (index unique)

**Files:**
- Modify: `README.md`
- Read first: `README.md` (l'actuel), tous les fichiers de `docs/` créés

**Interfaces:**
- Consumes: tous les docs de `docs/` (liens).
- Produces: `README.md` — **l'unique index**.

- [ ] **Step 1: Réécrire l'index de `README.md`**

Garder le pitch produit. Remplacer la table d'index par UNE table à deux entrées de tête :

```markdown
## Documentation

### Monter et utiliser une borne
| Étape | Document |
|---|---|
| Construire le matériel (câblage, lumière) | [`docs/monter-et-utiliser/1-electronique.md`](docs/monter-et-utiliser/1-electronique.md) |
| Installer l'image (flasher, 1er démarrage) | [`docs/monter-et-utiliser/2-installation.md`](docs/monter-et-utiliser/2-installation.md) |
| Préparer un événement (avant, à la maison) | [`docs/monter-et-utiliser/3-preparer-un-evenement.md`](docs/monter-et-utiliser/3-preparer-un-evenement.md) |
| Le jour J (sur place) | [`docs/monter-et-utiliser/4-le-jour-j.md`](docs/monter-et-utiliser/4-le-jour-j.md) |
| Référence des fichiers de config | [`docs/monter-et-utiliser/config-reference.md`](docs/monter-et-utiliser/config-reference.md) |

### Développer ou maintenir
| Sujet | Document |
|---|---|
| Développer (build, test, simulateur) | [`docs/developper-et-maintenir/developpement.md`](docs/developper-et-maintenir/developpement.md) |
| Architecture & décisions | [`docs/developper-et-maintenir/architecture.md`](docs/developper-et-maintenir/architecture.md) |
| Fabriquer l'image SD | [`docs/developper-et-maintenir/fabrication-image.md`](docs/developper-et-maintenir/fabrication-image.md) |
| Déploiement manuel (dev/debug) | [`docs/developper-et-maintenir/deploiement-manuel.md`](docs/developper-et-maintenir/deploiement-manuel.md) |
| Interface web d'admin/debug | [`docs/developper-et-maintenir/admin-debug.md`](docs/developper-et-maintenir/admin-debug.md) |
```

Conserver le bloc « développeurs en bref » (3 commandes) en repointant vers `developpement.md`.

- [ ] **Step 2: Vérifier (un seul index, liens valides)**

```bash
for f in $(grep -oE 'docs/[^)]+\.md' README.md); do test -f "$f" && echo "OK $f" || echo "MORT $f"; done
```
Expected: tous `OK`, aucun `MORT`.

- [ ] **Step 3: Commit**

```bash
git add README.md && git commit -m "docs: README.md devient l'index unique (2 publics)"
```

---

### Task 13: Corriger `tools/gopro-simulator/README.md` (l.30 UWP)

**Files:**
- Modify: `tools/gopro-simulator/README.md:30`

- [ ] **Step 1: Éditer la l.30**

Remplacer « The current UWP app still hardcodes `10.5.5.9`, so the app must be made configurable… » par un fait présent : les URLs GoPro sont configurables (`Gopro.ControlBaseUrl`/`MediaBaseUrl`/`KeepAliveHost`) → pointer `127.0.0.1` vers le simulateur.

- [ ] **Step 2: Vérifier**

```bash
grep -ciE 'uwp' tools/gopro-simulator/README.md   # attendu: 0
```
Expected: 0.

- [ ] **Step 3: Commit**

```bash
git add tools/gopro-simulator/README.md && git commit -m "docs: simulateur — corrige la mention UWP obsolète"
```

---

### Task 14: Réécriture des liens inter-docs + vérification globale d'acceptation

**Files:**
- Modify: `deploy/README.md`, `image-builder/README.md` (liens `../RUNBOOK_…`, `../GUIDE_OPERATEUR`), + tout `*.md` contenant un lien vers un fichier déplacé.

**Interfaces:**
- Consumes: tous les nouveaux chemins.

- [ ] **Step 1: Recenser les liens morts vers les anciens noms**

```bash
grep -rnoE '\]\(([^)]*/)?(README_NET8|INSTALLATION_BORNE|GUIDE_OPERATEUR|DEPLOY_RASPBERRY_PI|RUNBOOK_MAINTENEUR_CARTE_SD|LINUX_MIGRATION_BLOCKERS|TESTING_WITHOUT_GOPRO)\.md[^)]*\)' --include='*.md' . | grep -vE 'docs/superpowers'
```
Expected initialement: une liste (liens à corriger, notamment dans `deploy/README.md` et `image-builder/README.md`).

- [ ] **Step 2: Réécrire chaque lien** vers son nouveau chemin (table de migration §6 du spec). Ex. `../RUNBOOK_MAINTENEUR_CARTE_SD.md` → `../docs/developper-et-maintenir/fabrication-image.md` ; `../GUIDE_OPERATEUR.md` → `../docs/monter-et-utiliser/3-preparer-un-evenement.md` (ou `4-le-jour-j.md` selon le contexte du renvoi).

- [ ] **Step 3: Vérification d'acceptation globale**

```bash
# (a) plus aucun lien vers les anciens noms (hors specs/plans de process)
grep -rnoE '\]\(([^)]*/)?(README_NET8|INSTALLATION_BORNE|GUIDE_OPERATEUR|DEPLOY_RASPBERRY_PI|RUNBOOK_MAINTENEUR_CARTE_SD|LINUX_MIGRATION_BLOCKERS|TESTING_WITHOUT_GOPRO)\.md[^)]*\)' --include='*.md' . | grep -vE 'docs/superpowers'
# attendu: vide

# (b) purge UWP totale hors docs/superpowers
grep -rniE 'uwp|GoProWifi|RasberryPiLib|RasberryLib|testvlcsharp|appxmanifest|Windows 10 IoT' --include='*.md' . | grep -vE 'docs/superpowers'
# attendu: vide

# (c) aucun lien .md mort (résolution des chemins relatifs)
python3 - <<'PY'
import re,os,glob
dead=[]
for f in glob.glob('**/*.md',recursive=True):
    if '/superpowers/' in f: continue
    base=os.path.dirname(f)
    for m in re.findall(r'\]\(([^)#]+\.md)[^)]*\)', open(f,encoding='utf-8').read()):
        if m.startswith('http'): continue
        if not os.path.exists(os.path.normpath(os.path.join(base,m))): dead.append((f,m))
print("DEAD:",dead if dead else "aucun")
PY
# attendu: DEAD: aucun

# (d) structure 5 + 5
ls docs/monter-et-utiliser/ docs/developper-et-maintenir/
```
Expected: (a) vide, (b) vide, (c) `aucun`, (d) 5 fichiers + 5 fichiers conformes au spec §4.

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "docs: réécriture des liens inter-docs + vérif globale (UWP/lien morts vides)"
```

---

## Self-Review (effectué)

**Couverture du spec :** chaque cible du §4/§6 a une tâche — config-reference (T1), 1-electronique (T2), 2-installation (T3), 3-preparer (T4), 4-le-jour-j (T5), developpement+suppression TESTING (T6), admin-debug (T7), architecture+suppression LINUX_MIGRATION (T8), fabrication-image (T9), deploiement-manuel (T10), AGENTS (T11), README index (T12), simulateur (T13), liens+vérif globale (T14). Purge UWP §7 → vérifs T6/T8/T10/T11/T13 + check global T14b. Liens §8 → T14. Garde-fous §2 → T1 (lire appsettings), T2 (gaps non fabriqués), T8 (code réel). Critères §10 → vérifs réparties + T14.

**Placeholders :** aucun « TBD/TODO » ; les transformations renvoient à des sources existantes nommées + squelettes fournis pour les fichiers neufs (config-reference, README index). Le contenu prose détaillé des fichiers déplacés vit dans les sources que l'implémenteur lit — instruction de transformation explicite, pas placeholder.

**Cohérence des noms :** chemins identiques entre §4 du spec, table de migration, liens README (T12) et checks T14 (`docs/monter-et-utiliser/`, `docs/developper-et-maintenir/`).
