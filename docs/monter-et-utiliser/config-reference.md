# Référence de configuration

> Source unique des fichiers éditables sur la carte SD (partition FAT32
> `/boot/firmware/photobooth/`). Les autres docs renvoient ici plutôt que
> de dupliquer les valeurs.

## Fichiers de la carte

| Fichier | Rôle | Édité par |
|---|---|---|
| `wifi.txt` | réseau Wi-Fi de la GoPro (et réseau secondaire optionnel) | opérateur |
| `photobooth.json` | thème + comportement de la borne | opérateur (Theme, Gopro) / avancé (Hardware, Printer, Admin) |
| `fond.jpg` | image de fond personnalisée (déposée à côté de `photobooth.json`) | opérateur |
| `admin.txt` | mot de passe SSH `pi` (override, géré par le script de déploiement) | mainteneur |

---

## `wifi.txt`

Fichier de texte brut, une clé par ligne, format `CLE=valeur`.

| Clé | Obligatoire | Description |
|---|---|---|
| `GOPRO_SSID` | oui | SSID du réseau Wi-Fi émis par la GoPro |
| `GOPRO_PASSWORD` | oui | Mot de passe de ce réseau |
| `WIFI_COUNTRY` | oui | Code pays ISO 3166-1 alpha-2 (ex : `FR`, `BE`, `CH`, `US`) — obligatoire pour débloquer la radio sur Raspberry Pi OS |
| `WIFI_SSID` | non | SSID d'un réseau secondaire (box de la maison, tests avec Internet) — ligne en commentaire par défaut |
| `WIFI_PASSWORD` | non | Mot de passe du réseau secondaire |

---

## `photobooth.json` — sections

### `Theme` — personnalisation visuelle de l'événement

Édité par l'**opérateur** avant chaque événement.

| Champ | Défaut (`appsettings.json`) | Qui édite | Effet | Validation |
|---|---|---|---|---|
| `Names` | `"Replace with name"` | opérateur | Noms affichés sur l'écran d'accueil | aucune contrainte |
| `Year` | `"2026"` | opérateur | Année affichée sur l'écran d'accueil | aucune contrainte |
| `BackgroundImage` | `"avares://Photobooth.App/Assets/background.jpg"` | opérateur | Chemin de l'image de fond — laisser `""` pour garder l'image par défaut embarquée ; pour une image personnalisée, déposer `fond.jpg` sur la carte et mettre `/boot/firmware/photobooth/fond.jpg` | aucune contrainte (fichier absent = fond noir) |
| `FakePhotoImage` | `"avares://Photobooth.App/Assets/photo01.jpg"` | avancé | Image utilisée comme fausse photo quand `Gopro.Mode=fake` | aucune contrainte |
| `FontFamily` | `"avares://Photobooth.App/Assets/Wedding.ttf#Wedding Script"` | avancé | Famille de police affichée sur la carte souvenir | aucune contrainte |
| `CardColor` | `"#f4ecdf"` | avancé | Couleur de fond de la carte souvenir (hex ARGB/RGB) | aucune contrainte |
| `TextColor` | `"#FFFFFF"` | avancé | Couleur du texte principal (hex) | aucune contrainte |
| `AccentColor` | `"#000000"` | avancé | Couleur d'accentuation (hex) | aucune contrainte |
| `ScreenResolution` | `"1280x720"` | opérateur | Canvas de design de l'interface au format `LARGEURxHAUTEUR` ; l'UI est mise à l'échelle (Viewbox) pour remplir l'écran HDMI réel | Format invalide ou dimension hors 320–7680 → **bandeau rouge**, démarrage en mode dégradé (fallback 1280×720) |

### `Gopro` — connexion à la caméra

Édité par l'**opérateur** (champ `Mode`) ; les autres champs sont réservés à un usage avancé/mainteneur.

