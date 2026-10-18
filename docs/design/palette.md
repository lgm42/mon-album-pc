# Palette Mon Album PC

Palette de référence des deux apps, tirée de l'icône Gemini retenue (cartes en verre bleu, orange et
violet sur fond bleu nuit). Elle **remplace la palette vert sauge** du prototype Android de Claude Design
(`android/README.md`). Le reste de ce prototype reste valable : typographie, rayons, espacements, écrans.

Aperçu : `android/Mon Album PC v5.dc.html` est le prototype v4 recoloré avec cette palette.

## Thème clair

| Rôle | Hex | Remplace |
| --- | --- | --- |
| background | `#F6F7FB` | `#F7F6F1` |
| surface (cartes, lignes, barre nav) | `#E9ECF6` | `#E9EFE9` |
| surface variante | `#E3E7F3` | `#E4E9E3` |
| onBackground / texte | `#141B33` | `#22302C` |
| primary | `#3D4FD1` | `#2F7D6B` |
| primary pressed | `#3242B3` | `#276B5B` |
| primary texte sur fond clair | `#2E3DA8` | `#1F5A4C` |
| primaryContainer | `#DDE2FB` | `#D7ECE4` |
| onPrimaryContainer | `#1C2770` | `#184A3E` |
| primary clair (décors) | `#C9D1F7` | `#C6E0D4` |
| divider | texte à 14 % | |
| warning container / texte / icône | `#FBE7C4` / `#4A3000` / `#A85F00` | inchangé |
| erreur | `#C8463D` | inchangé |

## Thème sombre

| Rôle | Hex | Remplace |
| --- | --- | --- |
| background | `#0B1430` | `#141C1A` |
| surface | `#16224A` | `#1F2A27` |
| texte | `#EEF1FA` | `#EEF3EF` |
| primary | `#9AA8FF` | `#7CC9B3` |
| primary pressed | `#B3BEFF` | `#97D6C4` |
| primary texte | `#C3CCFF` | `#B6E4D6` |
| primaryContainer / on | `#26336E` / `#DDE2FB` | `#1F3D35` / `#B6E4D6` |
| divider | texte à 16 % | |
| warning container / texte / icône | `#3D2A0C` / `#FFDCA0` / `#FFB54A` | inchangé |

Le texte des boutons primaires prend la couleur `background` du thème, comme dans le prototype.

## Couleurs de marque (icône, logo, décors uniquement)

| Nom | Hex | Usage |
| --- | --- | --- |
| Bleu nuit | `#0B1430` → `#1E3058` | fond de l'icône, écran de bienvenue |
| Bleu | `#1757B4` → `#3E8AD7` | carte bleue de l'icône |
| Violet | `#6C62CB` → `#8555E4` | carte violette |
| Orange | `#E8600F` → `#ED9845` | carte orange, petites touches chaleureuses |

L'orange ne sert **jamais pour du texte** : son contraste est insuffisant sur fond clair (3:1). Il ne doit
pas non plus être confondu avec le bandeau d'avertissement : celui-ci reste ambre, sur son propre fond.

## Contrastes vérifiés (WCAG)

| Paire | Ratio |
| --- | --- |
| Texte de bouton (`#F6F7FB`) sur primary clair | 6,1 |
| Primary texte sur background clair | 8,4 |
| Texte sur surface claire | 14,4 |
| onPrimaryContainer sur primaryContainer | 10,4 |
| Texte de bouton (`#0B1430`) sur primary sombre | 8,1 |
| Primary texte sombre sur background sombre | 11,6 |
| Texte sur surface sombre | 13,7 |

## Icône

L'icône est celle de Gemini (voir [`prompt-logo-gemini.md`](prompt-logo-gemini.md), §4 ter). Elle
remplace l'icône « écran de PC » du prototype Claude Design.
