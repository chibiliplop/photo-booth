# Préparer un événement — à faire AVANT, à la maison

> **Quand ?** À la maison, avant le jour J, tranquillement, sans la pression de l'événement.
>
> **Le jour J sur place →** voir [4-le-jour-j.md](4-le-jour-j.md).

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

## Changer les noms / l'année / le fond pour un événement

Pour chaque nouvel événement, vous pouvez personnaliser l'écran : les **prénoms**, l'**année**, l'**image de fond**. Cela se fait **à la maison, sur un ordinateur**, avant le jour J. Aucun écran technique.

### Étape 1 — Mettre la carte mémoire dans l'ordinateur

- Éteignez la borne (débranchez l'alimentation).
- Retirez délicatement la **carte mémoire** (carte SD) de la borne.
- Insérez-la dans votre ordinateur (fente SD, ou adaptateur USB).
- Ouvrez la carte. Vous y verrez un dossier **`photobooth`**. Ouvrez-le. Vous y trouverez aussi un fichier **`LISEZ-MOI.txt`** qui résume tout.

### Étape 2 — Modifier les fichiers (toujours MODIFIER, jamais créer ni renommer)

> Pour le détail complet de chaque champ (valeurs autorisées, valeur par défaut, effet), consultez la **[référence de configuration](config-reference.md)**.

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

> **Pièges à éviter :** ne **renommez** aucun fichier, n'en **créez** pas ; modifiez ceux qui existent. Si vous ne voyez pas les extensions (`.json`, `.txt`), pas de souci : modifiez les fichiers tels qu'ils apparaissent — Windows peut masquer les extensions, mais les fichiers sont bien là.

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

## Le jour J (sur place)

Branchement, lecture des bandeaux, dépannage terrain, ranger/éteindre → **[4-le-jour-j.md](4-le-jour-j.md)**.