| Champ | Défaut (`appsettings.json`) | Qui édite | Effet | Validation |
|---|---|---|---|---|
| `Mode` | `"fake"` | opérateur | `"http"` = vraie GoPro pilotée en Wi-Fi ; `"fake"` = mode démo sans GoPro (fausses photos) — **repasser sur `"http"` avant le vrai événement** | aucune contrainte de format |
| `ControlBaseUrl` | `"http://10.5.5.9"` | mainteneur | URL de l'API de contrôle GoPro | obligatoire, ne doit pas être vide |
| `MediaBaseUrl` | `"http://10.5.5.9:8080"` | mainteneur | URL du serveur média GoPro (téléchargement des photos) | obligatoire, ne doit pas être vide |
| `KeepAliveHost` | `"10.5.5.9"` | mainteneur | IP cible des ping TCP keep-alive | aucune contrainte |
| `KeepAlivePort` | `8554` | mainteneur | Port TCP keep-alive | doit être compris entre 1 et 65535 |
| `KeepAliveIntervalSeconds` | `5` | mainteneur | Intervalle entre deux keep-alive (secondes) | aucune contrainte |
| `RequestTimeoutSeconds` | `3` | mainteneur | Délai maximal d'une requête HTTP GoPro (secondes) | doit être > 0 |
| `CaptureDeadlineSeconds` | `15` | mainteneur | Budget total pour récupérer la photo après déclenchement (secondes) | doit être > 0 |
| `MaxRetries` | `6` | mainteneur | Nombre max de tentatives par appel GoPro (503/timeout) | doit être > 0 |
| `RetryBackoffMs` | `500` | mainteneur | Délai entre deux tentatives (millisecondes) | aucune contrainte |

### `Hardware` — câblage GPIO

Édité par les **constructeurs de borne** uniquement. Ne pas modifier sur une borne déjà montée.

Tous les numéros de broches sont au format **BCM** (de 0 à 27), comme sur le schéma de branchement.

| Champ | Défaut (`appsettings.json`) | Qui édite | Effet | Validation |
|---|---|---|---|---|
| `Mode` | `"auto"` | mainteneur | `"auto"` = GPIO réels si `/dev/gpiochip0` présent, sinon clavier/fake ; `"linux"` = force GPIO réels ; `"fake"` = force clavier sans GPIO | aucune contrainte de format |
| `PhotoButtonPin` | `18` | avancé | Broche BCM du bouton PHOTO | doit être 0–27, pas en doublon |
| `VideoButtonPin` | `20` | avancé | Broche BCM du bouton VIDEO | doit être 0–27, pas en doublon |
| `PrintButtonEnabled` | `false` | avancé | `true` = un 3e bouton dédié à l'impression est câblé | aucune contrainte |
| `PrintButtonPin` | `21` | avancé | Broche BCM du bouton IMPRESSION (ignorée si `PrintButtonEnabled=false`) | doit être 0–27, pas en doublon (seulement si activé) |
| `LightEnabled` | `true` | avancé | `true` = une lumière/relais est câblé sur `LightPin` ; `false` = aucune lumière, workflow sans éclairage | aucune contrainte |
| `LightPin` | `17` | avancé | Broche BCM de la sortie LUMIÈRE (ignorée si `LightEnabled=false`) | doit être 0–27, pas en doublon (seulement si activé) |
| `ButtonDebounceMs` | `80` | mainteneur | Anti-rebond boutons (millisecondes) | doit être >= 0 |
| `I2cBus` | `1` | mainteneur | Numéro de bus I2C (capteur de lumière) | doit être >= 0 |
| `LightSensorAddress` | `"0x4A"` | mainteneur | Adresse I2C du capteur de lumière (hex) | aucune contrainte |
| `LightSensorEnabled` | `false` | mainteneur | Active la lecture du capteur de lumière I2C | aucune contrainte |

### `Timings` — temporisations

Édité par un **mainteneur** pour ajuster le rythme de la borne sans recompiler.

| Champ | Défaut (`appsettings.json`) | Qui édite | Effet | Validation |
|---|---|---|---|---|
| `PoseMs` | `2000` | mainteneur | Durée de la fenêtre de pose après le dernier décompte (ms) | aucune contrainte |
| `CountdownStepMs` | `1000` | mainteneur | Durée d'un pas de décompte (ms) | doit être > 0 |
| `LightSettleMs` | `1000` | mainteneur | Délai après allumage de la lumière avant déclenchement (ms) | aucune contrainte |
| `PhotoDisplayMs` | `5000` | mainteneur | Durée d'affichage de la photo capturée (ms) | aucune contrainte |
| `VideoCountdownSeconds` | `3` | mainteneur | Durée du décompte avant le tournage vidéo (0 = désactivé) | doit être >= 0 |
| `VideoMaxSeconds` | `10` | mainteneur | Durée maximale d'une vidéo (secondes) | doit être > 0 |
| `SlideshowIntervalSeconds` | `5` | mainteneur | Intervalle entre deux photos du diaporama (secondes) | doit être > 0 |
| `StatusPollSeconds` | `3` | mainteneur | Fréquence de sondage de la connectivité GoPro (secondes) | doit être > 0 |
| `WatchdogSeconds` | `30` | mainteneur | Délai maximal d'une action photo/vidéo avant reset forcé (secondes) | doit être > 0 |

