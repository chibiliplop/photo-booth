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

- [ ] **#11 — Aucune sauvegarde locale des photos** · 🟠 `M`  *(socle des suivants)*
  *Pourquoi* : les octets GoPro sont affichés ~5 s puis jetés ; rien n'est écrit sur le Pi.
  *Correctif* : `IPhotoRepository` (Save/List/Delete), écrire chaque JPEG horodaté ; `OutputPath` configurable.
- [ ] **#12 — Aucune galerie / revue des photos** · 🔴 `M` *(dépend de #11)*
  *Correctif* : `GalleryViewModel` + écran galerie (geste/bouton/QR), navigation Prev/Next.
- [ ] **#13 — Aucun partage (QR, email, galerie web, USB)** · 🔴 `L` *(dépend de #11)*
  *Correctif* : QR (ZXing.Net) → galerie web servie par un Kestrel local ; puis export USB ; puis email SMTP optionnel.
- [ ] **#14 — Aucune impression / export fichier** · 🔴 `L` *(dépend de #11)*
  *Correctif* : CUPS + `IPhotoService.PrintAsync` + bouton « Imprimer ».
- [ ] **#19 — Pas d'écran de confirmation/reprise après capture** · 🟠 `S`
  *Correctif* : écran modal « Valider / Recommencer » (15–20 s) + nouvel état.
- [ ] **#18 — Aucune personnalisation du tirage (cadre, logo, filigrane, montage)** · 🟠 `L`
  *Correctif* : `IPrintTemplate` via SkiaSharp (overlay marge/texte/logo, montages 2×2…), modèles en config.
- [ ] **#17 — Métadonnées photo absentes (événement, date, consentement RGPD)** · 🟠 `S` *(dépend de #11)*
  *Correctif* : `PhotoMetadata` sérialisé en JSON à côté de chaque JPEG.

### C. Caméra (couplage GoPro)

- [ ] **#15 — Caméra exclusivement GoPro** · 🔴 `L`
  *Pourquoi* : `IGoProClient` est l'unique abstraction ; sans GoPro la borne est inutilisable.
  *Correctif* : abstraction `ICaptureSource` + adaptateurs USB/RTSP/MJPEG ; `Camera.Type` en config ; fournir au moins un adaptateur USB générique.
- [ ] **#16 — Paramètres GoPro non éditables (IP, base URLs, Wi-Fi)** · 🟠 `M`
  *Correctif* : exposer `Gopro.ControlBaseUrl`/`MediaBaseUrl`/`KeepAliveHost` dans `photobooth.json` ; éventuellement découverte mDNS.
- [ ] **#27 — Mode dégradé GoPro mal documenté** · 🟠 `S`
  *Correctif* : clarifier le `GUIDE` (« bandeau orange = photos en pause, reprise auto au retour »).

### D. Affichage, langue, accessibilité

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

*Prochain lot suggéré : **#11 sauvegarde locale** (débloque galerie/partage/impression), ou **#7 + #8** (continuité du câblage DIY).*
