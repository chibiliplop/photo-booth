# Installation d'une borne — de l'image à la borne qui tourne

> **Pour qui** : la personne qui **installe une borne** sur un Raspberry Pi à
> partir de l'image distribuable (`photobooth-dist.img.xz`). Profil : à l'aise
> avec un PC, pas forcément développeur.
> **Résultat** : une carte SD qui, une fois insérée et branchée, démarre **seule**
> en plein écran et est prête dès le bandeau vert.
>
> Autres docs : le **build** de l'image → [`RUNBOOK_MAINTENEUR_CARTE_SD.md`](RUNBOOK_MAINTENEUR_CARTE_SD.md)
> et [`image-builder/README.md`](image-builder/README.md). L'usage **événement**
> (changer noms/fond/Wi-Fi, dépannage sur place) → [`GUIDE_OPERATEUR.md`](GUIDE_OPERATEUR.md).

---

## Vue d'ensemble (4 étapes)

```
1. Obtenir l'image  ->  2. Flasher la carte SD  ->  3. Premier démarrage  ->  4. Configurer l'événement
   (.img.xz)             (Raspberry Pi Imager)        (câblage + bandeau vert)   (fichiers sur la carte)
```

Tout ce qui est « système » est déjà figé dans l'image. Vous n'installez **rien**
à la main sur le Pi (pas de SSH, pas d'`apt`, pas de copie de fichiers) : l'image
est *turnkey*.

---

## 1. Matériel requis

| Élément | Détail |
|---|---|
| **Raspberry Pi** | Pi **3 / 3B+ / 3A+**, **4 / 400**, ou **Zero 2 W**. **Pi 5** = à valider séparément. **Ne marche pas** sur Pi 1 / Zero / Zero W (processeur trop ancien). |
| **Carte microSD** | **16 Go** recommandé (8 Go minimum), microSD de marque (l'endurance compte). |
| **Lecteur de carte** | Fente SD du PC ou adaptateur USB. |
| **Écran** | Entrée **HDMI** + son câble. |
| **GoPro** | En Wi-Fi (voir le guide opérateur pour l'allumage/veille). |
| **Boutons / lumière** | Photo, vidéo (+ lumière optionnelle), câblés sur le header GPIO. Câblage = guide opérateur. |
| **Alimentation** | L'alim officielle du Pi (sous-alimenter = source n°1 d'instabilité). |

> **Compatibilité** : le **même** `.img.xz` boote sur Pi 3 et Pi 4. Il faut juste
> que le **modèle visé ait été validé une fois** par le mainteneur (rendu, boutons,
> GoPro). En cas de doute sur le modèle, demandez au mainteneur.

---

## 2. Obtenir l'image `photobooth-dist.img.xz`

Deux façons, selon votre rôle :

**A. On vous l'a fournie / via les Releases GitHub (le plus simple).**
Récupérez le fichier `photobooth-dist.img.xz` (page *Releases* du dépôt, ou clé
USB fournie par le mainteneur). C'est tout — passez directement à l'**étape 3**
(flashage).

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

## 4. Premier démarrage

1. Insérez la carte dans le Pi.
2. Branchez dans l'**ordre** (détaillé dans le guide opérateur) :
   **GoPro allumée d'abord → écran HDMI → boutons → alimentation du Pi en
   DERNIER.**
3. **Patientez ~1 à 2 minutes** au tout premier démarrage : l'image **agrandit
   automatiquement** sa partition pour remplir la carte, applique le Wi-Fi de la
   GoPro, puis lance le kiosk plein écran.
4. **Bandeau VERT** = GoPro connectée, borne **prête**. Orange/rouge → dépannage
   du guide opérateur.

Ce qui se passe tout seul, sans intervention : expansion de la carte, connexion
Wi-Fi GoPro (réessais infinis), démarrage automatique du kiosk, relance auto en
cas de crash. Vous n'avez **aucune** commande à taper.

---

## 5. Configurer l'événement (sur la carte, depuis un PC)

Une fois la carte flashée (ou ré-insérée dans le PC), la **partition visible sous
Windows/Mac** contient un dossier **`photobooth`** avec les fichiers à éditer :

| Fichier | À quoi ça sert |
|---|---|
| `photobooth.json` | Noms, année, fond, et mode (`http` réel / `fake` démo). |
| `fond.jpg` | Image de fond (remplacez le fichier en gardant ce nom exact). |
| `wifi.txt` | Nom (SSID) + mot de passe **de votre GoPro**. |
| `LISEZ-MOI.txt` | Notice opérateur (rappels). |
| `admin.txt` | **Avancé** : mot de passe SSH `pi` (voir §6). L'opérateur n'y touche pas. |

> **Le pas-à-pas détaillé pour l'opérateur** (toujours *modifier*, jamais créer
> ni renommer ; mode test sans GoPro ; pièges Windows) est dans
> [`GUIDE_OPERATEUR.md`](GUIDE_OPERATEUR.md). Donnez **ce** guide à la personne qui
> exploite la borne.

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
  (noms, fond, Wi-Fi) repartent des modèles : reconfigurez via l'étape 5.
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
> **en refusant la personnalisation OS**, branchez **GoPro → écran → boutons →
> alim en dernier**, attendez le **bandeau vert**, puis réglez l'événement sur la
> carte. Tout le reste est automatique.
