# Installation d'une borne — de l'image à la borne qui tourne

> **Pour qui** : la personne qui **construit/installe une borne** sur son propre
> Raspberry Pi à partir de l'image logicielle (`photobooth-dist.img.xz`).
> **Ce projet distribue un LOGICIEL, pas un produit matériel** : vous fournissez et
> assemblez vous-même le matériel (Pi, écran, boutons, lumière éventuelle, GoPro,
> câblage). Profil : à l'aise avec un PC ; le câblage GPIO demande un minimum de
> bricolage électronique (voir §1.1).
> **Résultat** : une carte SD qui, une fois insérée et branchée, démarre **seule**
> en plein écran et est prête dès le bandeau vert.
>
> Autres docs : le **build** de l'image → [`RUNBOOK_MAINTENEUR_CARTE_SD.md`](RUNBOOK_MAINTENEUR_CARTE_SD.md)
> et [`image-builder/README.md`](image-builder/README.md). L'usage **événement**
> (changer noms/fond/Wi-Fi, dépannage sur place) → [`GUIDE_OPERATEUR.md`](GUIDE_OPERATEUR.md).

---

## Vue d'ensemble (4 étapes)

```
1. Obtenir l'image  ->  2. Flasher la carte SD  ->  3. Configurer la carte     ->  4. Premier démarrage
   (.img.xz)             (Raspberry Pi Imager)        (sur le PC, AVANT le boot)     (déjà prête : bandeau vert)
```

