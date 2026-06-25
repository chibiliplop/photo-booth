# Architecture (.NET 8 / Avalonia)

État réel du code dans `src/`. Document de référence (source unique) pour les **décisions de rendu** (DRM/FBDev) et de **packaging**.

## Vue d'ensemble

La borne est une application **.NET 8** unique : un exécutable Avalonia en kiosk plein écran qui pilote une GoPro (Wi-Fi, HTTP + UDP), les boutons et la lumière (GPIO), et optionnellement une imprimante (CUPS) et un hôte web d'admin/debug embarqué.

Le code est découpé en cinq projets en couches, du domaine pur vers l'hôte UI. Les dépendances ne vont que vers l'intérieur : `Core` ne référence rien d'applicatif ; tout le reste dépend de `Core`.

| Projet | Rôle | Dépend de |
| --- | --- | --- |
| `Photobooth.Core` | Domaine pur : abstractions (`IGoProClient`, `IPhotoDisplay`, `IButtonInput`, `ILightOutput`, `ILightSensor`, `IPrinterAdapter`), workflow acteur, options + validation, modèles GoPro JSON, télémétrie diagnostic, exception de résilience. **Aucune dépendance UI ni hardware** (uniquement `Microsoft.Extensions.Options` et `Logging.Abstractions`). | — |
| `Photobooth.Adapters` | Implémentations concrètes : `HttpGoProClient`/`FakeGoProClient` ; GPIO/I2C Linux (`System.Device.Gpio`) + fakes ; adaptateurs d'impression (CUPS / fichier / no-op). | `Core` |
| `Photobooth.Admin` | Hôte web d'admin/debug embarqué (Kestrel, `FrameworkReference Microsoft.AspNetCore.App`), opt-in. | `Core` |
| `Photobooth.App` | UI Avalonia (kiosk) + **composition root** : DI, configuration, Serilog, `appsettings.json`, assets, point d'entrée `Main`. | `Core`, `Adapters`, `Admin` |
| `Photobooth.Tests` | Tests xUnit (workflow, observabilité, admin), pilotés par les fakes. | tous |

Versions centralisées dans `src/Directory.Build.props` : `net8.0`, `Nullable=enable`, `ImplicitUsings=enable`, Avalonia `11.2.3`.

## Workflow acteur

`Photobooth.Core/Workflow/PhotoboothWorkflow.cs` est l'orchestrateur, implémenté en **acteur** : un seul consommateur draine un `Channel<BoothCommand>` (`SingleReader`) et est le **seul à écrire l'état**. C'est la propriété structurelle qui élimine les courses de l'ancien code à thread partagé. Le workflow ne tourne **jamais** sur le thread UI : il pousse des requêtes de rendu vers `IPhotoDisplay`, dont l'implémentation (le ViewModel) se charge elle-même de marshaler vers le thread UI.

### États (`BoothState`)

L'énumération `Photobooth.Core/Workflow/BoothState.cs` a **cinq** valeurs (et non quatre) :

| État | Signification |
| --- | --- |
| `Idle` | Au repos ; le diaporama tourne, les appuis bouton sont acceptés. |
| `Capturing` | Séquence photo en cours, ou décompte d'amorce vidéo (le décompte tient la borne en `Capturing`, ce qui bloque le diaporama et neutralise les appuis). |
| `Recording` | Enregistrement vidéo en cours. |
| `Degraded` | GoPro injoignable : la borne reste vivante et tente de se reconnecter (le diaporama ou le moniteur de connectivité repassent en `Idle` au retour). |
| `ShuttingDown` | Extinction propre : lumière éteinte, boucles arrêtées. |

L'état courant est exposé via `State` (lecture `Volatile`, sûre depuis n'importe quel thread) ; chaque transition passe par le point unique `SetState`, qui notifie aussi la télémétrie.

### Commandes (`BoothCommand`)

Records discrets fournis au consommateur (`Photobooth.Core/Workflow/BoothCommand.cs`) : `PhotoRequested`, `PrintRequested`, `VideoToggleRequested`, `VideoAutoStop(Epoch)`, `Recovered`, `Shutdown`. Les boutons physiques et le clavier sont routés vers le canal via `Submit` (exposé proprement aux composants externes — l'hôte admin — par l'interface `IBoothCommandSink`, sans leur donner toute la surface du workflow).

