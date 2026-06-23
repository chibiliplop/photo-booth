# Borne photo — guide de l'opérateur

Bienvenue ! Ce guide vous explique tout, pas à pas. Pas besoin d'être technicien : il suffit de brancher quelques câbles, et de modifier trois petits fichiers sur une carte mémoire quand vous changez d'événement. Prenez votre temps, tout est prévu pour que ce soit simple et sans risque.

> **À qui s'adresse ce guide** : à l'**utilisateur d'une borne déjà montée** (assemblée par la personne qui l'a construite). Si c'est **vous** qui construisez la borne — vous fournissez le Raspberry Pi, l'écran, les boutons, la GoPro, le câblage — et installez le logiciel, commencez d'abord par **`INSTALLATION_BORNE.md`** (montage, câblage GPIO, flashage de la carte). Ce guide-ci prend la suite, une fois la borne assemblée.

---

## Ce que comprend une borne montée

> *(La borne est assemblée par celui qui la construit ; voici ce qu'elle réunit. Le projet, lui, ne fournit que le logiciel.)*

- **La borne** (le boîtier qui fait tout fonctionner). Une carte mémoire est déjà installée à l'intérieur.
- **L'alimentation de la borne** (le chargeur qui se branche sur une prise de courant).
- **Le câble écran (HDMI)** — le câble qui va vers la télé ou l'écran.
- **Les boutons** (bouton **photo**, bouton **vidéo**) — et, si votre borne en est équipée, une **lumière** (elle s'allume automatiquement pendant la photo ; il n'y a pas de bouton lumière).
- **La caméra GoPro** et son chargeur.
- **Cette fiche** (et, si fournies, les petites fiches plastifiées).

Vous fournissez vous-même : **un écran ou une télévision** (entrée HDMI) et **une prise de courant**.

> Astuce : avant le jour J, vérifiez tranquillement que tout est dans la boîte.

---

## Brancher la borne

L'ordre des branchements est **important**. Suivez-le exactement.

**1. Allumez d'abord la GoPro.**
   - Mettez la GoPro en marche.
   - Vérifiez que son **Wi-Fi est activé**.
   - Réglez sa **mise en veille automatique sur « Jamais »**.

   > Pourquoi d'abord ? La borne cherche la GoPro dès qu'elle démarre. Règle à retenir : **« GoPro allumée, puis on branche le courant de la borne. »**

**2. Branchez l'écran** (câble HDMI entre la borne et l'écran), allumez l'écran, sélectionnez la bonne entrée HDMI.

**3. Branchez les boutons** (photo, vidéo) sur la borne, s'ils ne le sont pas déjà. Si votre borne a une lumière, branchez-la aussi (elle s'allume toute seule pendant la photo).

**4. Branchez l'alimentation de la borne en DERNIER.** C'est ce qui l'allume. Patientez environ une minute.

**5. Attendez le signal vert.** Un **bandeau VERT** indique : GoPro connectée, borne **prête**. Orange ou rouge → voir le dépannage plus bas.

> En résumé : **GoPro allumée → écran → boutons → courant de la borne en dernier → bandeau vert = prête.**

---

## Changer les noms / l'année / le fond pour un événement

Pour chaque nouvel événement, vous pouvez personnaliser l'écran : les **prénoms**, l'**année**, l'**image de fond**. Cela se fait **à la maison, sur un ordinateur**, avant le jour J. Aucun écran technique.

### Étape 1 — Mettre la carte mémoire dans l'ordinateur

- Éteignez la borne (débranchez l'alimentation).
- Retirez délicatement la **carte mémoire** (carte SD) de la borne.
- Insérez-la dans votre ordinateur (fente SD, ou adaptateur USB).
- Ouvrez la carte. Vous y verrez un dossier **`photobooth`**. Ouvrez-le. Vous y trouverez aussi un fichier **`LISEZ-MOI.txt`** qui résume tout.

### Étape 2 — Modifier les fichiers (toujours MODIFIER, jamais créer ni renommer)

**a) `photobooth.json`** — les prénoms, l'année, et (en bas) le mode.
   - Ouvrez-le (double-clic → Bloc-notes). Changez **uniquement le texte entre guillemets** :

```
{
  "Theme": {
    "Names": "Camille & Yann",
    "Year": "2026",
    "BackgroundImage": "/boot/firmware/photobooth/fond.jpg"
  },
  "Gopro": {
    "Mode": "http"
  }
}
```

   - Remplacez `Camille & Yann` par les prénoms, et `2026` par l'année.
   - **Ne touchez pas** à la ligne `BackgroundImage` ni à la structure (guillemets, virgules, accolades).
   - Laissez `"Mode": "http"` pour le vrai événement (voir « mode test » plus bas).
   - Enregistrez et fermez.

**b) `fond.jpg`** — l'image de fond.
   - **Remplacez** ce fichier par votre image, renommée exactement **`fond.jpg`**, en acceptant de remplacer l'existant. Gardez ce nom.

**c) `wifi.txt`** — seulement si vous utilisez **une autre GoPro** que d'habitude.
   - Chaque GoPro a son propre nom de réseau et son mot de passe. Ouvrez le fichier et remplacez le texte après le `=` :

```
GOPRO_SSID=GP12345678
GOPRO_PASSWORD=le-mot-de-passe-de-ma-gopro
```

   - Mettez le **nom de réseau (SSID)** de VOTRE GoPro et son **mot de passe**. Enregistrez.
   - Même GoPro qu'avant ? Vous n'avez **rien** à changer ici.

