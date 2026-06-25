# Refonte de la documentation — design

> **Date** : 2026-06-24
> **But** : remplacer une documentation « tentaculaire » (12 fichiers, ~2 160 lignes,
> 3 index divergents, 3 docs obsolètes traités comme factuels, faits dupliqués sur
> 4-6 fichiers, mentions massives d'un code UWP qui n'existe plus) par une
> arborescence claire **par public**, avec **une information = un endroit**.

---

## 1. Diagnostic (état actuel)

Le contenu, pris fichier par fichier, est soigné — le problème est **structurel**.

1. **Trois « index doc » concurrents et déjà divergents** : `README.md` (table « Vous
   voulez… / Document »), `README_NET8.md` (même table + colonne *Public*, mais liste
   `TESTING` et `deploy/` en plus), `AGENTS.md` (paragraphe qui re-liste les mêmes docs).
2. **Deux README à la racine**, dont un au nom daté : `README.md` (produit) +
   `README_NET8.md` (dev/build/simulateur/admin). GitHub n'affiche que `README.md`.
3. **Docs obsolètes figés à l'état « à faire », présentés comme factuels** :
   - `TESTING_WITHOUT_GOPRO.md` : tout au futur (« il faut rendre l'adresse
     configurable », « introduire `GoProOptions` », « extraire `IGoProClient` ») alors
     que `GoProOptions`/`IGoProClient`/`HttpGoProClient`/`FakeGoProClient` **existent
     déjà** dans `src/`.
   - `LINUX_MIGRATION_BLOCKERS.md` : un plan de migration phase 1→5 alors que la
     migration est réalisée (app Avalonia testée + image CI). `README.md` le pointe
     pour « architecture, décisions, risques » → piège.
4. **Faits dupliqués à la main sur 4-6 fichiers** (donc dérive garantie) : pins GPIO
   18/20/17 + pull-up + relais ; warning « `AVALONIA_RENDERER=software` no-op / `--drm`
   toujours accéléré » ; piège « Imager → répondre Non » ; commande `dotnet publish` ;
   units systemd + provisioning (la source est `deploy/`, mais le RUNBOOK les
   **reproduit inline**, ce qu'il avoue lui-même).
5. **Frontière floue entre 3 docs « mettre l'app sur un Pi »** : `INSTALLATION_BORNE`
   (flasher), `RUNBOOK` (fabriquer l'image), `DEPLOY_RASPBERRY_PI` (manuel/dev). Chacun
   ouvre par un encart « quel doc utiliser ? » pour se distinguer des deux autres.
6. **Code UWP fantôme** : `CS/`, `GoProWifi/`, `RasberryPiLib/`, `RasberryLib/`,
   `testvlcsharp/` **n'existent plus** (absents du disque, plus aucun fichier suivi par
   git) — mais `AGENTS.md` (~110 lignes), `LINUX_MIGRATION_BLOCKERS.md` (intégral),
   `README_NET8.md` (l.3), `DEPLOY_RASPBERRY_PI.md` (l.14), `TESTING_WITHOUT_GOPRO.md`,
   `tools/gopro-simulator/README.md` (l.30) en parlent encore comme s'ils étaient là.

---

## 2. Objectifs & principes directeurs

- **Une information = un endroit** (source de vérité unique). Tout le reste *lie*, ne
  *recopie* pas.
- **Organisation par public**, calquée sur les 2 publics réels (cf. §3).
- **Électronique séparée du logiciel** (axe transverse demandé).
- **Zéro UWP** : on n'en parle plus nulle part. Pas d'annexe legacy.
- **Zéro contenu obsolète présenté comme factuel** : l'architecture décrit le code
  **réel au présent** ; pas de récit de migration ni de plan « à faire ».
- **`README.md` = page d'accueil + l'unique index** (qui es-tu → va là).

### Garde-fous d'honnêteté (non négociables)

- **Ne rien inventer en électronique.** `1-electronique.md` consolide uniquement ce qui
  existe déjà (pins 18/20/17, pull-up externe ~10 kΩ, avertissement « un GPIO ne pilote
  pas du secteur → relais/MOSFET », MAX44009 optionnel). Les manques `#4` (sécurité
  220 V documentée) et `#30` (schéma + BOM avec composants/prix) du `BACKLOG.md` sont
  **signalés comme à compléter**, jamais fabriqués (pas de schéma inventé, pas de prix
  inventés).
- **`architecture.md` décrit le code réel**, vérifié dans `src/`, pas un ancien plan.

---

## 3. Publics cibles (2, pas 4)

| Public | Qui | Branche doc |
|---|---|---|
| **Le DIYer** | monte la borne (électronique + câblage), prépare et fait tourner les événements. Monteur = préparateur = opérateur. | `docs/monter-et-utiliser/` |
| **Le dev = mainteneur** | code, build/test local, fabrique l'image, déploie, dépanne à distance. | `docs/developper-et-maintenir/` |

Le découpage interne de la branche DIYer suit le **moment de vie**, pas les fichiers de
config (qui se chevauchaient) :

- **Construire** (une seule fois) → `1-electronique.md`
- **Mettre en service** (une seule fois) → `2-installation.md`
- **Préparer un événement** (avant, à la maison ; TOUTE la config + le test) →
  `3-preparer-un-evenement.md`
- **Le jour J** (sur place ; **zéro config**) → `4-le-jour-j.md`

Ce découpage répond à deux exigences explicites : « on ne configure pas le jour J » (la
config et la vérif vivent dans `3-préparer`) et « ne pas se rendre compte trop tard
qu'on a mal configuré » (`2-installation` et `3-préparer` se terminent par un **test
d'acceptation** — le mode test sans GoPro — qu'on ne quitte pas tant qu'il n'est pas
vert). `3-préparer` et `4-le-jour-j` correspondent aux **2 fiches plastifiées** déjà
prévues par le projet (RUNBOOK PHASE 6).