Tout ce qui est « système » est déjà figé dans l'image. Vous n'installez **rien**
à la main sur le Pi (pas de SSH, pas d'`apt`, pas de copie de fichiers) : l'image
est *turnkey*.

> **Le point clé** : la partition de configuration est **lisible sur votre PC**
> dès que la carte est flashée. On règle donc le **Wi-Fi de la GoPro** et le thème
> **avant** d'insérer la carte dans le Pi → la borne arrive **verte au tout premier
> démarrage**, sans aucun terminal. (On peut aussi reconfigurer plus tard de la
> même façon : sortir la carte, l'éditer sur le PC, la remettre.)

---

## 1. Matériel requis (vous le fournissez)

Nous fournissons l'**image logicielle** ; **tout le matériel ci-dessous est à
vous** :

| Élément | Détail |
|---|---|
| **Raspberry Pi** | Pi **3 / 3B+ / 3A+**, **4 / 400**, ou **Zero 2 W**. **Pi 5** = à valider séparément. **Ne marche pas** sur Pi 1 / Zero / Zero W (processeur trop ancien). |
| **Carte microSD** | **16 Go** recommandé (8 Go minimum), microSD de marque (l'endurance compte). |
| **Lecteur de carte** | Fente SD du PC ou adaptateur USB. |
| **Écran** | Entrée **HDMI** + son câble. |
| **GoPro** | En Wi-Fi (allumage/veille : voir le guide opérateur). |
| **Boutons / lumière** | Bouton photo, bouton vidéo (+ lumière optionnelle), câblés sur le header GPIO → **§1.1**. |
| **Alimentation** | L'alim **officielle** du Pi (sous-alimenter = source n°1 d'instabilité). |

> **Compatibilité** : le **même** `.img.xz` boote sur Pi 3 et Pi 4. Il faut juste
> que le **modèle visé ait été validé une fois** par le mainteneur (rendu, boutons,
> GoPro). En cas de doute sur le modèle, demandez au mainteneur.

### 1.1 Câblage des boutons et de la lumière (GPIO)

Numérotation **BCM** (les numéros « GPIO », pas la position physique des broches).
Broches **par défaut** :

| Fonction | Broche (BCM) par défaut | Câblage |
|---|---|---|
| Bouton **photo** | **GPIO 18** | bouton entre la broche et **GND** (masse). |
| Bouton **vidéo** | **GPIO 20** | bouton entre la broche et **GND**. |
| **Lumière** (optionnelle) | **GPIO 17** | sortie **active-high** (HIGH = allumé). |

- **Pull-up externe ~10 kΩ recommandé** sur GPIO 18 et 20 (vers 3,3 V) : le pull-up
  interne du Pi 3 est peu fiable. Le bouton tire la broche à la masse quand on
  appuie.
- ⚠️ **Lumière** : une broche GPIO **ne pilote pas** une lampe directement (3,3 V,
  quelques mA). Passez par un **relais ou un MOSFET** commandé par GPIO 17. Sans
  lumière ? Voir `LightEnabled=false` en §1.2.
- (Optionnel) **capteur de lumière MAX44009** sur I2C bus 1 (GPIO2/SDA, GPIO3/SCL),
  adresse `0x4A` — désactivé par défaut.

### 1.2 Changer les broches GPIO (si votre câblage diffère)

Les broches sont **configurables** sans recompiler : tout est dans le bloc
`Hardware` du fichier **`photobooth.json`** (sur la carte SD, voir §4). Adaptez-le
à VOTRE câblage :

```jsonc
"Hardware": {
  "Mode": "auto",        // auto = GPIO reels sur Pi ; fake = clavier/sans GPIO
  "PhotoButtonPin": 18,   // broche BCM du bouton photo
  "VideoButtonPin": 20,   // broche BCM du bouton vidéo
  "LightEnabled": true,   // false = borne SANS lumière (la broche n'est jamais ouverte)
  "LightPin": 17          // broche BCM de la sortie lumière (ignorée si LightEnabled=false)
}
```

- Valeurs BCM **0 à 27**, **sans doublon**. Une valeur invalide n'empêche **pas**
  le démarrage : la borne affiche un **bandeau rouge** à l'écran (pas de crash).
- `Mode: "auto"` → comportement normal : les GPIO réels sont utilisés sur Raspberry Pi.
  `Mode: "fake"` → aucun GPIO n'est ouvert ; utile pour tester l'interface au clavier.
- `LightEnabled: false` → borne **sans lumière** : tout fonctionne normalement, la
  broche lumière n'est jamais utilisée.
- Ce bloc est marqué *avancé* dans le fichier : l'**opérateur** d'événement n'y
  touche pas ; c'est **vous, au montage**, qui l'ajustez une fois.

---

## 2. Obtenir l'image `photobooth-dist.img.xz`

Deux façons, selon votre rôle :

**A. On vous l'a fournie / via les Releases GitHub (le plus simple).**
Récupérez le fichier `photobooth-dist.img.xz` depuis la
[page *Releases* du dépôt](https://github.com/chibiliplop/photo-booth/releases/latest)
(asset de la dernière version), ou sur la clé USB fournie par le mainteneur.
C'est tout — passez directement à l'**étape 3** (flashage).

**B. Vous êtes le mainteneur et devez la construire.**
- En CI : *Actions* → *Build SD image* → *Run workflow* (ou pousser un tag `v*`),
  puis téléchargez l'artefact / l'asset de Release.
- En local : `image-builder/build-local.sh` (WSL2/Linux + Docker).
- Détails : [`image-builder/README.md`](image-builder/README.md), RUNBOOK §10.

> **N'extrayez pas le `.xz`** : Raspberry Pi Imager (et Balena Etcher) le
> décompressent à la volée.

---

## 3. Flasher la carte SD avec Raspberry Pi Imager

### 3.1 Installer l'outil

Téléchargez **Raspberry Pi Imager** sur <https://www.raspberrypi.com/software/>
(Windows / macOS / Linux), installez-le, lancez-le. Insérez la carte microSD.

> ⚠️ **Le flashage EFFACE entièrement la carte.** Vérifiez qu'elle ne contient
> rien d'important.

### 3.2 Choisir l'image personnalisée

1. Cliquez **« Choisir l'OS »** (*Choose OS*).
2. Tout en bas de la liste, cliquez **« Utiliser une image personnalisée »**
   (*Use custom*).
3. Sélectionnez votre fichier **`photobooth-dist.img.xz`**.

### 3.3 Choisir la carte

1. Cliquez **« Choisir le stockage »** (*Choose Storage*).
2. Sélectionnez **votre carte microSD** (vérifiez bien la taille / le nom : ne
   vous trompez pas de disque !).

### 3.4 ⚠️ Étape CRITIQUE — refuser la « personnalisation de l'OS »

Quand vous cliquez **« Suivant »** / **« Écrire »**, Imager demande souvent :

> *« Voulez-vous appliquer les réglages de personnalisation de l'OS ? »*
> (*Would you like to apply OS customisation settings?*)

**Répondez « NON, effacer les réglages »** (*No, clear settings*).

**Pourquoi c'est important** : l'image est déjà complète et autonome. Si vous
laissez Imager injecter du Wi-Fi / SSH / nom d'hôte ici, il crée un profil réseau
parasite (`preconfigured`) qui peut **fuiter votre Wi-Fi** dans la carte et
**perturber la connexion à la GoPro**, et il peut écraser des réglages de l'image.
Imager **mémorise** ses derniers réglages et peut les **ré-appliquer en silence** :
d'où l'importance de bien cliquer « Non ».

> Filet de sécurité : même si ça arrive, la borne **purge ce profil à chaque
> démarrage**. Mais prenez l'habitude de répondre « Non » — c'est plus propre.

### 3.5 Écrire et vérifier

1. Confirmez l'effacement → Imager **écrit** puis **vérifie** l'image.
2. À la fin, retirez la carte proprement.

> **Alternative sans piège : [Balena Etcher](https://etcher.balena.io/)**, qui ne
> propose **aucune** personnalisation. Choisir le `.img.xz`, la carte, *Flash*.

---

## 4. Configurer la carte AVANT le premier démarrage (recommandé)

C'est tout l'intérêt de l'architecture : la **partition de configuration est
lisible sur votre PC** dès le flashage. En la réglant **avant** d'insérer la carte
dans le Pi, la borne démarre **déjà prête** (bandeau vert) au premier boot — aucun
terminal, aucune manip sur le Pi.

> Après l'écriture, Raspberry Pi Imager **éjecte** la carte. **Retirez-la puis
> ré-insérez-la** dans le PC : une partition amovible apparaît, nommée **`bootfs`**.
> L'autre partition (le système Linux) reste **invisible sous Windows** — c'est
> normal. ⚠️ Si Windows propose de **formater** un disque inconnu, cliquez
> **Annuler** (c'est la partition Linux, on n'y touche pas).

Ouvrez le lecteur **`bootfs`**, puis le dossier **`photobooth`**. Vous y trouverez :

| Fichier | À régler | Indispensable ? |
|---|---|---|
| **`wifi.txt`** | **SSID + mot de passe de la GoPro** | **Oui** — sinon la borne ne trouve pas la GoPro |
| `photobooth.json` | Noms, année ; mode `http` (réel) ou `fake` (démo). Contient aussi le bloc **`Hardware`** (broches GPIO → §1.2) | recommandé |
| `fond.jpg` | Remplacer par votre image (garder ce nom exact) | optionnel |
| `admin.txt` | **Avancé** : mot de passe SSH `pi` (voir §6) | non, l'opérateur n'y touche pas |
| `LISEZ-MOI.txt` | notice (ne pas éditer) | — |

### Le Wi-Fi de la GoPro (le point clé pour un boot vert)

Ouvrez **`wifi.txt`** (Bloc-notes) et renseignez le réseau de **votre** GoPro,
après le `=` :

```
GOPRO_SSID=GP12345678
GOPRO_PASSWORD=le-mot-de-passe-de-ma-gopro
WIFI_COUNTRY=FR
```

Au **premier démarrage**, le service de provisioning lit ce fichier et crée tout
seul la connexion Wi-Fi (avec réessais infinis si la GoPro est rallumée plus
tard). **Rien à taper sur le Pi.** Si vous laissez les valeurs d'exemple
(`GP12345678`…), la borne démarre quand même mais reste en **bandeau orange**
jusqu'à ce que vous corrigiez `wifi.txt`.

> Réseau secondaire (box maison) facultatif : décommentez `WIFI_SSID` /
> `WIFI_PASSWORD` dans `wifi.txt` (utile pour les tests avec Internet, ou pour
> joindre la borne en SSH — voir §6).

> **Règles** : on **modifie** les fichiers existants, on n'en **crée**/ne
> **renomme** aucun. Le pas-à-pas complet (mode test sans GoPro, pièges
> d'extensions cachées sous Windows) est dans
> [`GUIDE_OPERATEUR.md`](GUIDE_OPERATEUR.md) — **le** guide à donner à l'exploitant.

Quand c'est fait, éjectez proprement la carte (« Retirer en toute sécurité »).

---

## 5. Premier démarrage

1. Insérez la carte (déjà configurée) dans le Pi.
2. Branchez dans l'**ordre** (détaillé dans le guide opérateur) :
   **GoPro allumée d'abord → écran HDMI → boutons → alimentation du Pi en
   DERNIER.**
3. **Patientez ~1 à 2 minutes** au tout premier démarrage : l'image **agrandit
   automatiquement** sa partition pour remplir la carte, applique le Wi-Fi lu dans
   `wifi.txt`, puis lance le kiosk plein écran.
4. **Bandeau VERT** = GoPro connectée, borne **prête**. Orange/rouge → dépannage
   du guide opérateur.

Tout est automatique : expansion de la carte, connexion Wi-Fi GoPro, démarrage du
kiosk, relance auto en cas de crash. Vous n'avez **aucune** commande à taper.

> Pour reconfigurer **plus tard** : éteindre (couper le courant), sortir la carte,
> l'éditer sur le PC (comme au §4), la remettre. Les changements s'appliquent au
> redémarrage suivant.

---

## 6. (Avancé / mainteneur) Accès SSH et mot de passe

L'opérateur n'a **jamais** besoin de SSH (il ne touche qu'aux fichiers de la
carte). SSH est réservé au mainteneur.

- **Compte** : `pi`. **Mot de passe** : un défaut figé à la fabrication (souvent
  `raspberry`).
- **Changer le mot de passe de façon durable** : éditez `admin.txt` sur la carte
  (décommentez `PI_PASSWORD=...`). Il est réappliqué **à chaque démarrage**.
  ⚠️ Un `passwd` fait en SSH **ne persiste pas** (l'image a un système en lecture
  seule) — passez **toujours** par `admin.txt`.
- **Trouver la borne sur le réseau** : la résolution `.local` (mDNS/avahi) est
  **désactivée** pour accélérer le boot → utilisez l'**adresse IP**. Le plus
  simple : renseignez un réseau secondaire dans `wifi.txt` (`WIFI_SSID` /
  `WIFI_PASSWORD`) pointant vers un réseau **avec box/routeur**, puis relevez l'IP
  de la borne dans l'interface de la box. Sur le réseau GoPro seul (isolé,
  10.5.5.x), il n'y a pas d'Internet ni de DNS pratique.

---

## 7. Mettre à jour

- **Nouvelle image complète** (l'OS ou l'app a changé) : reflashez la carte
  (étape 3). ⚠️ Le reflashage **efface la carte** → les réglages d'événement
  (noms, fond, Wi-Fi) repartent des modèles : reconfigurez via l'**étape 4**.
- **Mise à jour de l'app seulement, sans reflasher** (opération mainteneur en
  SSH) : voir la section **« MISE À JOUR DE L'APP »** du
  [RUNBOOK](RUNBOOK_MAINTENEUR_CARTE_SD.md).

---

## 8. Dépannage de l'installation

| Symptôme | Piste |
|---|---|
| **Écran noir au 1ᵉʳ boot** | Patientez 1-2 min (expansion). Vérifiez écran allumé + **bonne entrée HDMI**, câble enfoncé, **alim officielle** (la sous-alimentation cause des écrans noirs). |
| **La carte n'est pas reconnue / écriture échoue** | Re-formatez via Imager, essayez un autre lecteur/une autre carte. |
| **Bandeau orange/rouge** | C'est côté GoPro/Wi-Fi, pas l'installation → guide opérateur (allumer/rallumer la GoPro, vérifier `wifi.txt`). |
| **Wi-Fi maison s'est connecté tout seul** | Vous avez laissé la customisation Imager active (§3.4). Reflashez en répondant **« Non »**, ou laissez la borne purger le profil au boot. |
| **Ça ne boote pas du tout sur ce Pi** | Modèle non supporté/non validé (cf. §1) — confirmez le modèle avec le mainteneur. **Pi 5** doit être validé à part. |
| **Comportement bizarre, besoin de logs** | Accès SSH (§6) puis `journalctl -u photobooth -f` et `journalctl -u photobooth-provision`. |

---

> **En une phrase** : récupérez le `.img.xz`, flashez-le avec Raspberry Pi Imager
> **en refusant la personnalisation OS**, **réglez `wifi.txt` (Wi-Fi GoPro) et le
> thème sur la carte depuis le PC**, puis insérez-la et branchez **GoPro → écran →
> boutons → alim en dernier** : la borne arrive **verte** au premier démarrage.
> Tout le reste est automatique.
