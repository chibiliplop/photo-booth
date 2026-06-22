# BACKLOG — Borne photo

> Issu de l'audit « ce qui manque » du **2026-06-21** (usage cible : l'utilisateur **fabrique lui-même** sa boîte — bouton(s), lumière **optionnelle**, écran HDMI ; on ne fournit que le programme + l'image SD).
> 79 manques bruts → 30 distincts après dédup et vérification adversariale.

## Légende

- **Sévérité** : 🔴 bloquant · 🟠 majeur · 🟡 mineur · ⚪ confort
- **Effort** : `S` petit · `M` moyen · `L` gros
- `#n` = rang de priorité global de l'audit (1 = le plus important).

---

## ✅ Livré (2026-06-21)

- [x] **#1 — Pins GPIO éditables par l'opérateur** · 🟠 `S`
  Section `Hardware` (`PhotoButtonPin`/`VideoButtonPin`/`LightPin`) ajoutée au fichier éditable `deploy/boot-config/photobooth.json`. Aucun code modifié : `Program.cs` empilait déjà ce fichier sur toutes les sections.
- [x] **#2 — Lumière désactivable** · 🔴 `M`
  `HardwareOptions.LightEnabled` (défaut `true`) + classe `NoOpLightOutput` : si `false`, la broche lumière n'est jamais ouverte et `_light.On()/Off()` deviennent des no-ops. Câblé dans `HardwareBundle.Create`. Exposé dans `photobooth.json`.
- [x] **#6 — Validation des broches** · 🟠 `S`
  `HardwareOptions.Validate()` rejette les broches hors 0–27 et les doublons (uniquement parmi les pins actifs ; `LightPin` ignoré si lumière off), avec messages FR explicites.
- [x] **#5 — Erreurs critiques affichées à l'écran** · 🔴 `M`
  Bandeau rouge persistant (`MainViewModel.Diagnostic` + overlay `MainView.axaml`, câblé dans `App.axaml.cs`) pour config invalide / GPIO inaccessible. `buttons.Start()` protégé par try/catch. Distinct du statut GoPro → jamais écrasé.
- [x] **#9 — Incohérence doc « bouton lumière »** · 🟠 `S`
  Le « bouton lumière » inexistant retiré de `GUIDE_OPERATEUR.md` (3 endroits) ; docs techniques (`DEPLOY`, `RUNBOOK`) alignées sur la lumière optionnelle.
- [x] **#20 — UI responsive (résolution & tailles d'écran codées en dur)** · 🔴 `L`
  Toute l'UI est composée sur un canevas de référence (`ThemeOptions.ScreenResolution`, défaut `1280x720`) mis à l'échelle uniformément par un `Viewbox` : rendu bord à bord sur tout écran 16:9 (720p/1080p/4K) ; les autres formats sont centrés (fond plein écran via `UniformToFill`, UI ni rognée ni déformée). `MainWindow` n'a plus de `1280×720` en dur (binding `DesignWidth`/`DesignHeight` + repli design). `ScreenResolution` exposé dans `photobooth.json`/`appsettings.json` et validé (repli `1280×720` + message FR si malformé, jamais de crash). Les tailles cartes/polices restent en pixels de design — le `Viewbox` les met à l'échelle.
  *Vérifs* : build 0 warning · 22/22 tests (dont 14 nouveaux : parsing/validation de `ScreenResolution`) · rendu screenshot confirmé en 720p, 1080p (composition identique, scalée) et 1280×1024 (5:4 centré, sans déformation).
- [x] **#14 — Impression** · 🔴 `L`
  `IPrinterAdapter` + adapters `CupsPrinterAdapter`, `FilePrinterAdapter`, `NoOpPrinterAdapter`. `Printer.Type` configurable (`disabled`/`cups`/`file`). Déclenchement configurable : bouton impression séparé (`manual` + `Hardware.PrintButtonEnabled`), impression automatique (`auto`), ou réutilisation du bouton PHOTO pendant une fenêtre configurable (`photo-button-window`). En mode `photo-button-window` : la photo reste affichée pendant toute la fenêtre (slideshow suspendu), pill centrée « Appuyez pour imprimer » + compte à rebours animé, `AllowMultiplePrints` pour autoriser plusieurs tirages. Détection USB automatique au boot via `photobooth-printer.service` (CUPS configuré sans SSH). Paquets CUPS/Gutenprint inclus dans l'image.