---

## 4. Arborescence cible

```
README.md                          ← accueil + index UNIQUE (qui es-tu → va là)
AGENTS.md                          ← réécrit 100 % src/ ; ZÉRO mention UWP
docs/
  monter-et-utiliser/              (le DIYer)
    1-electronique.md              ← construire le matériel (câblage GPIO, relais, BOM/schéma à compléter)
    2-installation.md              ← flasher l'image + 1er démarrage + test d'acceptation (mode test, sans GoPro)
    3-preparer-un-evenement.md     ← AVANT, à la maison : config (noms/fond/wifi) + mode test jusqu'au vert
    4-le-jour-j.md                 ← SUR PLACE : ordre de branchement → opérer → dépannage rapide → pointeur admin
    config-reference.md            ← SOURCE UNIQUE des champs photobooth.json / wifi.txt
  developper-et-maintenir/         (le dev = mainteneur)
    developpement.md               ← build/test/run local + simulateur + screenshot (ex-README_NET8)
    architecture.md                ← état RÉEL du code (ex-LINUX_MIGRATION_BLOCKERS, réécrit)
    fabrication-image.md           ← fabriquer l'image SD (ex-RUNBOOK ; pointe deploy/ + image-builder/)
    deploiement-manuel.md          ← install manuelle Pi pour dev/debug (ex-DEPLOY_RASPBERRY_PI)
    admin-debug.md                 ← interface web d'admin/debug consolidée (4 sources → 1)
deploy/        README.md           ← INCHANGÉ — source de vérité du kit de déploiement
image-builder/ README.md           ← INCHANGÉ — source de vérité de la fabrication CI
tools/gopro-simulator/ README.md   ← CONSERVÉ, 1 ligne UWP obsolète corrigée (l.30)
BACKLOG.md                         ← INCHANGÉ (backlog, pas doc de référence)
docs/superpowers/                  ← INCHANGÉ (specs/plans de process)
```

---

## 5. Spécification fichier par fichier

### `README.md` (réécrit — accueil + index unique)
- Garde le pitch produit existant (logiciel, pas matériel ; kiosk Pi + GoPro).
- **Un seul tableau d'index**, deux entrées de tête (« Je monte/j'utilise une borne » /
  « Je développe ou je maintiens »), pointant vers les docs de `docs/`.
