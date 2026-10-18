# Handoff : Mon Album PC — app Android

## Vue d'ensemble
Mon Album PC sauvegarde automatiquement les photos et vidéos d'un téléphone Android sur le PC de la maison, en Wi-Fi local (pas de cloud). Ce package couvre l'app Android : premier lancement / association, galerie avec état de sauvegarde (écran signature), dossiers, réglages, erreurs, mise à jour, téléphone dissocié, notifications.

Public : non-informaticiens, souvent âgés. Interface **en français uniquement, vouvoiement**, vocabulaire du quotidien (jamais « MediaStore », « hash », « TLS », « DCIM »…).

## À propos des fichiers
`Mon Album PC v4.dc.html` est une **référence de design en HTML** (prototype cliquable), pas du code de production. La tâche est de **recréer ces écrans en Jetpack Compose / Material 3** (cible du projet), avec les patterns du codebase. Ouvrir le fichier dans un navigateur (servi en local, ex. `npx serve .`) : le téléphone est cliquable et le panneau de droite permet de sauter à chaque écran / situation et de basculer clair/sombre.

La logique du prototype (classe `Component` en bas du fichier) sert de spécification des états : données factices, machine d'écrans, règles d'affichage du bandeau et des badges.

## Fidélité
**Haute fidélité.** Couleurs, typo, rayons, espacements et textes sont définitifs. Reproduire fidèlement en Compose. Les miniatures sont des **placeholders** (une même photo recadrée + teinte pastel en `multiply`) : en production, afficher les vraies miniatures, sans teinte.

Référence : Samsung Galaxy A35, 412 × 892 dp utiles. Doit fonctionner sur petits écrans et avec la taille de police système agrandie (pas de hauteurs fixes sur les blocs de texte, utiliser `sp`). Cibles tactiles ≥ 48 dp. Contrastes WCAG AA.

---

## Design tokens

### Couleurs — thème clair (palette de marque par défaut)
| Rôle | Hex |
|---|---|
| background | `#F7F6F1` |
| surface (cartes, lignes, barre nav) | `#E9EFE9` |
| onBackground / texte | `#22302C` |
| primary (accent) | `#2F7D6B` |
| primary pressed | `#276B5B` |
| primary texte sur fond clair (lien, libellé actif) | `#1F5A4C` |
| primaryContainer (chip actif, pastille nav, hover) | `#D7ECE4` |
| onPrimaryContainer | `#184A3E` |
| divider | `#22302C` à 14 % |
| warning container (bandeau orange) | `#FBE7C4` / texte `#4A3000` / icône `#A85F00` |
| erreur (badge) | `#C8463D` |

### Couleurs — thème sombre
| Rôle | Hex |
|---|---|
| background | `#141C1A` |
| surface | `#1F2A27` |
| texte | `#EEF3EF` |
| primary | `#7CC9B3` (pressed `#97D6C4`) |
| primary texte | `#B6E4D6` |
| primaryContainer / on | `#1F3D35` / `#B6E4D6` |
| divider | `#EEF3EF` à 16 % |
| warning container | `#3D2A0C` / texte `#FFDCA0` / icône `#FFB54A` |

Le texte des boutons primaires = couleur `background` du thème. Couleurs dynamiques Android 12+ : en option dans les réglages, la palette ci-dessus est le défaut.

