# Migration Linux - blocages et plan de portage

## Objectif

Migrer l'application photobooth actuellement compatible Windows 10 IoT Core vers Linux, avec une cible naturelle de type Raspberry Pi OS.

Le systeme actuel combine:

- une application UWP plein ecran pour l'interface photobooth;
- des boutons physiques et une sortie lumiere via GPIO;
- un capteur de luminosite MAX44009 via I2C;
- une GoPro pilotee en Wi-Fi via HTTP/UDP;
- un affichage de type borne/photo booth avec compte a rebours, prise de photo, recuperation de la derniere image, et diaporama.

## Synthese

La migration n'est pas bloquee par la GoPro: la majorite du pilotage GoPro repose sur HTTP, UDP et JSON, donc c'est portable.

Les vrais blocages sont:

1. le framework UWP et le packaging AppX;
2. l'interface XAML/UWP;
3. les APIs Windows pour GPIO et UI thread;
4. les types image/stream UWP;
5. le modele d'execution Windows IoT Core.

Une migration saine consiste a extraire la logique metier photobooth, porter la couche GoPro en .NET moderne, remplacer la couche hardware par des abstractions Linux, puis reconstruire l'UI avec une technologie Linux-compatible.

## Blocages identifies

| Zone | Fichiers | Pourquoi ca bloque Linux | Remplacement probable |
| --- | --- | --- | --- |
| Projet UWP | `CS/PushButton.csproj`, `GoProWifi/GoProWifi.csproj`, `RasberryPiLib/RasberryPiLib.csproj` | Les projets ciblent `UAP`, importent les targets Windows XAML, et utilisent `Microsoft.NETCore.UniversalWindowsPlatform`. Ces projets ne se compilent pas tels quels sur Linux. | Nouveaux projets `net8.0` ou `net9.0`, potentiellement `net8.0` console/service + UI separee. |
| App UWP | `CS/App.xaml`, `CS/App.xaml.cs`, `CS/Package.appxmanifest` | Cycle de vie UWP, manifest AppX, capacites `internetClient`/`lowLevel`, activation Windows, packaging Windows Store/AppX. | Application Linux native, service systemd, app web locale, Avalonia, GTK, SDL, kiosk Chromium, ou autre UI multiplateforme. |
| UI XAML UWP | `CS/MainPage.xaml`, `CS/DropShadowPanelPage.xaml` | XAML UWP et `Microsoft.Toolkit.Uwp.UI.Controls.DropShadowPanel` n'existent pas sur Linux. | Refaire l'UI: Avalonia XAML, Blazor/Electron/kiosk web, GTK, Qt, ou simple front web fullscreen. |
| Controle UI thread | `CS/MainPage.xaml.cs` | `DispatcherTimer`, `Dispatcher.RunAsync`, `CoreDispatcherPriority`, `Window.Current.CoreWindow.KeyDown` sont UWP. | `System.Threading.Timer`, `PeriodicTimer`, event loop du framework UI choisi, ou callbacks async propres. |
| GPIO Windows | `CS/MainPage.xaml.cs`, `RasberryPiLib/Button.cs`, `RasberryPiLib/SwitchPin.cs`, `RasberryPiLib/ShiftRegister.cs`, `RasberryPiLib/SevenSergmentLed.cs` | `Windows.Devices.Gpio.GpioController`, `GpioPin`, `GpioPinValue`, `GpioPinEdge` sont Windows IoT. | `System.Device.Gpio` sur Linux, `libgpiod`, ou une lib Raspberry Pi dediee. |
| Gestion debounce bouton | `RasberryPiLib/Button.cs` | `GpioPin.DebounceTimeout` est fourni par l'API Windows. | Debounce logiciel maison avec timestamps, timer, channel/queue d'evenements, ou support natif de la lib GPIO choisie. |
| I2C creation | `CS/MainPage.xaml.cs` | `I2cDevice.Create(settings)` doit etre valide avec le binding Linux et l'acces `/dev/i2c-*`. | `System.Device.I2c` avec permissions Linux, activation I2C via `raspi-config`, groupe `i2c`, ou acces root/service. |
| Type image UWP | `GoProWifi/GoproWifi.cs`, `CS/MainPage.xaml.cs` | `BitmapImage`, `IRandomAccessStream`, `InMemoryRandomAccessStream`, `DataWriter` sont UWP/WinRT. | La couche GoPro doit retourner des `byte[]`, `Stream`, chemin fichier, ou type image du framework UI cible. |
| Assets UWP | `CS/Assets/*`, `Package.appxmanifest` | Les assets sont references par chemins UWP/AppX et tailles de logos Windows. | Garder les images utiles, refaire le chargement d'assets selon le framework cible. |
| Build/deploiement | `CS/PushButton.sln` | Solution Visual Studio UWP, configs ARM/x86/x64 Windows, deploiement Windows IoT. | Build Linux ARM64/ARM avec `dotnet publish`, Docker optionnel, deploiement `systemd`, script d'installation. |
| VLC/test video | `testvlcsharp/` | Projet experimental UWP/FFmpegInterop. Pas necessaire au flux photo actuel. | Ignorer au debut, reevaluer seulement si le mode video live devient un objectif. |

