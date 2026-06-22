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
- [ ] **#14 — Aucune impression** · 🔴 `L`
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

- [ ] **#UI-1 — Pas de flash visuel au déclenchement** · 🟠 `S`
  *Pourquoi* : sans retour visuel au moment du shutter, le déclenchement semble silencieux et les invités ne savent pas si la photo a été prise.
  *Correctif* : rectangle blanc fullscreen (ZIndex élevé) qui monte à opacité 1 puis redescend en ~300 ms via une animation Avalonia, déclenché depuis `ShowPhoto` ou un nouvel événement `IPhotoDisplay.Flash()`. 10 lignes de XAML + 2 lignes de ViewModel.

- [ ] **#UI-2 — Révélation Polaroid de la photo** · 🟡 `M`
  *Pourquoi* : la photo apparaît instantanément après le slide-in ; un effet de "développement" renforcerait l'analogie Polaroid de l'interface cartes.
  *Correctif* : animer un `ClipRect` ou un wipe vertical sur l'`Image` (haut → bas, 500–700 ms, EaseOutCubic) une fois la carte arrivée en position.

- [ ] **#UI-3 — Écran idle vide (cartes sans contenu)** · 🟠 `S`
  *Pourquoi* : au démarrage et entre les séquences, les 3 cartes sont vides ; l'écran semble cassé pour les invités qui arrivent.
  *Correctif* : overlay centré (ZIndex modéré) visible tant qu'aucune photo n'a été prise, affichant le nom de l'événement + « Appuyez sur le bouton ! ». Disparaît au premier `ShowPhoto`.

- [ ] **#UI-4 — Compte à rebours photo peu visible** · 🟡 `S`
  *Pourquoi* : le "3 / 2 / 1" s'affiche sur une carte de 600 px inclinée ; illisible à distance pour les invités debout devant la borne.
  *Correctif* : overlay plein écran (réutiliser la structure du clapperboard vidéo) avec le chiffre centré + animation de pulse (scale 1.3 → 1.0 à chaque battement).

- [ ] **#UI-5 — Pas de profondeur dynamique sur la carte active** · ⚪ `S`
  *Pourquoi* : toutes les cartes ont la même ombre quelle que soit leur position dans la pile ; la carte du dessus ne "ressort" pas visuellement.
  *Correctif* : quand `ZIndex = 100`, passer à une `BoxShadow` plus grande/sombre (ex. `-15 15 35 0 #88000000`) via binding sur `CardViewModel.ZIndex`. Pur XAML, zéro code.

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

## Dette technique connexe (hors audit)

- [ ] Sur échec d'init GPIO, le `GpioController` partiellement ouvert n'est pas disposé (fuite mineure, pré-existante) — `HardwareBundle.Create`.

---

*Prochain lot suggéré : **#19 + #14** (confirmation + impression, indépendants, haute valeur événementielle), ou **#7 + #8** (continuité du câblage DIY).*