Les appuis qui s'accumulent pendant une séquence sont jetés (`DrainButtonCommands`) : un invité qui martèle le bouton obtient **une** photo, pas une file. L'auto-stop vidéo utilise un compteur d'**epoch** : un `VideoAutoStop` n'arrête la prise que si l'epoch correspond toujours, ce qui empêche d'arrêter une nouvelle prise avec le minuteur de l'ancienne.

### Boucles indépendantes

`StartAsync` lance quatre tâches en parallèle, pour qu'une capture lente ne puisse jamais affamer les autres :

- **Consommateur** (`ConsumeLoopAsync`) : traite les commandes ; toute exception non gérée force un `SafeReset` (lumière éteinte, retour `Idle`).
- **Keepalive GoPro** (`KeepAliveLoopAsync`) : envoie le paquet UDP keepalive sur un `PeriodicTimer` ; ne doit jamais s'interrompre, sinon la caméra se met en veille.
- **Diaporama** (`SlideshowLoopAsync`) : en `Idle`/`Degraded`, affiche une image aléatoire de la GoPro ; un succès en `Degraded` est une voie de récupération automatique.
- **Connectivité** (`ConnectivityLoopAsync`) : sonde de joignabilité GoPro sur un minuteur lent ; **seul** rédacteur du point de connectivité persistant ; agit uniquement sur transition (pas de clignotement) et uniquement en `Idle`/`Degraded` (ne se bat jamais avec une capture en cours).

L'accès aux médias GoPro entre le diaporama et une capture est sérialisé par un unique `SemaphoreSlim` (`_goproGate`). La séquence photo elle-même prend un **snapshot** des fichiers existants *avant* le déclenchement, puis attend l'apparition d'un fichier **nouveau** (`WaitForNewPhotoAsync`) dans la limite du délai de capture — jamais de retour d'une photo périmée. Un **watchdog** par opération force un reset si une séquence dépasse `Timings.WatchdogSeconds`.

## Abstraction GoPro