- Bloc « développeurs en bref » (3 commandes) conservé, pointant `developpement.md`.
- **Supprime** toute table d'index redondante ailleurs (voir `developpement.md`, `AGENTS.md`).

### `docs/monter-et-utiliser/1-electronique.md` (consolidation)
- **Sources** : `INSTALLATION_BORNE.md` §1.1/§1.2, `DEPLOY_RASPBERRY_PI.md` §0.
- **Contenu** : matériel requis (Pi, microSD, écran, alim), câblage GPIO (pins par
  défaut 18/20/17, numérotation BCM), pull-up externe, **avertissement relais/MOSFET
  pour la lumière**, MAX44009 optionnel, comment changer les pins (bloc `Hardware`).
- **Gaps signalés (non fabriqués)** : `#4` (circuit relais 5 V détaillé + DANGER 220 V)
  et `#30` (schéma de câblage + BOM chiffrée) → section « À compléter » renvoyant au
  `BACKLOG.md`. **Aucun composant/prix/schéma inventé.**

### `docs/monter-et-utiliser/2-installation.md` (source : `INSTALLATION_BORNE.md`)
- Obtenir l'image, flasher avec Raspberry Pi Imager (**piège « répondre Non à la
  customisation »** — source de vérité ici, les autres docs y renvoient), 1er démarrage.
- Se termine par un **test d'acceptation en mode test (sans GoPro)** : écran + boutons +
  lumière OK. La partie électronique part dans `1-electronique.md` ; les champs de config
  renvoient à `config-reference.md`.

### `docs/monter-et-utiliser/3-preparer-un-evenement.md` (source : `GUIDE_OPERATEUR.md`)
- **Avant, à la maison** : éditer `photobooth.json` (noms/année/fond), `fond.jpg`,
  `wifi.txt` si autre GoPro — en renvoyant à `config-reference.md` pour le détail des
  champs.
- **Mode test sans GoPro** (dry-run) jusqu'au bandeau vert — le test qu'on refait la veille.
- Règle « on modifie, on ne crée/renomme jamais » + piège extensions cachées Windows.

### `docs/monter-et-utiliser/4-le-jour-j.md` (source : `GUIDE_OPERATEUR.md`)
- **Sur place, zéro config** : ordre de branchement (GoPro → écran → boutons → alim en
  dernier), lecture des bandeaux vert/orange/rouge, dépannage rapide (power-cycle GoPro /
  borne), ranger/éteindre.
- Section courte « dépannage avancé » : **pointeur** vers `developper-et-maintenir/admin-debug.md`
  (ne recopie pas le contenu) + comment activer l'interface si le mainteneur le demande
  (toujours avec PIN).

### `docs/monter-et-utiliser/config-reference.md` (NOUVEAU — source de vérité config)
- Énumère, **une seule fois**, les fichiers de la FAT32 et les sections de
  `photobooth.json` : `Theme`, `Gopro`, `Hardware`, `Timings`, `Printer`, `Admin`,
  `Logging`, `ScreenResolution` — défaut, qui édite (opérateur vs avancé/mainteneur),
  comportement de validation (valeur invalide → bandeau rouge, pas de crash).
- `wifi.txt` : clés exactes `GOPRO_SSID` / `GOPRO_PASSWORD` / `WIFI_COUNTRY` (+ réseau
  secondaire optionnel). `admin.txt` : avancé.
- Lié par `2-installation.md`, `3-preparer-un-evenement.md`, et `admin-debug.md`.

### `docs/developper-et-maintenir/developpement.md` (source : `README_NET8.md`)
- Pré-requis .NET 8, build/test, lancer en local (fake + simulateur + variables
  `PHOTOBOOTH_`), mode `--screenshot`, structure `src/`.
- **Retire** le préambule UWP (« réécriture de l'ancienne app UWP / code dans CS/… »).
- **Retire** sa propre table d'index doc (l'index unique est dans `README.md`).
- Absorbe le noyau utile de `TESTING_WITHOUT_GOPRO.md` (« tester sans GoPro = mode fake
  + simulateur »), le reste de ce fichier étant obsolète.