## Parties reutilisables

### Pilotage GoPro

`GoProWifi/GoproWifi.cs` contient une logique largement portable:

- commandes HTTP vers `http://10.5.5.9/gp/gpControl/...`;
- listing media via `http://10.5.5.9:8080/gp/gpMediaList`;
- telechargement des medias via `http://10.5.5.9:8080/videos/DCIM/...`;
- keepalive UDP vers `10.5.5.9:8554` depuis `CS/MainPage.xaml.cs`;
- modeles JSON dans `GoProWifi/GoproMedia.cs`.

Ce qui doit changer:

- enlever les references UWP (`BitmapImage`, `IRandomAccessStream`, `Windows.Storage.Streams`);
- retourner des donnees neutres (`byte[]`, `Stream`, `MemoryStream`, ou chemin fichier);
- reutiliser `HttpClient`, `UdpClient`, `System.Text.Json` ou `Newtonsoft.Json`.

### Logique photobooth

La sequence metier est portable:

1. attendre un appui bouton;
2. bloquer les actions concurrentes;
3. afficher "Prenez la pose";
4. compte a rebours;
5. allumer la lumiere;
6. declencher la GoPro;
7. eteindre la lumiere;
8. attendre que la GoPro publie le fichier;
9. recuperer et afficher l'image;
10. reprendre le diaporama.

Cette logique devrait etre extraite de `MainPage.xaml.cs` vers un service sans dependance UI ni GPIO.

### Capteur MAX44009

`RasberryPiLib/Max44009.cs` utilise deja `System.Device.I2c`, ce qui est plus proche d'une cible Linux que le reste de `RasberryPiLib`.

Points a verifier sur Raspberry Pi OS:

- I2C active dans le systeme;
- bus correct (`/dev/i2c-1`);
- adresse `0x4A`;
- permissions du compte qui lance l'app.

## Decisions d'architecture a prendre

### Choix de l'UI Linux

Options raisonnables:

- **App web locale en kiosk Chromium**: robuste pour photobooth, facile a styliser, plein ecran, bonne separation backend/frontend.
- **Avalonia UI**: proche du monde XAML/C#, multiplateforme, peut conserver une partie de la mentalite UWP.
- **GTK/Qt**: natif Linux, plus bas niveau cote UI.
- **Console/service + page web**: backend .NET qui gere GoPro/GPIO, frontend web local qui affiche l'experience.

Recommandation pragmatique: backend .NET sur Linux + UI web locale plein ecran. Cela isole mieux les contraintes GPIO/GoPro et rend l'interface plus simple a iterer.

### Choix de la couche hardware

Options:

- `System.Device.Gpio` / `System.Device.I2c` si le support Raspberry Pi cible est suffisant;
- `libgpiod` via binding .NET ou wrapper CLI;
- petite couche native/script si besoin, mais a eviter au debut.

Recommandation: creer des interfaces applicatives (`IButtonInput`, `ILightOutput`, `ILightSensor`) puis une implementation Linux. Cela permettra aussi une implementation fake pour developper sans Raspberry Pi.

## Plan de migration propose

### Phase 1 - Decoupage sans changer le comportement

- Creer un projet de domaine portable, par exemple `Photobooth.Core`.
- Extraire les concepts:
  - `IGoProClient`;
  - `IPhotoDisplay`;
  - `IButtonInput`;
  - `ILightOutput`;
  - `IPhotoBoothWorkflow`.
- Garder l'app UWP comme hote temporaire.
- Adapter `MainPage.xaml.cs` pour appeler cette couche extraite.

Resultat attendu: le comportement Windows IoT reste identique, mais la logique n'est plus prisonniere de UWP.