`Photobooth.Core/Abstractions/IGoProClient.cs` est le contrat neutre, sans UI : `SetSinglePhotoModeAsync`, `SetVideoModeAsync`, `TriggerAsync`, `StopAsync`, `ListMediaAsync`, `DownloadMediaAsync`, `SendKeepAliveAsync`, `IsReachableAsync`. Les médias circulent en **`byte[]`** (jamais de type image dépendant d'un framework UI). Les modèles JSON (`Photobooth.Core/GoPro/GoProMedia.cs`) sont désérialisés via `System.Text.Json`.

Deux implémentations dans `Photobooth.Adapters/GoPro/` :

- **`HttpGoProClient`** : caméra réelle (ou simulateur Python local) en HTTP + UDP. Un seul `HttpClient` et un seul `UdpClient` partagés (pas d'allocation par appel). Retry borné par `MaxRetries` avec timeout par tentative + budget global + annulation (jamais de boucle infinie) ; à l'épuisement, lève `GoProUnavailableException` (`Photobooth.Core/Resilience/`) plutôt que de retourner `null`. Le timeout interne de `HttpClient` est désactivé : les délais sont pilotés au cas par cas via des `CancellationTokenSource` liés.
- **`FakeGoProClient`** : substitut sans réseau pour le développement sans caméra (et repli opérationnel quand `Gopro.Mode = fake`). Maintient une liste de médias qui grossit à chaque `TriggerAsync`, ce qui exerce réellement la logique « attendre un nouveau fichier » du workflow.

Le choix se fait à la composition (`ServiceConfiguration`) selon `Gopro.Mode` (`http` par défaut, ou `fake`).

## Couche hardware Linux + fakes

Les abstractions hardware sont dans `Photobooth.Core/Abstractions/HardwareInterfaces.cs` : `IButtonInput` (événements `PhotoPressed`/`VideoPressed`/`PrintPressed`, debounce interne), `ILightOutput` (`On`/`Off`, active-high), `ILightSensor` (lecture lux ; non consommé par le workflow v1, conservé pour une auto-exposition future).

Implémentations réelles dans `Photobooth.Adapters/Hardware/Linux/` (via `System.Device.Gpio`, qui fournit aussi `System.Device.I2c`) :

- **`GpioButtonInput`** : ouvre les pins en pull-up interne quand c'est supporté (repli `Input`), réagit sur le front descendant, debounce logiciel. Le callback d'edge ne fait que debouncer + lever l'événement (jamais de travail sur le thread d'événement du driver). Une fenêtre de grâce de 500 ms après l'armement absorbe le faux front parfois émis quand le pull-up interne s'active.
- **`GpioLightOutput`** : lumière active-high sur un pin de sortie ; démarre OFF et n'est jamais laissée allumée au `Dispose`.
- **`Max44009LightSensor`** : capteur de luminosité ambiante via I2C (adresse `0x4A` par défaut, bus `/dev/i2c-1`). Activé seulement si `Hardware.LightSensorEnabled`.

Le **debounce** (`Photobooth.Adapters/Hardware/Debouncer.cs`) utilise l'horloge monotone `Stopwatch` (insensible aux changements NTP) et est lock-free, sûr depuis le thread d'événement GPIO.

Les **fakes** (`Photobooth.Adapters/Hardware/Fake/FakeHardware.cs`) couvrent le développement hors Pi et les tests : `FakeButtonInput` (aucun edge spontané ; l'App route le clavier directement vers le workflow), `FakeLightOutput` (en mémoire, expose `IsOn`), `NoOpLightOutput` (borne câblée sans lumière : ne touche jamais le pin, silencieux), `FakeLightSensor` (lecture constante).

La résolution du hardware est centralisée dans `Photobooth.App/Composition/HardwareBundle.cs`, avec **repli gracieux** : `auto` choisit le GPIO réel uniquement si on est sous Linux *et* que `/dev/gpiochip0` existe (`linux` force, `fake` désactive). Si le GPIO était attendu mais échoue (câblage, droits, pin occupé), on **ne crash-loop pas** : repli en mode clavier + message d'erreur affiché à l'écran kiosk (seule sortie de l'opérateur sur site).

## Impression

`IPrinterAdapter` (`Photobooth.Core/Abstractions/IPrinterAdapter.cs`) imprime un JPEG (`byte[]`) et expose `IsEnabled`. Trois implémentations dans `Photobooth.Adapters/Printing/`, choisies selon `Printer.Type` :

- **`CupsPrinterAdapter`** (`cups`) : chemin Linux normal, pipe les octets vers la commande `lp` (imprimante, copies, média, options CUPS) ; un code de sortie non nul lève une erreur avec la vraie raison (`stderr`).
- **`FilePrinterAdapter`** (`file`) : écrit le JPEG dans un dossier (export / tests).
- **`NoOpPrinterAdapter`** (par défaut, `disabled`) : `IsEnabled = false`, ne fait rien.

## Diagnostic / télémétrie

`Photobooth.Core/Diagnostics/BoothTelemetry.cs` est un snapshot thread-safe (verrou) de l'état diagnostic vivant : état courant (`RecordState`, appelé depuis le point unique `SetState`), dernière joignabilité GoPro (`RecordGoProReachable`), et résultat de la dernière impression — `RecordPrintSuccess` / `RecordPrintFailure(reason)`, exposé en `PrintResult` immuable. La raison d'échec d'impression, autrefois avalée, est désormais conservée et lisible par l'hôte admin.

Le logging applicatif passe par **Serilog** (console + fichier rotatif + `InMemoryLogSink` en ring buffer), configuré dans `Program.cs`.

## Hôte d'admin/debug

`Photobooth.Admin` est un hôte **Kestrel** embarqué (`AdminWebHost`), démarré **uniquement** si `Admin.Enabled` (off par défaut). Il récupère les singletons partagés du conteneur racine et les re-déclare dans son conteneur interne (forward). Tout échec de démarrage est loggé puis avalé : la borne n'est **jamais** dégradée par le mode debug. Auth par PIN optionnel + CSRF. Détails fonctionnels : [`admin-debug.md`](admin-debug.md).

## Composition root

`Photobooth.App/Composition/ServiceConfiguration.cs` câble tout le graphe DI : binding des sections d'options + validation, Serilog, le `MainViewModel` qui **est** la surface `IPhotoDisplay`, le client GoPro, l'imprimante, le `HardwareBundle`, la télémétrie, le workflow (exposé aussi en `IBoothCommandSink`) et l'hôte admin.

`Program.cs` construit la configuration en couches : `appsettings.json` embarqué → surcharge optionnelle `photobooth.json` (depuis `/boot/firmware/photobooth/`, ou `PHOTOBOOTH_CONFIG_DIR`, repli dev `./config/`) → variables d'environnement préfixées `PHOTOBOOTH_`. Une config invalide saisie par un opérateur **ne renvoie jamais** un code non nul (sinon crash-loop écran noir sous `systemd Restart=always`) : on logge et on démarre en mode dégradé. Les sections d'options (`Gopro`, `Hardware`, `Timings`, `Theme`, `Printer`, `Admin`) sont détaillées dans [`../monter-et-utiliser/config-reference.md`](../monter-et-utiliser/config-reference.md).

## Rendu (Avalonia) — DRM vs FBDev

L'UI est **Avalonia 11** (`Avalonia.Desktop` + `Avalonia.LinuxFramebuffer`). Avalonia est retenu parce qu'il est multiplateforme et proche du monde XAML/C# : le code tourne tel quel sur le poste de dev (macOS/Windows, fenêtre classique) et sur le Pi (kiosk), sans front web séparé à maintenir.

`Program.cs` choisit le backend de rendu selon les arguments :

- **`--drm` → `StartLinuxDrm` : rendu DRM/KMS, accéléré matériellement** (GPU VideoCore IV via Mesa vc4). Transforms et opacité sont accélérés, les animations sont nettement plus fluides. C'est le mode **par défaut du service** turnkey (`deploy/systemd/photobooth.service` : `ExecStart=… --drm`).
- **`--fbdev` → `StartLinuxFbDev` : rendu logiciel** (framebuffer). Plus lent mais robuste ; **fallback** documenté si un écran/modèle donne un écran noir ou une instabilité EGL (alors avec `LIBGL_ALWAYS_SOFTWARE=1` + `GALLIUM_DRIVER=llvmpipe`).
- Sans flag, `StartWithClassicDesktopLifetime` : fenêtre de bureau (dev). `--fullscreen` la passe en plein écran sans décorations.

Point d'attention : **`AVALONIA_RENDERER=software` est un no-op en Avalonia 11** (c'était une variable d'Avalonia 0.10). On ne s'y fie pas : pour forcer du logiciel sous DRM, on passe par les variables Mesa ci-dessus, ou on bascule sur FBDev. D'après les mainteneurs Avalonia, **DRM est *toujours* accéléré et FBDev *toujours* logiciel** — c'est ce qui motive le choix de `--drm` par défaut sur le Pi validé.

## Packaging & exécution

- **Publication self-contained `linux-arm64`** (le runtime .NET et ASP.NET — embarqué via `FrameworkReference` — sont inclus : rien à installer sur le Pi). Cible 64-bit recommandée ; un OS 32-bit (`armv7l`) imposerait `linux-arm`.
- **Trimming désactivé** (stabilité), **ReadyToRun activé** (démarrage plus rapide). La commande de référence vit dans le pipeline d'image (`image-builder/build-local.sh`) :
  ```bash
  dotnet publish src/Photobooth.App/Photobooth.App.csproj -c Release \
    -r linux-arm64 --self-contained true -p:PublishReadyToRun=true -o publish
  ```
- **GC workstation, non concurrent** (`Photobooth.App.csproj`) : moindre pression mémoire sur le Pi (1 Go) ; le service plafonne en plus le tas .NET (`DOTNET_GCHeapHardLimit`).
- **`systemd` avec `Restart=always`** (`deploy/systemd/photobooth.service`) : c'est le principal levier de stabilité — le kiosk se relance seul après un crash. Le service tourne sur `tty1`, démarre en `--drm`, et appartient aux groupes `gpio i2c video input render`. L'unité complète est dans [`../../deploy/systemd/photobooth.service`](../../deploy/systemd/photobooth.service).