### Étape 3 — Remettre la carte dans la borne

- Éjectez proprement la carte (clic droit → Éjecter / « Retirer en toute sécurité »).
- Replacez-la dans la borne. Au prochain démarrage, les nouveaux réglages s'appliquent.

> **Pièges à éviter :** ne **renommez** aucun fichier, n'en **créez** pas ; modifiez ceux qui existent. Si vous ne voyez pas les extensions (`.json`, `.txt`), pas de souci : modifiez les fichiers tels qu'ils apparaissent.

---

## Vérifier que ça marche (mode test sans GoPro)

Vous pouvez tout tester **la veille, sans la GoPro**, grâce au **mode test** : la borne fait de « fausses » photos pour vérifier écran, boutons et affichage.

### Activer le mode test

- Mettez la carte dans l'ordinateur, ouvrez `photobooth.json`.
- Tout en bas, remplacez `"Mode": "http"` par **`"Mode": "fake"`**. Enregistrez.
- Remettez la carte dans la borne.

### Faire le test

1. Branchez l'**écran** puis l'**alimentation** (pas besoin de GoPro en mode test).
2. Vérifiez que les **bons prénoms, la bonne année, le bon fond** s'affichent.
3. **Bouton photo** : une séquence se lance, une (fausse) photo s'affiche.
4. **Bouton vidéo** : l'écran indique l'enregistrement.
5. **La lumière** (si votre borne en est équipée) : elle s'allume pendant la prise de la photo (étape 3), puis s'éteint. Il n'y a pas de bouton dédié.

### Repasser en mode réel (très important !)

- Remettez la carte dans l'ordinateur, rouvrez `photobooth.json`, remettez **`"Mode": "http"`**. Enregistrez.
- Remettez la carte dans la borne.

> Tant que `"Mode"` est sur `"fake"`, la borne **ne prend pas de vraies photos**. Pensez à le remettre sur `"http"` avant le vrai événement.

---

## Si l'écran reste noir / si la photo ne s'affiche pas

Pas de panique. La borne se relance toute seule. Procédez dans l'ordre.

### Niveau 1 — Écran noir (rien ne s'affiche)

Presque toujours un problème de courant ou de câble.
- Écran **allumé** et sur la bonne entrée **HDMI** ?
- **Alimentation de la borne** bien branchée (prise + borne) ?
- **Débranchez puis rebranchez l'alimentation de la borne.** Attendez ~1 minute : elle redémarre seule.
- Câble **HDMI** bien enfoncé des deux côtés ?

### Niveau 2 — Bandeau ORANGE qui reste (GoPro pas trouvée)

L'écran marche, mais la borne ne « voit » pas la caméra.
- GoPro **allumée**, **Wi-Fi activé**, veille sur « Jamais » ?
- **Éteignez puis rallumez la GoPro.** Le bandeau repasse souvent au vert tout seul.
- Si vous avez changé de GoPro, vérifiez que `wifi.txt` correspond bien à **cette** caméra.

### Niveau 3 — Bandeau ROUGE en cours de soirée (GoPro décrochée)

Souvent la GoPro s'est endormie ou la batterie est faible.
- **Rallumez la GoPro** (rebranchez-la sur le secteur si besoin).
- La borne **se reconnecte automatiquement** : attendez le retour du vert.
- Sinon, refaites un **débranchement/rebranchement de l'alimentation de la borne**.

> Règle d'or : **9 fois sur 10, éteindre/rallumer la GoPro ou débrancher/rebrancher la borne règle le problème.** Vous ne pouvez rien casser.

### Interface de dépannage à distance (avancé — seulement si on vous le demande)

La borne dispose d'une petite **page web de dépannage** que la personne qui l'entretient peut utiliser à distance. Elle est **éteinte par défaut** et vous n'en avez **pas besoin** pour un événement normal.

Si votre mainteneur vous demande de l'activer, c'est une seule chose : dans `photobooth.json` (la même carte mémoire que les prénoms), ajoutez le bloc qu'il vous indique, **avec toujours un code PIN** :

```
  "Admin": { "Enabled": true, "Pin": "votre-code-ici" }
```

> ⚠️ **Toujours un PIN**, et seulement sur demande : cette page permet de piloter la borne. Ne l'activez pas « pour voir », et remettez `"Enabled": false` (ou retirez le bloc) une fois le dépannage terminé.

---

## Ranger / éteindre proprement

1. **Débranchez l'alimentation de la borne.** Il n'y a pas de bouton « arrêter » : couper le courant est la façon normale d'éteindre. La lumière s'éteint automatiquement.
2. **Éteignez la GoPro** et mettez-la à charger.
3. Débranchez le **câble HDMI** et les **boutons**.
4. Laissez la **carte mémoire dans la borne** (sauf si vous préparez le prochain événement).
5. Rangez chaque élément dans la boîte.

> Rien à « sauvegarder » avant de débrancher : la borne est faite pour qu'on lui coupe simplement le courant.

---

### En cas de doute

- **GoPro allumée d'abord, puis courant de la borne en dernier, et on attend le bandeau vert.**
- En cas de souci : **on éteint/rallume la GoPro, ou on débranche/rebranche la borne.**

Bon événement !