- Note macOS « lancer depuis une session graphique (Avalonia.Native -6661) » conservée.

### `docs/developper-et-maintenir/architecture.md` (RÉÉCRIT depuis `LINUX_MIGRATION_BLOCKERS.md`)
- Décrit l'**architecture réelle au présent** : projets en couches (`Core` /
  `Adapters` / `Admin` / `App` / `Tests`), workflow acteur (Idle/Capturing/Recording/
  Degraded), abstraction `IGoProClient` + adapters, couche hardware Linux + fakes,
  télémétrie diagnostic.
- **Décisions qui survivent** (ce qui avait de la valeur dans l'ancien doc, reformulé en
  faits présents) : pourquoi Avalonia, rendu **DRM (accéléré) vs FBDev (logiciel)** +
  `AVALONIA_RENDERER=software` no-op en Avalonia 11, self-contained `linux-arm64`,
  trimming off, `systemd Restart=always`.
- **Zéro** : table de blocages UWP, plan phase 1→5, checklist de remplacements UWP.

### `docs/developper-et-maintenir/fabrication-image.md` (source : `RUNBOOK_MAINTENEUR_CARTE_SD.md`)
- Architecture 2 couches, phases de fabrication (golden master / CustoPiZer), validation
  Phase 4 sur Pi réel, PiShrink, distribution.
- **Cesse de reproduire `deploy/` inline** : les units systemd, le script de
  provisioning et les modèles `boot-config/` sont **référencés** depuis `deploy/`
  (source de vérité), pas copiés. Idem la fabrication CI → renvoie à `image-builder/`.
- Les faits partagés (publish, rendu, piège Imager) renvoient à leur source unique
  (`developpement.md`, `architecture.md`, `2-installation.md`).

### `docs/developper-et-maintenir/deploiement-manuel.md` (source : `DEPLOY_RASPBERRY_PI.md`)
- Install manuelle Pi pour **dev/debug** (scp + systemd à la main, paquets, imprimante).
- **Retire** la mention UWP (l.14). Renvoie à `architecture.md` (rendu) et
  `fabrication-image.md` (chemin de distribution) au lieu de redupliquer.

### `docs/developper-et-maintenir/admin-debug.md` (NOUVEAU — consolidation)
- **Sources éclatées aujourd'hui** : `README_NET8.md` §admin, `RUNBOOK` §3.5,
  `deploy/README.md` point 1, `docs/superpowers/specs/2026-06-23-admin-debug-*`.
- **Contenu** : à quoi sert l'hôte web (Kestrel, opt-in `Admin.Enabled=false` par
  défaut), table des clés `Admin`, ce qu'il expose en lecture/écriture, **modèle de
  menace** (PIN = seule frontière réseau↔root, CSRF, SameSite, audit-log), privilèges
  `sudoers`/`photobooth-write-config.sh`, activation sur borne déployée.
- La part opérateur (activer sur demande + PIN) reste **pointée** depuis `4-le-jour-j.md`.

---

## 6. Table de migration (ancien → nouveau)