### Typographie
Police **Nunito** (Google Fonts, 400/600/700/800) partout.
| Usage | Taille / graisse |
|---|---|
| Titre écran d'accueil (h1) | 36 sp / 800, interligne 1.08 |
| Titre d'étape onboarding (h2) | 28–30 sp / 800 |
| Titre d'onglet (« Vos souvenirs », « Dossiers », « Réglages ») | 26 sp / 800 |
| Grand compteur (« 1 240 ») | 48 sp / 700 |
| Titre de section | 16 sp / 700 |
| Corps principal | 16–17 sp / 400, interligne 1.5 |
| Libellé de ligne | 17 sp / 600 |
| Secondaire (nombre d'éléments, sous-titres) | 14 sp / 400, opacité 70 % |
| Bouton | 16–17 sp / 700 |
| En-tête de mois | 17 sp / 700 |
| Durée vidéo | 11 sp / 600 |

### Rayons
| Élément | Rayon |
|---|---|
| Boutons | pilule (50 %) |
| Chips de filtre | 24 dp (pilule) |
| Cartes surface | 24 dp |
| Lignes (dossiers, réglages) | 22 dp |
| Bandeau d'état | 18 dp |
| Miniatures galerie | 16 dp ; miniature de dossier 14 dp |
| Dialogue | 28 dp |
| Feuille d'info (visionneuse) | 24 dp en haut |
| Snackbar | 14 dp |
| Cases du code | 12 dp |
| Puce durée vidéo | 8 dp |
| Pastilles d'icône d'écran (76 dp), badges, points de progression | cercle |
| Interrupteur | piste pilule 52×30, bouton rond 18 |

### Espacements
Grille de 4 dp. Marges latérales d'écran 16–24 dp (onboarding 24, onglets 16–20). Lignes de liste : padding 10–12 × 16–18 dp, 8 dp entre les lignes, hauteur min 64–72 dp. Galerie : 4 colonnes, gouttière 6 dp, marge 14 dp.

### Élévation
Presque tout est à plat (surfaces teintées, pas d'ombres). Ombre forte seulement sur le bouton flottant et la snackbar : `0 12 32 rgba(0,0,0,.22)`. Pas de traits de séparation entre les lignes : les cartes suffisent.

---

## Navigation
Barre du bas, 3 onglets à parts égales, 80 dp de haut, fond `surface`, coins supérieurs 28 dp :
1. **Photos** (accueil), icône image
2. **Dossiers**, icône dossier
3. **Réglages**, icône engrenage

Onglet actif : pastille 64×32 dp `primaryContainer` derrière l'icône, libellé 14 sp/700 en `primary texte`. Inactif : 14 sp/400, couleur texte. Icônes : **Lucide** (lucide.dev), 24 dp, trait 2, bouts ronds. Équivalents Material Symbols acceptables.

---

## Écrans

### 1. Premier lancement (association)
Mise en page commune : padding 16/24/24 dp, **6 points de progression** en haut (10 dp, espacés de 8 ; actifs = primary, sinon divider), contenu aligné à gauche, action principale pleine largeur 56 dp en bas.

1. **Bienvenue** : logo (carré arrondi 56 dp + « Mon Album **PC** »), collage de 3 photos posées de travers (150 dp, cadre blanc 5 dp, rotations −6°, 3°, −2°), titre « Sauvegardez vos photos sur votre PC, automatiquement. », sous-titre « Elles se copient toutes seules sur l'ordinateur de la maison, par le Wi-Fi. Sans câble, sans abonnement. », 3 atouts (Par le Wi-Fi de la maison · Rangées sur votre PC · Sans rien faire ensuite), bouton **Commencer →**.
2. **Autorisation photos** : pastille ronde primary + icône image, surtitre « Autorisation 1 sur 2 », titre « Accès à vos photos et vidéos », texte « Mon Album PC en a besoin **pour pouvoir les copier sur votre PC**… ». Bouton **Continuer**, puis demande système Android (`READ_MEDIA_IMAGES` / `READ_MEDIA_VIDEO`).
   - « Tout autoriser » → étape notifications.
   - « Sélectionner des photos » (accès partiel Android 14, `READ_MEDIA_VISUAL_USER_SELECTED`) ou refus → **Accès partiel** : pastille warning, « Une partie seulement de vos photos sera sauvegardée », encart « choisissez « Tout autoriser » », boutons **Autoriser toutes les photos** (redemande) / **Continuer quand même**.
3. **Notifications** : « Autorisation 2 sur 2 », « Elles servent **à vous prévenir quand c'est sauvegardé**… », puis demande système `POST_NOTIFICATIONS`.
4. **Scanner le QR code** : caméra en plein écran (CameraX + ML Kit), barre d'état en clair, consigne « Sur le PC, ouvrez **Mon Album PC** et cliquez sur « Ajouter un téléphone ». ». Cadre de 280 dp avec 4 coins primary de 4 dp et une ligne de balayage animée (2,2 s, aller-retour). Lien « Je n'ai pas de caméra qui fonctionne » → **Saisie du code** : 6 cases (3 + 3) de 64 dp, saut automatique, retour arrière vers la case précédente, bouton Valider désactivé tant que le code est incomplet, message d'erreur sous les cases.
5. **Association réussie** : pastille coche, « C'est relié », « Ce téléphone est relié à **PC-SALON**, album de **Marie**. », 2 cellules Ordinateur / Album.
6. **Choix des dossiers** : même liste que l'onglet Dossiers, dossiers recommandés cochés, bouton **Valider · 4 dossiers · 10 312 éléments** (dynamique). Pas de confirmation quand on décoche pendant l'onboarding.
7. **C'est parti** : compteur « 1 240 / 10 312 » qui avance, barre de progression 8 dp, « Environ 2 h restantes… », carte « **Vous pouvez fermer l'app**, la sauvegarde continue. », bouton **Voir mes photos**.

**Erreurs d'association** (pastille warning + titre + texte + 2 boutons) :
- QR expiré ou déjà utilisé : « Ce QR code n'est plus valable » · Scanner à nouveau / Saisir le code à la place.
- PC introuvable : « Le téléphone et le PC doivent être connectés au même Wi-Fi. » + 3 vérifications numérotées · Réessayer / Ouvrir les réglages Wi-Fi.
- Déjà associé : « Ce téléphone est déjà relié à un autre PC » (BUREAU-PAPA), rien n'est supprimé · Dissocier et relier à PC-SALON / Annuler.

### 2. Photos (accueil) — écran signature
De haut en bas :
- **En-tête** : icône d'app 28 dp, « Bonjour Marie » (14 sp, 70 %), titre « Vos souvenirs ».
- **Bandeau d'état** (carte 18 dp, marges 16) : icône + **début en gras** + suite. Toucher le bandeau ouvre l'aide si un lien est présent. Variantes :
  | Situation | Style | Texte | Lien |
  |---|---|---|---|
  | ok | surface | **Tout est sauvegardé** · dernière sauvegarde il y a 2 h | — |
  | en attente | surface | **52 photos et vidéos en attente** · sauvegarde dès que le PC est joignable | — |
  | en cours | surface + barre primary de 4 dp en bas | **Sauvegarde en cours** · 12 / 52 | — |
  | PC introuvable | warning | **PC-SALON introuvable depuis 3 jours** | « Que faire ? » → dialogue |
  | disque débranché | warning | **Le PC ne peut pas recevoir les photos** : son disque de destination est débranché | « Aide » → dialogue |
  | erreurs | surface, icône primary | **3 éléments n'ont pas pu être sauvegardés** · nouvel essai automatique | « Voir » → filtre Erreurs |
  | vide | surface | **Aucune photo à sauvegarder** pour l'instant | — |
- **Carte mise à jour** (si le PC propose une version plus récente) : « Une nouvelle version de Mon Album PC est disponible. » · **Mettre à jour** (→ installeur système Android) / Plus tard.
- **Filtres** en chips 48 dp défilant horizontalement : Tout · En attente · Erreurs · Non suivis, chacun avec son nombre. Actif : fond primaryContainer, texte onPrimaryContainer.
- **Grille** : 4 colonnes, miniatures carrées, groupées par mois (en-tête « Octobre 2026 » + « 18 éléments »). Les vidéos ont une puce durée en bas à gauche (fond `#201E1DCC`, icône lecture + « 1:42 »).
- **Badge d'état** : rond de 20 dp en bas à droite :
  | État | Fond | Icône | Autre |
  |---|---|---|---|
  | Sauvegardé | `#201E1D` à 60 % | coche blanche | discret, état normal |
  | En attente | blanc, contour 1,5 texte | horloge | |
  | En cours | `#201E1D`, contour primary | flèche montante | barre de progression de 4 dp en haut de la miniature |
  | Erreur | `#C8463D` | « ! » cerclé blanc | |
  | Non suivi | `#F3F2F2`, contour gris | tiret | miniature à 35 % d'opacité |
- **Poignée de défilement rapide** à droite (28×48, rayon 14, couleur texte). Elle apparaît pendant le défilement, se glisse, et affiche une bulle avec le mois courant. Elle disparaît 1,4 s après l'arrêt.
- **Bouton flottant** « Sauvegarder maintenant » (56 dp, primary, en bas à droite), masqué pendant une sauvegarde ou si la galerie est vide. Si le PC est introuvable ou le disque débranché : snackbar explicative au lieu d'une action.
- **États vides** : par filtre (« Rien en attente », « Aucune erreur », « Tout est suivi »), et global « Aucune photo dans les dossiers suivis » avec un bouton Choisir les dossiers.

**Visionneuse plein écran** (fond noir) : retour, date (« Photo · 14 octobre 2026, 18:30 »), position « 3 / 104 ». Balayage gauche/droite (seuil 50 dp) avec boutons ‹ › en secours. Vidéo : bouton lecture rond de 72 dp et barre de progression. **Feuille d'info** en bas (rayon 24 en haut) : badge 28 dp + titre d'état + phrase, puis cellules Dossier / Taille.
- Sauvegardé : « Sauvegardé le 14/10/2026 sur PC-SALON. »
- En attente : « Sera envoyé sur PC-SALON dès que le PC est joignable. »
- En cours : « Copie vers PC-SALON · 64 % »
- Erreur : motif (ex. « Le disque du PC était plein. Nouvel essai automatique dès qu'il y aura de la place. ») + bouton **Réessayer maintenant**.
- Non suivi : « Le dossier « Snapchat » n'est pas sauvegardé. » + bouton **Sauvegarder ce dossier**.

### 3. Dossiers
Titre + résumé « 4 dossiers · 10 312 éléments ». Trois sections (titre 16 sp/700 + « 2 sauvegardés ») :
1. **Recommandés** (cochés par défaut) : Appareil photo, Captures d'écran, WhatsApp Images, WhatsApp Video.
2. **Autres applications** : Photos envoyées WhatsApp, Statuts WhatsApp, Snapchat, Instagram…
3. **Autres dossiers** : Téléchargements, Bluetooth, Appareil photo + étiquette « Carte SD ».

Chaque ligne est une carte 22 dp : miniature 52 dp (dernière photo, 50 % si décoché), nom 17/600, « 8 412 éléments », interrupteur. Toute la ligne est cliquable (`role=switch`). Pour **décocher**, un dialogue demande « Ne plus sauvegarder « X » ? », avec la note « Les photos déjà sauvegardées restent sur votre PC. » · Ne plus sauvegarder / Annuler. Le bouton **Ajouter un dossier…** ouvre le sélecteur Android (`ACTION_OPEN_DOCUMENT_TREE`).

### 4. Réglages
- **Mon PC** : carte avec icône écran, « PC-SALON » (20/700), « Album de Marie », « Dernière sauvegarde : aujourd'hui à 12:31 », et un bouton icône corbeille de 48 dp. La confirmation dit « Dissocier ce téléphone ? », avec la note « Rien n'est supprimé : ni sur le téléphone, ni sur le PC. »
- **Sauvegarde** : interrupteur « Sauvegarder uniquement pendant la recharge » (désactivé par défaut) · Notifications → réglages système (`ACTION_APP_NOTIFICATION_SETTINGS`) · Brancher en USB →.
- **À propos** : Version (« 1.4.0 · à jour » ou « la 1.5.0 est disponible » + bouton Mettre à jour) · Licence.

**Aide USB** : 3 étapes numérotées (gros chiffres primary) :
1. Brancher le câble.
2. Toucher la notification « Chargement de cet appareil via USB ».
3. Choisir « Transfert de fichiers ».

Chaque étape a une petite illustration qui reproduit l'interface Android (notification, puis menu USB avec « Transfert de fichiers / Android Auto » sélectionné). Les termes système s'écrivent toujours entre guillemets, exactement comme le téléphone les affiche.

### 5. Téléphone dissocié
Écran plein : « Ce téléphone n'est plus relié à PC-SALON », « Le PC l'a retiré de sa liste… », note « Rien n'a été supprimé… », bouton **Associer à nouveau** → scanner.

### 6. Notifications Android
Uniquement :
- progression discrète (`setOnlyAlertOnce`, priorité basse) : « Sauvegarde en cours · 12 / 52 · PC-SALON » ;
- fin : « 52 photos et 3 vidéos sauvegardées sur PC-SALON » ;
- erreur actionnable : « Sauvegarde impossible : le disque du PC est plein. »

**Aucune notification permanente au repos** (utiliser un foreground service seulement pendant un transfert, via WorkManager).

---

## Interactions et comportement
- Dialogues de l'app : carte 28 dp, titre 22/800, corps 16, note optionnelle avec coche, actions empilées pleine largeur (primaire en premier).
- Les demandes système (autorisations, installation) utilisent les vraies boîtes de dialogue Android. Le prototype les imite seulement.
- Snackbar : fond = couleur texte, texte = fond, au-dessus de la barre nav, 3,8 s.
- Transitions : interrupteurs 150 ms, barres de progression 400–500 ms, poignée de défilement en fondu 300 ms.
- Sauvegarde : passer `pending → uploading (avec %) → saved`. Quand tout est envoyé : bandeau « Tout est sauvegardé · à l'instant » et notification de fin.

## État (cf. classe `Component`)
- `pairing` : non associé / associé (`pcName`, `albumName`) / dissocié par le PC.
- `permissions` : photos (complet / partiel / refusé), notifications.
- `folders` : map id → suivi (booléen).
- `media[]` : id, date, dossier, vidéo + durée, `state ∈ {saved, pending, uploading(pct), error(reason), untracked}`. `untracked` se déduit du dossier.
- `backupStatus` : ok · pending(n) · running(done/total) · pcUnreachable(depuis) · destDiskMissing · errors(n), avec `lastBackupAt`.
- `filter` : all · pending · error · untracked.
- `chargingOnly` : booléen.
- `updateAvailable` : version.

## Assets
- `assets/photo.jpg` : photo placeholder uniquement, à ne pas livrer.
- Icônes : Lucide.
- **Icône d'app / logo** : carré arrondi primary avec, en blanc, un écran de PC (contour 3,5 dp) qui contient une petite photo carrée pleine, sur un pied à deux barres. La même icône sert pour l'app Windows. Version ronde en masque adaptatif. À vectoriser en `adaptive-icon` (foreground blanc, background `#2F7D6B`).

## Fichiers
- `Mon Album PC v4.dc.html` : prototype complet (gabarit + logique). Ouvrir avec `support.js` et `_ds/` à côté.
- `support.js`, `_ds/…/styles.css`, `_ds/…/_ds_bundle.js` : moteur du prototype. Le fichier `styles.css` contient la feuille de base, dont la v4 remplace les couleurs, la police et les rayons dans son `<style>` d'en-tête. Les valeurs de ce README font foi.
- Pas encore dessinée : la page web mobile de téléchargement (§8 du brief).