**Vérifs** : build 0 warning/erreur · 8/8 tests · binding config 5 cas · rendu runtime du bandeau confirmé · relecture adversariale « OK à livrer ».

---

## ❌ Écarté — faux positif vérifié

- **#3 — « Commentaires `//` dans `photobooth.json` → écran noir au boot »**
  **Faux.** Testé empiriquement : `Microsoft.Extensions.Configuration.Json` 8.0.1 ignore les commentaires `//` et tolère les virgules finales. Le fichier livré parse correctement. **Ne pas re-signaler.**
  *Risque résiduel réel mais distinct* : un JSON *réellement* malformé par l'opérateur (guillemet/accolade manquant) plante encore en silence car `config.Build()` est hors du try/catch et avant l'init Serilog → voir **#31** ci-dessous (ajouté).

---

## 🔲 À faire — par thème (priorité décroissante)

### A. Câblage & fabrication DIY

- [ ] **#4 — Sécurité électrique 220 V de la sortie lumière non documentée** · 🔴 `M`
  *Pourquoi* : `DEPLOY` indique « lumière → GPIO 17 » sans avertir qu'un GPIO 3,3 V ne pilote pas du secteur. Câbler du 220 V directement = Pi détruit / risque d'électrocution.
  *Correctif* : `SCHEMA.md` avec circuit relais 5 V (optocoupleur/SSR), composants, avertissement DANGER ; commentaire dans `GpioLightOutput.cs`.
- [ ] **#7 — Mode mono-bouton impossible** · 🔴 `M`
  *Pourquoi* : `GpioButtonInput` ouvre toujours les 2 boutons ; un bricoleur ne voulant qu'un bouton photo est bloqué.
  *Correctif* : `PhotoButtonEnabled`/`VideoButtonEnabled` ; n'ouvrir un pin que si activé. S'appuie sur la validation #6.
- [ ] **#8 — Pull-up/down, active-high/low, front montant/descendant câblés en dur** · 🟠 `M`
  *Pourquoi* : `InputPullUp` + `Falling` forcés ; lumière `High=on` forcé. Un câblage active-low/pull-down externe oblige à modifier le C#.
  *Correctif* : `ButtonPullMode`, `ButtonEdge`, `LightActiveHigh` en config, mappés dans les adapters Linux.
- [ ] **#10 — Aucun écran de diagnostic / test GPIO ni calibration** · 🟠 `M`
  *Pourquoi* : impossible de valider son câblage avant l'événement.
  *Correctif* : écran de calibration (« appuyez sur PHOTO → GPIO 18 détecté ») déclenché par appui long / `--calibrate`, + bandeau d'état GPIO permanent.