| Ancien | Action | Nouveau |
|---|---|---|
| `README.md` | réécrit (index unique) | `README.md` |
| `README_NET8.md` | `git mv` + nettoyage UWP/index | `docs/developper-et-maintenir/developpement.md` |
| `INSTALLATION_BORNE.md` | scinder | `docs/monter-et-utiliser/1-electronique.md` (élec) + `2-installation.md` (reste) |
| `GUIDE_OPERATEUR.md` | scinder par moment de vie | `docs/monter-et-utiliser/3-preparer-un-evenement.md` + `4-le-jour-j.md` |
| (extrait des champs de config, aujourd'hui dupliqués) | consolider | `docs/monter-et-utiliser/config-reference.md` (NOUVEAU) |
| `DEPLOY_RASPBERRY_PI.md` | `git mv` + nettoyage UWP | `docs/developper-et-maintenir/deploiement-manuel.md` |
| `RUNBOOK_MAINTENEUR_CARTE_SD.md` | `git mv` + dé-duplication vers `deploy/` | `docs/developper-et-maintenir/fabrication-image.md` |
| `LINUX_MIGRATION_BLOCKERS.md` | **réécrit** (état réel) | `docs/developper-et-maintenir/architecture.md` |
| `AGENTS.md` | **réécrit** 100 % src/ | `AGENTS.md` (reste à la racine) |
| (sources admin éclatées) | consolider | `docs/developper-et-maintenir/admin-debug.md` (NOUVEAU) |
| `TESTING_WITHOUT_GOPRO.md` | **supprimé** (noyau absorbé dans `developpement.md`) | — |
| `deploy/README.md`, `image-builder/README.md`, `tools/gopro-simulator/README.md`, `BACKLOG.md` | inchangés (sauf 1 ligne UWP dans le simulateur) | idem |

---

## 7. Purge UWP (liste exacte)

À supprimer/réécrire de sorte qu'aucune mention UWP ne subsiste hors `docs/superpowers/`
(qui est de l'archive de process et reste tel quel) :

- `AGENTS.md` : réécriture intégrale (lignes 5, 11, 19, 27-39, 45, 70, 76, 89, 99-106,
  115, 134, 138, 141 et toutes les sections legacy).
- `LINUX_MIGRATION_BLOCKERS.md` : remplacé par `architecture.md` (aucune ligne UWP reprise).
- `README_NET8.md` l.3 ; `DEPLOY_RASPBERRY_PI.md` l.14 ; `tools/gopro-simulator/README.md` l.30.
- `TESTING_WITHOUT_GOPRO.md` (l.27, 31, 120…) : supprimé.

**Vérification d'acceptation** : `grep -rniE 'uwp|GoProWifi|RasberryPiLib|RasberryLib|testvlcsharp|appxmanifest|Windows 10 IoT' --include='*.md' .` (hors `docs/superpowers/`) doit ne **rien** retourner.

---

## 8. Réécriture des liens internes

Après les `git mv`, réécrire tous les liens markdown inter-docs (recensés) :
`image-builder/README.md` (×6), `RUNBOOK_…` (×5 + ×3 relatifs), `INSTALLATION_BORNE` (×5),
`GUIDE_OPERATEUR` (×4 + ×1), `README_NET8` (×2), `LINUX_MIGRATION_BLOCKERS` (×2),
`DEPLOY_RASPBERRY_PI` (×2), `TESTING_WITHOUT_GOPRO` (×1, à retirer), `deploy/README` (×1).
Inclut les liens depuis `deploy/README.md` et `image-builder/README.md` vers
`../RUNBOOK_…` et `../GUIDE_OPERATEUR` → repointer vers les nouveaux chemins `docs/…`.

**Vérification d'acceptation** : aucun lien `.md` mort (script de check des liens
relatifs sur tous les `*.md`).

---

## 9. Hors périmètre

- Pas de site de doc généré (mkdocs/docusaurus).
- Aucune modification de code `src/` ni des scripts `deploy/` / `image-builder/`.
- `BACKLOG.md`, `docs/superpowers/` : inchangés.
- On ne **traite pas** les items du backlog (#4/#30 électronique, etc.) — on les
  **signale** seulement là où c'est pertinent.

---

## 10. Critères d'acceptation

1. `README.md` contient **le seul** index ; aucune autre doc ne re-liste l'ensemble.
2. Arborescence `docs/monter-et-utiliser/` (5 fichiers) + `docs/developper-et-maintenir/`
   (5 fichiers) conforme au §4.
3. `grep` UWP (§7) ne retourne rien hors `docs/superpowers/`.
4. Aucun lien `.md` mort ; tous les renvois pointent les nouveaux chemins.
5. Un fait partagé (pins, rendu DRM, piège Imager, publish, units systemd) a **une seule
   source** ; les autres docs y renvoient.
6. `architecture.md` ne contient aucun plan de migration ni table de blocages — uniquement
   l'état réel + décisions.
7. `1-electronique.md` n'invente ni schéma ni BOM ; les gaps #4/#30 sont signalés.
8. `4-le-jour-j.md` ne contient aucune étape de configuration.
