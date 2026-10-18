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

## 4 bis. Prompts v2 (après un premier essai jugé « pas très moderne »)

Le premier résultat reprenait le pictogramme générique « image » (montagne + soleil) posé sur un écran
en contour sombre : banal, daté, et l'écran disparaissait sur le fond. Les prompts v2 interdisent ce
cliché et visent le style des icônes Android 15 / Windows 11 / iOS 18.

```text
Design a modern app icon (2026 style, like the latest Google Material You and Apple iOS app icons) for
"Mon Album PC", an app that safely brings the family's phone photos home to their PC.

Avoid clichés: NO mountain-and-sun picture placeholder, NO literal computer monitor outline, NO clip art.

Concept: two or three overlapping rounded rectangles (photo cards), slightly fanned, made of vibrant
semi-transparent glass-like layers, where the overlaps create new colors. Together they suggest a
simple house shape or a safe shelter. One single, bold, memorable silhouette.

Style: soft depth, smooth vivid gradients, subtle inner glow, crisp edges, very minimal, no text, no
outlines, filled shapes only. Must remain recognizable at 16x16 pixels.

Colors: rich blue to indigo gradient (#1F4E8C to #4F46E5) with a warm amber/coral accent (#F2A33A to
#FF7A59) on one card. Background: a clean light gradient or deep blue, your choice, filling the square.

Composition: centered, symbol in the central 60%, square 1024x1024. Mood: premium, calm, trustworthy.
```

Variantes de concept à demander dans la même conversation :

```text
Same modern style and colors, give me 4 different concepts:
1. a single photo card with a folded corner turning into a downward arrow, glassy layers,
2. a rounded house silhouette whose window is a glowing photo card,
3. abstract camera-viewfinder corner brackets enclosing a small heart or home shape,
4. a stack of cards being "collected" into a soft rounded box (like a photo album), seen slightly from
   above.
```

## 4 ter. Concept retenu (2026-10-18)

**Cartes photo translucides en éventail qui forment une maison** : bleu, orange et violet, en verre,
sur un fond bleu nuit. C'est l'icône du haut de la planche Gemini de la v2.

Points à corriger à la régénération :
- la maison se lit mal, parce que la carte centrale la recouvre ;
- en petit (16–32 px), il faut une version simplifiée.

Prompt de régénération, en joignant la planche et en précisant « the top icon » :

```text
Recreate the top icon of the attached image as a single app icon, 1024x1024, centered, same glassy
fanned photo cards, same colors (blue, orange, violet overlaps) and same deep navy rounded-square
background.

Improvement: make the house silhouette instantly readable. The house (pitched roof with a small
chimney) must be the clear overall outline formed by the fanned cards, or a crisp lighter house shape
where the cards overlap, not hidden behind the middle card. Keep it minimal: three cards maximum, no
text, no extra details.
```

Puis, dans la même conversation :

```text
Now produce, keeping exactly the same shapes:
1. the symbol alone on a transparent background, centered in the middle 66% (Android adaptive icon
   foreground),
2. a flat simplified version for tiny sizes (16-32 px): two fanned cards with the house shape, no glass
   effect, no gradients, solid colors,
3. a single-color white silhouette and a single-color black silhouette of that simplified version, on
   transparent background (Android notification icon, Windows tray icon).
```

## 5. Contraintes techniques (pour l'export final)

- **Android** : icône adaptative avec une couche de premier plan (le symbole, centré dans 66 % du
  canevas) et une couche de fond (couleur unie). Icône monochrome pour Android 13+ (icônes à thème).
  Icône de notification blanche sur fond transparent.
- **Windows** : `.ico` multi-tailles (16, 20, 24, 32, 48, 256 px). Icônes systray en 16, 20, 24 et
  32 px, en versions claire et sombre, avec 4 états (normal, transfert, attention, erreur) obtenus par
  une pastille de couleur sur l'icône de base.
- Garder le fichier source en haute résolution, 1024×1024 minimum (SVG idéalement, après vectorisation).