- [ ] **#30 — Pas de schéma de câblage ni de BOM** · 🟠 `L`
  *Pourquoi* : la doc suppose une borne pré-montée ; le bricoleur ne sait ni quoi acheter ni comment câbler.
  *Correctif* : `SCHEMA.md`/`BRANCHEMENTS.md` (diagramme GPIO↔boutons/lumière/alim, tolérances), `BOM.md` (composants, réf., prix, optionnels), `FABRICATION_DIY.md`. Inclut le relais lumière (#4).

### B. Devenir des photos (le plus gros trou produit)

> **Principe architectural** : les photos restent sur la carte GoPro. Le Pi récupère les octets JPEG en mémoire (déjà fait pour l'affichage), les utilise pour afficher/imprimer/partager, puis les jette. Pas de copie locale obligatoire — avantage RGPD (les photos ne transitent pas sur le Pi) et pas de risque de saturation SD.

- [ ] **#19 — Pas d'écran de confirmation/reprise après capture** · 🟠 `S`
  *Pourquoi* : les octets JPEG sont déjà en mémoire au moment de l'affichage.
  *Correctif* : écran modal « Valider / Recommencer » (15–20 s) + nouvel état machine.
- [ ] **#18 — Aucune personnalisation du tirage (cadre, logo, filigrane, montage)** · 🟠 `L`
  *Correctif* : `IPrintTemplate` via SkiaSharp (overlay marge/texte/logo, montages 2×2…) appliqué en mémoire sur le `byte[]` avant impression. Modèles en config.
- [x] **#14 — Aucune impression** · 🔴 `L`
  *Pourquoi* : aucun chemin d'impression depuis la capture GoPro.
  *Correctif* : abstraction `IPrinterAdapter` + bouton « Imprimer » ; le `byte[]` (éventuellement transformé par #18) est passé directement à l'adapter sans écriture disque.
  *Adapters prévus* :
  - `CupsPrinterAdapter` — imprimantes standard Linux (inkjet, laser, dye-sub via gutenprint)
  - `DyeSubPrinterAdapter` — pilotes natifs DNP DS-RX1HS / Mitsubishi CP-D70DW / HiTi S420
  - `FilePrinterAdapter` — export vers dossier (USB, test)
  - `NoOpPrinterAdapter` — impression désactivée
  `Printer.Type` configurable dans `photobooth.json`.
- [ ] **#13 — Aucun partage (QR, email, galerie web, USB)** · 🔴 `L`
  *Correctif* : QR (ZXing.Net) → galerie web servie par un Kestrel local (photo servie depuis la RAM le temps de la session) ; export USB (`FilePrinterAdapter` réutilisable) ; email SMTP optionnel.
- [ ] **#12 — Aucune galerie / revue des photos** · 🟠 `M`
  *Pourquoi* : sans stockage local, la galerie peut parcourir les fichiers via l'API HTTP GoPro (liste des médias disponibles sur la carte).
  *Correctif* : `GalleryViewModel` + écran galerie (geste/bouton/QR), navigation Prev/Next via `ICaptureSource.ListMediaAsync`.
- [ ] **#11 — Pas de sauvegarde locale des photos** · ⚪ `M`  *(optionnel — non bloquant)*
  *Pourquoi* : utile si l'opérateur veut archiver sans récupérer la carte GoPro, ou servir une galerie persistante.
  *Correctif* : `IPhotoRepository` (Save/List/Delete) activable via `Storage.Enabled`, écriture chaque JPEG horodaté dans `OutputPath`. Désactivé par défaut.
- [ ] **#17 — Métadonnées photo absentes (événement, date, consentement RGPD)** · 🟠 `S` *(dépend de #11 si persistance voulue)*
  *Correctif* : `PhotoMetadata` sérialisé en JSON à côté de chaque JPEG quand #11 est activé ; sinon émis en log uniquement.

### C. Caméra (couplage GoPro)

- [ ] **#15 — Caméra exclusivement GoPro** · 🔴 `L`
  *Pourquoi* : `IGoProClient` est l'unique abstraction ; sans GoPro la borne est inutilisable.
  *Correctif* : abstraction `ICaptureSource` + adaptateurs USB/RTSP/MJPEG ; `Camera.Type` en config ; fournir au moins un adaptateur USB générique.
- [ ] **#16 — Paramètres GoPro non éditables (IP, base URLs, Wi-Fi)** · 🟠 `M`
  *Correctif* : exposer `Gopro.ControlBaseUrl`/`MediaBaseUrl`/`KeepAliveHost` dans `photobooth.json` ; éventuellement découverte mDNS.
- [ ] **#27 — Mode dégradé GoPro mal documenté** · 🟠 `S`
  *Correctif* : clarifier le `GUIDE` (« bandeau orange = photos en pause, reprise auto au retour »).

### D. Améliorations UX / animations

- [x] **#UI-1 — Flash visuel au déclenchement** · 🟠 `S`
  `FlashOverlay` (Rectangle blanc ZIndex=240) : Opacity 1→0 en 300 ms (DispatcherTimer 16 ms), déclenché dans `AnimateCardIn` quand `IsImageVisible`. Code-behind uniquement.

- [x] **#UI-2 — Révélation Polaroid de la photo** · 🟡 `M`
  `RevealOverlay0/1/2` (Rectangle blanc dans chaque photo Panel, `ClipToBounds="True"`) : `TranslateTransform.Y` 0→500 en 600 ms EaseOutCubic. Démarre à t=0.7 du slide-in (carte atterrie), hors GPU.

- [x] **#UI-3 — Écran idle vide (cartes sans contenu)** · 🟠 `S`
  `IsIdle` (VM, défaut `true`) : overlay noir semi-transparent ZIndex=150 avec `Names` + « Appuyez sur le bouton ! ». Disparaît au premier `ShowMessage` ou `ShowPhoto`.

- [x] **#UI-4 — Compte à rebours photo peu visible** · 🟡 `S`
  `IsPhotoCountdown` + `PhotoCountdownText` (VM) : overlay plein écran ZIndex=210, chiffre FontSize=320. Déclenché par `ShowMessage("3"/"2"/"1")`. Pulse `ScaleTransform` 1.3→1.0 (EaseOutCubic 400 ms) à chaque battement via code-behind.

- [x] **#UI-5 — Profondeur dynamique sur la carte active** · ⚪ `S`
  `CardViewModel.CardShadow` : `BoxShadows.Parse(...)` selon `ZIndex` (100 → ombre `-15 15 35 0 #88000000`, sinon `-10 10 20 0 #66000000`). Binding dans les 3 `Border.BoxShadow`.

### E. Affichage, langue, accessibilité

- [ ] **#21 — Orientation non configurable (paysage verrouillé)** · 🔴 `M`
  *Correctif* : `ThemeOptions.IsPortrait` (swap dimensions + réarrangement des cartes).
- [ ] **#22 — Textes français en dur, aucune i18n** · 🟠 `M`
  *Correctif* : `ThemeOptions.Language` + messages externalisés (`Assets/Languages/{fr,en}.json`) ; guide opérateur EN.

### E. Fiabilité & exploitation terrain

- [ ] **#24 — Pas de watchdog matériel** · 🟠 `M`
  *Correctif* : `dtparam=watchdog=on`, pulser `/dev/watchdog`, `WatchdogSec` systemd.
- [ ] **#25 — Pas d'arrêt propre accessible à l'opérateur** · 🟠 `M`
  *Pourquoi* : `BoothCommand.Shutdown` existe mais n'est relié à aucun geste/bouton.
  *Correctif* : appui long (3 s) ou softkey UI avec confirmation → SIGTERM.
- [ ] **#26 — GoPro non power-cyclable ; déconnexion prolongée sans recovery actif** · 🟠 `M`
  *Correctif* : power-cycle/wake HTTP après X min (`Gopro.AutoRestartAfterMinutes`) + « Tentative de reconnexion ».
- [ ] **#28 — Risque saturation/corruption carte SD** · 🟠 `M`
  *Correctif* : health-check disque (alerte si < 100 Mo), overlay ON en prod (logs en RAM), recommander onduleur, sync avant écritures critiques.
- [ ] **#29 — Pas de mise à jour logicielle terrain** · 🟠 `L`
  *Correctif* : binaire hors overlay (`/opt/photobooth`) + script de MAJ atomique (fetch+checksum+swap) ; option OTA.

### F. Configuration opérateur

- [ ] **#31 — `config.Build()` hors try/catch → JSON malformé = plantage silencieux** · 🟠 `S`  *(ajouté suite à l'analyse de #3)*
  *Correctif* : envelopper `config.Build()` dans un try/catch qui affiche un message lisible à l'écran avant exit.
- [ ] **#23 — Hot-reload désactivé (tout changement de config = redémarrage)** · 🟡 `S`
  *Correctif* : `reloadOnChange:true` pour `photobooth.json` uniquement, ou geste « Recharger config ».

---

## 🔲 Compléments d'audit UX/UI + fonctionnel (2026-06-22)

> Seconde passe après relecture du programme Avalonia (`src/Photobooth.App`), du workflow (`PhotoboothWorkflow`) et des fichiers de configuration. Ces points complètent le backlog initial : ils ciblent surtout l'exploitation réelle par un opérateur non-tech, la borne DIY et la perception invité.

### G. Parcours opérateur / jour J

- [ ] **#32 — Pas d'écran « prêt événement » avant ouverture aux invités** · 🟠 `M`
  *Pourquoi* : l'opérateur voit seulement des bandeaux GoPro/GPIO. Il n'a pas de checklist claire confirmant que l'écran, la GoPro, les boutons, la lumière, la config et l'espace disque sont OK avant de laisser les invités utiliser la borne.
  *Correctif* : écran de pré-vol au boot ou via appui long : GoPro OK, mode réel/fake, boutons détectés, lumière activée/désactivée, fond chargé, résolution, espace disque, date/heure. Bouton/timeout « Démarrer la soirée ».

- [ ] **#33 — Pas de mode test matériel autonome** · 🟠 `M`
  *Pourquoi* : le mode fake teste l'UI, mais pas explicitement le câblage. Un bricoleur ne sait pas si PHOTO, VIDEO et lumière sont correctement branchés avant l'événement.
  *Correctif* : option `--test-hardware` ou geste au démarrage : « appuyez sur PHOTO », « appuyez sur VIDEO », « test lumière 2 s », résultat visuel vert/rouge.

- [ ] **#34 — Aucun résumé opérateur lisible depuis la partition boot** · 🟠 `S`
  *Pourquoi* : en cas de problème sur site, les logs Serilog sont dans le dossier applicatif, pas dans un fichier simple que l'opérateur peut lire sous Windows/Mac en remettant la SD dans un ordinateur.
  *Correctif* : écrire `/boot/firmware/photobooth/etat-borne.txt` à chaque boot : version, config chargée, mode GoPro, dernier diagnostic, date/heure, dernière erreur courte, compteurs de session.

- [ ] **#35 — Pas de sauvegarde/restauration de la dernière bonne config** · 🟠 `S`
  *Pourquoi* : #31 affichera une erreur si `photobooth.json` est cassé, mais l'opérateur non-tech n'a pas forcément de chemin simple pour revenir à une config fonctionnelle.
  *Correctif* : à chaque config valide, copier `photobooth.json` vers `photobooth.last-good.json`. En cas de config invalide, afficher l'erreur et proposer/indiquer de restaurer ce fichier.

- [ ] **#36 — Pas de mode « fin de soirée »** · ⚪ `S`
  *Pourquoi* : l'opérateur débranche directement. C'est simple, mais il n'a aucun récapitulatif ni confirmation que la session est terminée.
  *Correctif* : geste opérateur (appui long / raccourci clavier) affichant « Événement terminé », nombre de photos/vidéos, dernier statut GoPro, rappel « éteindre la GoPro puis débrancher ».

### H. Session, statistiques et maintenance terrain

- [ ] **#37 — Pas de notion de session événement** · 🟡 `M`
  *Pourquoi* : `Theme.Names`/`Year` personnalisent l'écran, mais il n'existe pas d'identifiant de session utilisable pour logs, exports, galerie, impression ou support.
  *Correctif* : ajouter `Event.Name`, `Event.Date`, `Event.SessionId` (auto si absent), heure de début/fin, compteurs `PhotosTaken`/`VideosTaken`.

- [ ] **#38 — Aucun compteur discret pour l'opérateur** · ⚪ `S`
  *Pourquoi* : pendant l'événement, l'opérateur ne sait pas combien de photos/vidéos ont été prises, ni depuis quand la GoPro est connectée.
  *Correctif* : panneau caché par appui long / touche maintenance : compteurs, uptime, dernière erreur, dernier fichier GoPro, état du mode fake/http, espace disque.

- [ ] **#39 — Pas de health-check espace disque / logs au niveau UI** · 🟠 `S`
  *Pourquoi* : #28 mentionne la saturation SD, mais l'UI ne prévient pas l'opérateur si les logs ou une future sauvegarde locale remplissent la carte.
  *Correctif* : vérifier l'espace libre au boot et périodiquement ; bandeau rouge si sous seuil configurable (`Storage.MinFreeMb`, défaut 250 Mo).

- [ ] **#40 — Pas d'indication de version build à l'écran** · ⚪ `S`
  *Pourquoi* : en support à distance, impossible de savoir quelle image/app tourne sans SSH.
  *Correctif* : afficher version courte dans le panneau maintenance et l'écrire dans `etat-borne.txt`.

### I. Feedback invité / UX de prise de vue

- [ ] **#UX-6 — Appuis ignorés sans feedback pendant une séquence** · 🟡 `S`
  *Pourquoi* : le workflow draine correctement les doubles appuis, mais l'invité qui rappuie pendant une capture/vidéo ne voit rien. Il peut croire que le bouton ne marche pas.
  *Correctif* : quand un appui est refusé car `Capturing`/`Recording`, afficher brièvement « Patientez... » ou faire pulser le contour/flash UI sans lancer de nouvelle action.

- [ ] **#UX-7 — Pas de retour audio optionnel** · 🟡 `M`
  *Pourquoi* : en événement, les invités ne regardent pas toujours l'écran. Un bip de décompte ou un son shutter rend la borne plus compréhensible.
  *Correctif* : `Sound.Enabled`, `CountdownBeep`, `ShutterSound`, `ErrorSound`; off par défaut ou volume configurable. Jouer les sons uniquement côté App, pas dans Core.

- [ ] **#UX-8 — Pas de message clair « ne bougez plus / traitement en cours » après le shutter** · 🟡 `S`
  *Pourquoi* : après `Souriez`, la GoPro peut mettre plusieurs secondes à écrire le fichier. Le message « La photo arrive... » existe, mais il ne distingue pas le moment où il faut encore rester immobile du moment où la photo est en traitement.
  *Correctif* : états visuels séparés : « Ne bougez plus » pendant lumière/shutter, puis « Préparation de la photo » pendant attente média/téléchargement.

- [ ] **#UX-9 — Aucune instruction persistante en mode idle après plusieurs photos** · 🟡 `S`
  *Pourquoi* : #UI-3 couvre l'écran vide au démarrage, mais une fois le slideshow lancé, rien n'indique clairement aux nouveaux invités quel bouton utiliser.
  *Correctif* : call-to-action discret mais permanent en bas d'écran (configurable) : « Appuyez sur le bouton PHOTO » / « Appuyez sur VIDEO pour un message », masqué pendant les séquences.

- [ ] **#UX-10 — Pas de mode accessibilité / haute lisibilité** · 🟡 `M`
  *Pourquoi* : l'UI actuelle est stylée, mais un écran petit, bas ou éloigné rend les textes difficiles à lire.
  *Correctif* : `Theme.HighVisibility=true` : compte à rebours plein écran, contraste renforcé, statuts plus gros, call-to-action plus lisible, animations réduites.

### J. Fonctionnel photo / impression / partage

- [ ] **#41 — Bouton impression partiellement présent mais non fonctionnel** · 🟠 `S`
  *Pourquoi* : `HardwareOptions` contient `PrintButtonPin`/`PrintButtonEnabled` et `GpioButtonInput` expose `PrintPressed`, mais `IButtonInput` ne l'expose pas et `App.axaml.cs` ne route aucune commande impression. Risque de confusion : la config suggère une fonction qui n'existe pas.
  *Correctif* : soit retirer les options jusqu'à #14, soit ajouter proprement `PrintPressed` à `IButtonInput`, `BoothCommand.PrintRequested`, routage App, fake clavier (`P`) et no-op explicite tant que l'adapter imprimante n'existe pas.

- [ ] **#42 — Pas de cache mémoire pour fluidifier le slideshow GoPro** · ⚪ `M`
  *Pourquoi* : le slideshow liste/télécharge depuis le Wi-Fi GoPro à chaque tick. Sur un signal faible, l'écran peut sembler inerte ou irrégulier.
  *Correctif* : précharger 2–3 JPEG en RAM pendant `Idle`, remplacer progressivement le cache, ne jamais écrire disque par défaut.

- [ ] **#43 — Pas de limite ou filtre de galerie/slideshow par session** · 🟡 `M`
  *Pourquoi* : la GoPro peut contenir d'anciennes photos. Le slideshow choisit aujourd'hui parmi les fichiers non vidéo disponibles, ce qui peut afficher des images d'un événement précédent.
  *Correctif* : option `Slideshow.Source=all|session|recent`, filtrage par date GoPro si disponible ou par snapshot au démarrage.

- [ ] **#44 — Pas de protection produit contre le mode fake oublié le jour J** · 🔴 `S`
  *Pourquoi* : le guide demande de repasser `"Mode": "http"`, mais si l'opérateur oublie, la borne paraît fonctionner tout en ne prenant aucune vraie photo.
  *Correctif* : en `Gopro.Mode=fake`, afficher un bandeau persistant « MODE TEST - aucune vraie photo » et/ou exiger une confirmation au boot si `Event.Production=true`.

### K. Thème et personnalisation

- [ ] **#45 — Personnalisation thème trop limitée pour des événements variés** · 🟡 `M`
  *Pourquoi* : `Theme` couvre noms, année, fond, couleurs et police, mais pas les textes d'appel à action, logo, position du logo, style de carte, ni variantes mariage/anniversaire/entreprise.
  *Correctif* : ajouter `Theme.CallToAction`, `Theme.LogoImage`, `Theme.LogoPlacement`, `Theme.Preset`, `Theme.ShowYear`, avec valeurs par défaut compatibles.

- [ ] **#46 — Image de fond invalide silencieuse pour l'opérateur** · 🟡 `S`
  *Pourquoi* : si `BackgroundImage` pointe vers un fichier absent, l'app logge et retombe sur le fond par défaut. C'est robuste, mais l'opérateur ne comprend pas pourquoi son fond ne s'affiche pas.
  *Correctif* : diagnostic non bloquant à l'écran et dans `etat-borne.txt` : « fond.jpg introuvable, fond par défaut utilisé ».

- [ ] **#47 — Pas de prévisualisation de configuration hors borne** · ⚪ `M`
  *Pourquoi* : l'opérateur modifie la SD à la maison mais ne peut pas vérifier facilement le rendu avant de redémarrer la borne.
  *Correctif* : outil `--preview-config <dir>` ou script qui rend un PNG de l'écran idle à partir du dossier boot-config.

### L. Sécurité d'exploitation / anti-erreur

- [ ] **#48 — Pas de verrouillage des raccourcis clavier en production** · ⚪ `S`
  *Pourquoi* : en mode desktop/dev, Espace/Entrée/V déclenchent les actions. Sur une borne avec clavier branché ou accessible, un invité peut lancer des actions non prévues.
  *Correctif* : `Operator.KeyboardShortcutsEnabled`, désactivable en production sauf combinaison maintenance.

- [ ] **#49 — Pas de confirmation avant actions opérateur sensibles** · 🟡 `S`
  *Pourquoi* : les futurs gestes `shutdown`, `reload config`, `test hardware`, `end session` doivent éviter les déclenchements accidentels pendant une soirée.
  *Correctif* : confirmation plein écran 3 s / appui long maintenu, jamais sur appui court.

- [ ] **#50 — Pas de stratégie visible si l'heure système est fausse** · 🟡 `S`
  *Pourquoi* : la borne peut être hors ligne sur le Wi-Fi GoPro. Une mauvaise date casse les logs, futures sessions, filtres slideshow et exports.
  *Correctif* : afficher un avertissement si l'heure semble aberrante (avant 2024, par exemple), permettre `Event.Date` comme repli pour nommage/session.

## Dette technique connexe (hors audit)

- [ ] Sur échec d'init GPIO, le `GpioController` partiellement ouvert n'est pas disposé (fuite mineure, pré-existante) — `HardwareBundle.Create`.

---

*Prochain lot suggéré : **#19 + #14** (confirmation + impression, indépendants, haute valeur événementielle), ou **#7 + #8** (continuité du câblage DIY).*
