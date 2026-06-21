# Tester sans GoPro

## Probleme

L'application actuelle suppose qu'une GoPro est disponible sur le reseau Wi-Fi a l'adresse `10.5.5.9`.

Cette dependance bloque les tests de developpement:

- impossible de valider le compte a rebours sans camera;
- impossible de tester la recuperation de la derniere photo;
- impossible de verifier les erreurs reseau;
- difficile de travailler sur Linux ou sur PC sans le materiel complet.

## Solution recommandee

Mettre en place deux niveaux de test:

1. **Simulateur GoPro HTTP/UDP** pour tester les appels reseau reels sans camera.
2. **Interface `IGoProClient` + fake in-memory** pour tester la logique photobooth sans reseau.

Le simulateur est utile pour l'integration. Le fake in-memory est utile pour les tests rapides et le developpement de l'UI.

## Etat actuel du code

Les endpoints GoPro sont cables en dur:

- `GoProWifi/GoproWifi.cs`
  - `http://10.5.5.9/gp/gpControl/...`
  - `http://10.5.5.9:8080/gp/gpMediaList`
  - `http://10.5.5.9:8080/videos/DCIM/...`
- `CS/MainPage.xaml.cs`
  - keepalive UDP vers `10.5.5.9:8554`

Pour pouvoir tester sans camera, il faut d'abord rendre cette adresse configurable.

## Simulateur inclus

Un simulateur local est fourni dans:

- `tools/gopro-simulator/simulator.py`
- `tools/gopro-simulator/README.md`

Il simule:

- les commandes `gpControl`;
- le listing media `/gp/gpMediaList`;
- le telechargement d'images `/videos/DCIM/...`;
- un listener UDP compatible avec le keepalive GoPro.

Il utilise une image existante du projet comme fausse photo.

## Changement minimum a faire dans l'app

Introduire une configuration GoPro:

```csharp
public sealed class GoProOptions
{
    public string ControlBaseUrl { get; set; } = "http://10.5.5.9";
    public string MediaBaseUrl { get; set; } = "http://10.5.5.9:8080";
    public string KeepAliveHost { get; set; } = "10.5.5.9";
    public int KeepAlivePort { get; set; } = 8554;
}
```

Puis remplacer les URLs hardcodees par:

- `ControlBaseUrl + "/gp/gpControl/..."`
- `MediaBaseUrl + "/gp/gpMediaList"`
- `MediaBaseUrl + "/videos/DCIM/..."`
- `KeepAliveHost` / `KeepAlivePort` pour UDP.

En mode simulateur local:

```text
ControlBaseUrl = http://127.0.0.1:8080
MediaBaseUrl = http://127.0.0.1:8080
KeepAliveHost = 127.0.0.1
KeepAlivePort = 8554
```

## Architecture cible pour la migration Linux

Extraire une interface:

```csharp
public interface IGoProClient
{
    Task SetSinglePhotoMode();
    Task SetVideoMode();
    Task Trigger();
    Task Stop();
    Task<IReadOnlyList<GoProMediaFile>> ListMedia();
    Task<byte[]> DownloadMedia(string directory, string fileName);
}
```

Implementations:

- `HttpGoProClient`: parle a une vraie GoPro ou au simulateur.
- `FakeGoProClient`: ne fait aucun reseau, retourne des images locales.

Le workflow photobooth ne devrait dependre que de `IGoProClient`.

## Scenarios a tester sans GoPro

- Demarrage app sans camera.
- Appui photo.
- Compte a rebours complet.
- Allumage/extinction lumiere si hardware disponible, sinon fake.
- Creation d'une fausse photo apres `Trigger`.
- Recuperation de la derniere image.
- Diaporama avec plusieurs images.
- Erreur reseau simulee.
- Media temporairement indisponible apres capture.
- Video start/stop et indicateur `Rec`.

## Ordre de mise en place

1. Ajouter la configuration GoPro dans `GoProWifi`.
2. Modifier `MainPage.xaml.cs` pour utiliser la configuration du keepalive.
3. Tester l'app contre `tools/gopro-simulator`.
4. Extraire `IGoProClient`.
5. Ajouter un fake in-memory pour les tests de workflow.
6. Reutiliser la meme abstraction pendant la migration Linux.

## Decision

Le simulateur local est la meilleure premiere etape: il permet de travailler sans camera tout en gardant le protocole GoPro reel. Ensuite, l'interface `IGoProClient` permettra de tester la logique photobooth sans reseau et sans materiel.
