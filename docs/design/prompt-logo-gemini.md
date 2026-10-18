# Prompts Gemini — icône et logo Mon Album PC

L'icône est commune aux deux apps (Android et Windows). Elle est générée avec Gemini, puis intégrée par
Claude Design dans les maquettes.

Les prompts sont en anglais : les modèles d'image le suivent mieux. On ne demande **aucun texte dans
l'icône**. Le nom « Mon Album PC » sera posé à côté, en typographie, par Claude Design ou à la main.

## 1. Prompt principal

```text
Design a flat app icon for "Mon Album PC", an app that automatically backs up the photos of an Android
phone to the family's Windows PC over home Wi-Fi.

Concept: a simple photo symbol (a rounded picture card with a small mountain and a sun) gently sliding
into, or resting inside, a computer monitor outline. The feeling is "your photos are safe at home".

Style: modern flat vector, geometric, bold simple shapes, no gradients or only one very subtle gradient,
no 3D, no shadows except one soft shadow under the photo card, no text, no letters. Must stay
recognizable at 16x16 pixels.

Colors: deep trustworthy blue (#1F4E8C) as the main color, a warm amber accent (#F2A33A) for the sun, white
for the photo card. Solid single-color background filling the square.

Composition: centered, the symbol occupies the central 60% of the canvas (generous padding, the
corners will be cropped into a circle or a rounded square). Square 1024x1024, PNG.

Mood: serious, reassuring, clear, made for non-technical people and elderly users. Not playful, not
childish, not techy.
```

## 2. Variantes à demander ensuite (dans la même conversation)

```text
Give me 4 variations of this icon with different concepts, same style and colors:
1. a stack of two photo cards with a small house roof above them,
2. a photo card with a downward arrow entering a monitor,
3. a monitor whose screen shows a photo (mountain and sun), with a tiny phone beside it,
4. a photo card shaped like a house.
```

## 3. Déclinaisons une fois le concept choisi

```text
Take the chosen icon and produce:
- the foreground symbol alone on a transparent background (for an Android adaptive icon, symbol within
  the central 66% safe zone),
- a single-color white version on transparent background (Android notification icon and dark mode),
- a single-color black version on transparent background (Windows tray icon, light mode),
- a simplified version that still reads at 16x16 pixels (remove small details like the sun if needed).
Keep exactly the same shapes and proportions.
```

## 4. Logo horizontal (facultatif)

```text
Create a horizontal logo: the chosen icon on the left, and on the right the words "Mon Album PC" in a
clean, friendly sans-serif (similar to Inter or Segoe UI, semibold), dark blue #1F4E8C. White background,
plenty of margin. Spell the text exactly: Mon Album PC.
```

Si le texte généré est déformé, on garde l'icône seule et on compose le logo à la main.

## 5. Contraintes techniques (pour l'export final)

- **Android** : icône adaptative avec une couche de premier plan (le symbole, centré dans 66 % du
  canevas) et une couche de fond (couleur unie). Icône monochrome pour Android 13+ (icônes à thème).
  Icône de notification blanche sur fond transparent.
- **Windows** : `.ico` multi-tailles (16, 20, 24, 32, 48, 256 px). Icônes systray en 16, 20, 24 et
  32 px, en versions claire et sombre, avec 4 états (normal, transfert, attention, erreur) obtenus par
  une pastille de couleur sur l'icône de base.
- Garder le fichier source en haute résolution, 1024×1024 minimum (SVG idéalement, après vectorisation).