### `Printer` — impression

Édité par l'**opérateur** (champs `Type`, `Media`) ; les autres champs sont avancés.

| Champ | Défaut (`appsettings.json`) | Qui édite | Effet | Validation |
|---|---|---|---|---|
| `Type` | `"disabled"` | opérateur | `"disabled"` = pas d'impression ; `"cups"` = impression via CUPS/lp ; `"file"` = export JPEG dans un dossier | doit valoir `disabled`, `cups` ou `file` |
| `TriggerMode` | `"manual"` | opérateur | `"photo-button-window"` = le bouton PHOTO imprime pendant X secondes après la capture (recommandé) ; `"auto"` = impression automatique ; `"manual"` = bouton impression séparé (`Hardware.PrintButtonEnabled=true`) | doit valoir `manual`, `auto` ou `photo-button-window` |
| `PhotoButtonPrintWindowSeconds` | `15` | opérateur | Durée de la fenêtre d'impression en mode `photo-button-window` (secondes) | doit être > 0 |
| `AutoPrintDelaySeconds` | `0` | avancé | Délai avant impression automatique en mode `auto` (secondes) | doit être >= 0 |
| `Name` | `""` | avancé | Nom de la file CUPS — créée automatiquement au démarrage, ne pas modifier | aucune contrainte |
| `Copies` | `1` | opérateur | Nombre d'exemplaires imprimés par photo | doit être > 0 |
| `Media` | `"Postcard"` | opérateur | Format papier CUPS (ex : `"Postcard"` pour Canon Selphy CP1300, `"A4"` pour jet d'encre/laser) | aucune contrainte de format |
| `Options` | `"fit-to-page=true"` | avancé | Options CUPS supplémentaires au format `clé=valeur` (séparées par `;`) | obligatoire si `Type=cups` |
| `LpCommand` | `"lp"` | mainteneur | Commande d'impression CUPS | obligatoire si `Type=cups` |
| `OutputPath` | `"printed"` | avancé | Dossier de destination pour `Type=file` | obligatoire si `Type=file` |
| `AllowMultiplePrints` | `false` | avancé | `true` = plusieurs impressions de la même photo pendant la fenêtre ; `false` = une seule impression puis retour au mode photo | aucune contrainte |

### `Admin` — interface de diagnostic web

Désactivée par défaut. Activez-la pour accéder aux logs et à l'état de la borne depuis un navigateur sur le même réseau.

| Champ | Défaut (`appsettings.json`) | Qui édite | Effet | Validation |
|---|---|---|---|---|
| `Enabled` | `false` | opérateur | `true` = démarre le serveur web embarqué sur `http://<ip-borne>:<Port>` | aucune contrainte |
| `ListenAddress` | `"0.0.0.0"` | mainteneur | Interface d'écoute Kestrel (`"0.0.0.0"` = tout le réseau local) | ne doit pas être vide |
| `Port` | `8080` | mainteneur | Port HTTP de l'interface d'admin | doit être compris entre 1 et 65535 |
| `Pin` | `""` | opérateur | Code d'accès optionnel (vide = pas d'authentification) | aucune contrainte |
| `ShowAddressOnStartup` | `true` | mainteneur | Affiche l'URL d'admin à l'écran au démarrage (1er appui bouton photo = fermeture) ; sans effet si `Enabled=false` | aucune contrainte |

### `Logging` — journalisation

Section gérée par le **mainteneur**. Ne contient qu'un sous-objet `File`.

| Champ | Défaut (`appsettings.json`) | Qui édite | Effet | Validation |
|---|---|---|---|---|
| `Logging.File.Path` | `"logs/booth-.log"` | mainteneur | Chemin du fichier de log (le suffixe de date est ajouté automatiquement par Serilog) | aucune contrainte |

---

## Comportement en cas de valeur invalide

Une valeur invalide (pins hors 0–27, doublon de broches, résolution malformée, port hors plage) n'empêche pas
le démarrage : la borne affiche un **bandeau rouge** et démarre en mode dégradé. Les erreurs de validation
sont retournées par la méthode `Validate()` de chaque classe `*Options` et affichées à l'écran au lancement.