### Phase 2 - Porter le client GoPro

- Creer un client GoPro portable en .NET moderne.
- Supprimer les types UWP:
  - remplacer `BitmapImage` par `byte[]` ou `Stream`;
  - supprimer `IRandomAccessStream`;
  - garder les modeles JSON.
- Ajouter une strategie d'attente explicite pour la disponibilite du dernier media, au lieu de supposer immediatement que le dernier fichier est pret.

Resultat attendu: le client GoPro compile et peut etre teste depuis Windows ou Linux sans UI.

### Phase 3 - Porter la couche Raspberry Pi

- Refaire `Button`, `SwitchPin`, `ShiftRegister`, `SevenSegmentLed` avec une API Linux.
- Implementer le debounce logiciel.
- Gerer proprement l'absence de GPIO pour le developpement local.
- Valider les pins:
  - photo: GPIO `18`;
  - video: GPIO `20`;
  - lumiere: GPIO `17`.

Resultat attendu: un executable Linux peut lire les boutons et piloter la lumiere.

### Phase 4 - Refaire l'interface

- Reproduire l'experience visuelle:
  - fond;
  - trois cadres photo superposes;
  - compte a rebours;
  - indicateur `Rec`;
  - affichage derniere photo;
  - diaporama.
- Remplacer les `DropShadowPanel`, `Canvas.SetZIndex`, `BitmapImage` et chemins assets UWP.
- Prevoir un mode plein ecran/kiosk.

Resultat attendu: UI Linux utilisable sans dependances Windows.

### Phase 5 - Deploiement Raspberry Pi OS

- Produire un publish Linux ARM/ARM64.
- Installer les dependances systeme.
- Configurer I2C/GPIO.
- Creer un service `systemd` pour le backend.
- Si UI web/kiosk: configurer Chromium en plein ecran au demarrage.
- Documenter la connexion Wi-Fi GoPro.

Resultat attendu: photobooth demarrable automatiquement sur Linux.

## Risques a traiter pendant la migration

- La GoPro peut etre lente a rendre disponible le fichier apres capture.
- Le reseau Wi-Fi GoPro peut couper l'acces Internet du Raspberry Pi.
- Les permissions GPIO/I2C sous Linux peuvent varier selon distribution et modele de Raspberry Pi.
- Le mapping des numeros GPIO doit etre confirme: garder la numerotation BCM si c'est ce que la lib Linux utilise.
- Le comportement des boutons en pull-up/falling-edge doit etre reteste physiquement.
- Les erreurs sont actuellement souvent ignorees par des `catch` vides; la version Linux doit logger sans casser l'experience utilisateur.
- Le code actuel melange UI, hardware et GoPro dans `MainPage.xaml.cs`; migrer en une seule etape serait risque.

## Checklist des remplacements

- [ ] Remplacer `TargetPlatformIdentifier=UAP`.
- [ ] Supprimer `Microsoft.NETCore.UniversalWindowsPlatform`.
- [ ] Supprimer les imports `Microsoft.Windows.UI.Xaml.CSharp.targets`.
- [ ] Remplacer `Package.appxmanifest`.
- [ ] Remplacer `Windows.UI.Xaml.*`.
- [ ] Remplacer `Microsoft.Toolkit.Uwp.*`.
- [ ] Remplacer `Windows.Devices.Gpio.*`.
- [ ] Supprimer `BitmapImage` de la couche GoPro.
- [ ] Supprimer `IRandomAccessStream`, `InMemoryRandomAccessStream`, `DataWriter`.
- [ ] Remplacer `DispatcherTimer` et `Dispatcher.RunAsync`.
- [ ] Implementer un debounce bouton portable.
- [ ] Ajouter une configuration GoPro/IP/pins.
- [ ] Ajouter un mode developpement sans hardware.
- [ ] Ajouter logs et diagnostics.
- [ ] Ajouter documentation de deploiement Linux.

## Conclusion

Le portage est faisable, mais ce n'est pas un simple changement de target framework. L'application actuelle est construite autour de UWP et Windows IoT. La bonne strategie est de conserver la logique photobooth et le protocole GoPro, puis de remplacer les couches host/UI/hardware par des equivalents Linux.

Le plus gros chantier est l'interface et l'architecture applicative. Le plus facile a reutiliser est le client GoPro, a condition de le rendre independant de `BitmapImage` et des streams UWP.
