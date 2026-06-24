# Électronique — câblage de la borne

> **Pour qui** : le bricoleur qui **monte sa propre borne** et doit câbler les
> boutons, la lumière et les capteurs optionnels sur le header GPIO du Pi.
> Ce document est la **référence unique** pour les faits électroniques du projet.
> Il est lié depuis `2-installation.md` et depuis le bloc `Hardware` de
> [`config-reference.md`](config-reference.md).

---

## 1. Matériel requis (vous le fournissez)

Le projet distribue un **logiciel** ; tout le matériel est à vous :

| Élément | Détail |
|---|---|
| **Raspberry Pi** | Pi **3 / 3B+ / 3A+**, **4 / 400**, ou **Zero 2 W**. Pi 5 = à valider séparément. **Ne marche pas** sur Pi 1 / Zero / Zero W (processeur trop ancien). |
| **Carte microSD** | 16 Go recommandé (8 Go minimum), microSD de marque (endurance importante). |
| **Alimentation** | L'alim **officielle** du Pi — la sous-alimentation est la première source d'instabilité. |
| **Écran** | Entrée HDMI + câble. |
| **Bouton photo** | Bouton poussoir — câblé sur GPIO 18 (voir §2). |
| **Bouton vidéo** | Bouton poussoir — câblé sur GPIO 20 (voir §2). |
| **Lumière** (optionnelle) | LED ou lampe pilotée via relais/MOSFET sur GPIO 17 (voir §2 et §3). |
| **Résistances pull-up** | ~10 kΩ × 2 (pour GPIO 18 et 20) — voir §2. |

---

## 2. Câblage GPIO (numérotation BCM)

Toutes les broches sont données en **numérotation BCM** (les numéros « GPIO »,
pas la position physique des broches sur le header).

### Broches par défaut

| Fonction | Broche BCM | Câblage |
|---|---|---|
| Bouton **photo** | **GPIO 18** | bouton entre la broche et GND (masse). |
| Bouton **vidéo** | **GPIO 20** | bouton entre la broche et GND. |
| **Lumière** (optionnelle) | **GPIO 17** | sortie **active-high** (HIGH = allumé) — piloter via relais ou MOSFET (voir §3). |

### Pull-up externe (boutons)

Un **pull-up externe ~10 kΩ** est recommandé sur GPIO 18 et GPIO 20 (vers 3,3 V) :
le pull-up interne du Pi 3 est peu fiable. Le bouton tire la broche à la masse
(GND) quand on appuie.

Schéma simplifié pour chaque bouton :

```
3,3 V ──┬── ~10 kΩ ──── GPIO 18 (ou 20)
        |
       GND ─── [bouton] ─┘
```

> Le bouton court-circuite la résistance vers GND ; au repos la broche est maintenue
> HIGH (3,3 V) par la résistance.

---

## 3. Avertissement sécurité — sortie lumière

> ⚠️ **Un GPIO du Pi délivre 3,3 V et quelques mA — il ne pilote PAS une lampe
> secteur ou toute charge de puissance directement.**
>
> Pour piloter une lumière sur GPIO 17, **vous devez obligatoirement passer par
> un relais ou un MOSFET** commandé par la broche. Câbler du 220 V directement
> sur un GPIO détruirait le Pi et présente un **risque d'électrocution**.

Si vous n'avez pas de lumière, désactivez-la dans la config :
`LightEnabled: false` — la broche GPIO 17 n'est alors jamais activée (voir §4).

> Les détails du circuit relais 5 V (optocoupleur / SSR, composants, avertissements
> 220 V) sont en cours de rédaction — voir **§5 (À compléter)**.

---

## 4. Capteur de luminosité optionnel — MAX44009

Le capteur MAX44009 est **optionnel et désactivé par défaut**.

- Bus : **I2C bus 1** (GPIO 2 / SDA, GPIO 3 / SCL)
- Adresse : **`0x4A`**
- Activation : via `raspi-config` → Interface Options → I2C → Enable, puis
  vérification avec `sudo i2cdetect -y 1` (doit afficher `0x4a` si le capteur
  est branché).

---

## 5. Changer les broches GPIO (si votre câblage diffère)

Les broches sont **configurables sans recompiler** dans le bloc `Hardware` du
fichier `photobooth.json` (sur la carte SD) :

```jsonc
"Hardware": {
  "Mode": "auto",         // auto = GPIO réels sur Pi ; fake = clavier/sans GPIO
  "PhotoButtonPin": 18,   // broche BCM du bouton photo
  "VideoButtonPin": 20,   // broche BCM du bouton vidéo
  "LightEnabled": true,   // false = borne SANS lumière (broche jamais activée)
  "LightPin": 17          // broche BCM de la sortie lumière (ignorée si LightEnabled=false)
}
```

Valeurs BCM **0 à 27**, sans doublon. Une valeur invalide n'empêche pas le
démarrage : la borne affiche un **bandeau rouge** à l'écran.

Pour la description complète de toutes les clés et leurs valeurs valides, voir
[`config-reference.md`](config-reference.md) — bloc `Hardware`.

---

## À compléter (suivi BACKLOG)

- Schéma de câblage détaillé + nomenclature chiffrée (BOM) : non encore rédigés
  (voir `BACKLOG.md` #30).
- Circuit relais 5 V documenté + avertissement sécurité 220 V : voir `BACKLOG.md` #4.
