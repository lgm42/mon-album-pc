# Handoff : Mon Album PC — fenêtre principale (Windows, WPF Fluent)

> **Notes de reprise (2026-10-18)** : elles priment sur le reste du document.
> - **Accent** : on utilise l'indigo de marque, et non l'accent système ni `#005FB8`. Valeurs : `#3D4FD1`
>   en clair, `#9AA8FF` en sombre (voir [`../palette.md`](../palette.md)). Il faut le surcharger dans les
>   ressources Fluent WPF (`SystemAccentColor*`). La couleur de l'album « Marie » (`#005FB8`) passe donc à
>   une autre teinte, pour ne pas se confondre avec l'accent.
> - **Port** : `53317` est le port de LocalSend. Il faut en choisir un autre, qui sera fixé dans le
>   protocole.
> - **Assistant, étape 2** : afficher aussi le **code à 6 chiffres** sous le QR code, pour les téléphones dont la
>   caméra ne fonctionne pas (l'écran de saisie existe dans le prototype Android).
> - **Panneau Informations** : le chemin du dossier ne doit pas se couper au milieu (`2026-10` / `\`). Il faut
>   tronquer avec une ellipse et afficher le chemin complet en infobulle.
> - **Pas encore dessinés** : icônes systray et menu, import USB, premier lancement, notifications Windows,
>   thème sombre.

## Overview
Mon Album PC reçoit en Wi-Fi les photos et vidéos des téléphones Android de la famille et les range dans le dossier de chaque utilisateur (« album »). Elle sait aussi importer un téléphone branché en USB et propose une visionneuse rapide. L'app vit dans la zone de notification et se lance avec Windows.

Ce lot couvre la **fenêtre principale** : Photos (visionneuse et plein écran), Albums et téléphones, l'assistant « Ajouter un téléphone », Historique, Réglages et Diagnostic réseau.
**Hors de ce lot** (à concevoir ensuite) : les icônes systray dans leurs 4 états et le menu contextuel, l'import USB, le premier lancement, les notifications Windows et le thème sombre.

## About the Design Files
Le fichier HTML fourni est une **référence de conception** : un prototype qui montre l'apparence et le comportement attendus, pas du code de production. Il faut le **recréer en WPF (.NET) avec le thème Fluent intégré** (`ThemeMode`, .NET 9+), en utilisant les contrôles natifs : NavigationView ou équivalent, ContentDialog, InfoBar, ToggleSwitch, CheckBox, ComboBox, Slider, ProgressBar et ProgressRing, CommandBar, MenuFlyout, ToolTip. Gardez les ressources de thème système (`AccentFillColorDefaultBrush`, `CardBackgroundFillColorDefaultBrush`, etc.) plutôt que les valeurs hex codées en dur ci-dessous. Les hex ne servent qu'à décrire ce qui a été dessiné, en thème **clair**.

Pour ouvrir le prototype : `Mon Album PC - Fenêtre principale.dc.html` dans un navigateur, à côté de `support.js`, `image-slot.js` et `assets/`. Le panneau Tweaks (selon l'hôte) expose les états : `photosState` (normal / vide / indexation), `destinationOffline`, `syncState` (à jour / réception), `wizardOutcome` (succès / code expiré / injoignable / réseau bloqué) et `fillMode` (pour déposer de vraies images dans les miniatures).

## Fidelity
**Haute fidélité** pour la structure, les textes, les états et les espacements. Pour le rendu exact des contrôles, **le thème Fluent de WPF fait foi** : le prototype imite Fluent en HTML, il ne faut pas reproduire ses approximations pixel pour pixel.

Interface **en français uniquement**, avec vouvoiement. Les textes du prototype sont définitifs, sauf indication contraire.

## Fenêtre & shell
- Taille par défaut 1200×800, minimum **1024×640**, redimensionnable. Une seule instance par PC.
- Barre de titre de 32 px : icône 16 px et « Mon Album PC » en 12 px, boutons système de 46×32. Fond Mica (`SystemBackdropType = Mica`), sinon fond uni #EEF0F4 → `SolidBackgroundFillColorBase`.
- **Volet de navigation** (gauche, 272 px ouvert, 48 px compact). Il passe automatiquement en mode compact **sous 1280 px** de largeur ; le bouton hamburger force l'un ou l'autre mode.
  - Éléments du haut : **Photos** (par défaut), **Albums et téléphones**, **Historique**. En pied : **Réglages**.
  - Élément de 36 px, coins 4 px, icône 16 px, 16 px d'écart entre icône et libellé. L'élément sélectionné a un fond `SubtleFillColorSecondary` et un indicateur accent de 3×16 px à gauche.
  - **Carte d'état** au-dessus de Réglages (masquée en mode compact). Elle a trois variantes :
    - « Tout est à jour », avec « Dernière sauvegarde il y a 2 h » et une coche verte ;
    - « Réception en cours », avec « A35 de Marie · 12 / 52 » et une barre de progression de 3 px ;
    - « Sauvegardes en attente », avec « Le dossier de destination est indisponible » et une icône d'avertissement orange.
- **Zone de contenu** : calque `LayerFillColorDefault`, bordure de 1 px `CardStrokeColorDefault` en haut et à gauche, coin supérieur gauche arrondi à 8 px.
- **Bandeau « destination indisponible »** : InfoBar Warning en haut du contenu, sur toutes les pages. Texte : « **Dossier de destination indisponible.** D:\Photos est introuvable : le disque externe est peut-être débranché. Les téléphones attendent et réessaieront plus tard. » Boutons [Réessayer] et ✕. L'icône systray passe en état attention.

## Screens / Views

### 1. Photos (visionneuse)
Disposition en trois colonnes : filtres | grille | infos.
- **Volet de filtres** (216 px, 184 px sous 1360 px), bordure droite.
  - Section « Albums » : « Tous les albums » puis un élément par album (pastille de couleur de 8 px, nom, nombre d'éléments aligné à droite en chiffres tabulaires).
  - Section « Dates » : l'année (2026, en gras, avec chevron) puis les mois (Octobre, Septembre, Août…) avec leur nombre. Les compteurs dépendent de l'album choisi.
  - Les éléments font 32 px, avec le même indicateur de sélection que la navigation.
- **En-tête** : « Photos » (Segoe UI Variable Display, 28/36, semi-gras), suivi d'une légende grise comme « Marie · Octobre 2026 · 16 éléments ».
- **Barre de commandes** (CommandBar, 40 px, coins 8 px), qui s'applique à l'élément sélectionné :
  - pivoter à gauche, pivoter à droite (90°) | Supprimer, Ouvrir dans l'Explorateur, Partager ;
  - à droite : un Slider de taille des miniatures (96 à 260 px, pas de 4, 150 par défaut) et un bouton bascule « Infos ».
  - Sans sélection, les actions sont désactivées (opacité 0,4).
  - Quand la place manque (colonne centrale de moins de 700 px), les libellés disparaissent et il ne reste que les icônes, avec une infobulle. En WPF, utilisez le débordement natif de la CommandBar ou des libellés dynamiques.
- **Grille** : virtualisée, ce qui est impératif car elle doit être très rapide (`VirtualizingWrapPanel` ou équivalent). Elle est groupée par mois : titre 16/22 semi-gras et « 16 éléments » en 12 px gris.
  - Cellules carrées, colonnes `auto-fill` de largeur minimale égale au slider, écart de 4 px, coins 4 px.
  - Vidéo : badge en bas à droite (fond noir à 62 %, texte blanc 12 px, icône lecture et durée « 0:34 »).
  - Survol : voile noir à 6 %. Sélection : bordure accent de 2 px avec liseré blanc intérieur de 2 px, et bouton « Afficher en plein écran » de 28 px en haut à droite.
  - Clic : sélectionne. Double-clic ou Entrée : plein écran. Flèches gauche/droite : déplacent la sélection. Suppr : supprime.
- **Panneau Informations** (repliable, 288 px ou 248 px), avec icône 16 px, libellé 12 px gris et valeur pour chaque ligne :
  - Prise de vue : « 17 octobre 2026 à 13:17 » ;
  - Téléphone d'origine ;
  - Dimensions · taille : « 4000 × 3000 · 2,6 Mo » ;
  - Album · dossier : `D:\Photos\Marie\2026\2026-10\` ;
  - un emplacement en pointillés « Carte de la position GPS · v2 » (fonction prévue en v2).
  - Sans sélection : « Sélectionnez une photo pour voir sa date, son téléphone d'origine et ses dimensions. »
- **États** :
  - **Vide** : pas de filtres ni d'infos ; au centre, le logo 96 px, « Aucune photo pour l'instant. » (20/28 semi-gras), « Ajoutez un téléphone pour commencer. Ses photos et vidéos arriveront ici automatiquement, en Wi-Fi. » et un bouton accent [Ajouter un téléphone] qui ouvre l'assistant.
  - **Indexation** : InfoBar Informational sous le titre, avec une ProgressBar. Texte : « **Recensement des photos déjà présentes** dans D:\Photos\Marie, pour ne pas les reprendre en double : 1 240 sur 4 812. Vous pouvez continuer à utiliser l'app pendant ce temps. »
  - **Destination indisponible** : voir le bandeau du shell.
- **Supprimer** ouvre un ContentDialog :
  - titre « Supprimer cette photo ? » (ou « cette vidéo ») ;
  - texte « {nom du fichier} sera placé dans la Corbeille de Windows. Vous pourrez le restaurer depuis la Corbeille. » ;
  - boutons [Supprimer] (accent) et [Annuler].
  - Ensuite, un message « Placé dans la Corbeille de Windows. » avec un bouton [Annuler] qui restaure. Utilisez l'API de la Corbeille (`FileSystem.DeleteFile(..., RecycleOption.SendToRecycleBin)`).
- **Partager** passe par le partage Windows (`DataTransferManager`). **Ouvrir dans l'Explorateur** lance `explorer /select,"chemin"`.

### 2. Plein écran
- Recouvre toute la zone sous la barre de titre, sur un fond sombre #1C1C1C, même en thème clair.
- Barre du haut de 48 px :
  - à gauche : ← Retour (Échap), le nom du fichier, puis la date et « 3 sur 38 » en légende ;
  - à droite : les mêmes actions que la grille (icônes seules) | Infos.
- Média centré en mode « contain », avec 16/72/24 px de marge et une rotation animée en 250 ms.
- Boutons précédent/suivant de 40 px sur les côtés (opacité 0,3 aux extrémités). Clavier : ← →, Échap, Suppr.
- Vidéo : bouton lecture rond de 64 px au centre et barre de lecture en bas (lecture, temps, barre de progression, durée). En WPF : `MediaElement`.
- Panneau infos à droite de 288 px (fond #272727), avec les mêmes champs.

### 3. Albums et téléphones
- En-tête : « Albums et téléphones », puis [Nouvel album] (bouton standard) et [Ajouter un téléphone] (bouton accent).
- Grille de cartes : `minmax(min(100%,400px),1fr)`, écart de 16 px, contenu limité à 1200 px de large.
- **Carte d'album** (coins 8 px, fond `CardBackgroundFillColorDefault`, bordure `CardStrokeColorDefault`) :
  - En-tête : avatar rond de 40 px (initiale blanche sur la couleur de l'album), nom en 20/28 semi-gras, légende « 4 812 photos · 1 téléphone », puis deux boutons icône : Modifier et Supprimer.
  - **Supprimer** est désactivé (opacité 0,4) tant qu'il reste des téléphones. Son infobulle dit : « Impossible de supprimer un album qui a encore des téléphones. Dissociez-les d'abord. » Un album sans téléphone se supprime directement, avec le message « Album X supprimé. Le dossier et ses photos sont conservés. »
  - Ligne dossier : icône dossier, chemin en police mono 13 px (Cascadia Mono) tronqué avec une ellipse, et bouton [Ouvrir].
  - Sous-titre « Téléphones », puis une **ligne par téléphone** (56 px minimum). Colonnes : icône 32 px | nom en semi-gras + modèle en 12 px | « Dernière sauvegarde » + valeur | « Reçus » + nombre | menu « … ».
    - Au-delà de 7 jours sans sauvegarde, la valeur passe en **orange #9D5D00 semi-gras, avec une icône d'avertissement**.
    - Le menu « … » contient « Dissocier… ». Dialogue de confirmation : « Dissocier {nom} ? » / « Ce téléphone ne sera plus sauvegardé sur ce PC. Les photos déjà reçues restent dans le dossier de l'album. Pour le relier à nouveau, il faudra rescanner un QR code. »
  - Sans téléphone : « Aucun téléphone n'est relié à cet album. »
  - Lien en bas : « + Ajouter un téléphone à cet album », qui ouvre l'assistant avec cet album présélectionné.
- **Dialogue « Nouvel album » / « Modifier l'album »** (520 px) :
  - champ « Nom de l'album » ;
  - « Dossier de destination » : affichage en lecture seule + [Parcourir…] (`OpenFolderDialog`) ;
  - aperçu : « Les photos seront rangées ainsi : » `D:\Photos\Marie\2026\2026-10\` (rangement `{dossier}\{AAAA}\{AAAA-MM}\`) ;
  - en modification, si le dossier change : InfoBar Warning « Les photos déjà importées restent dans **{ancien}**. Seules les prochaines photos iront dans le nouveau dossier. » ;
  - boutons [Créer l'album] ou [Enregistrer] (désactivé tant que le nom ou le dossier manque) et [Annuler].

### 4. Assistant « Ajouter un téléphone » (ContentDialog, 640 px, contenu de 360 px de haut minimum)
Titre « Ajouter un téléphone » et « Étape n sur 3 » à droite.
1. **Pour quel album ?**
   - Consigne : « Pour quel album ? Les photos de ce téléphone seront rangées dans son dossier. »
   - Liste de choix de 56 px (bouton radio, avatar de 32 px, nom, « dossier · n téléphone(s) »). Le choix sélectionné a une bordure accent et un fond #F3F8FC.
   - Dernière option : « Créer un nouvel album ». Elle affiche en ligne un champ Nom et le dossier proposé `D:\Photos\{Nom}`.
   - [Suivant] reste désactivé tant qu'un nouvel album n'a pas de nom.
2. **Scannez ce QR code**
   - QR centré de 232 px (fond blanc, bordure, coins 8 px).
   - Dessous : « Code valable encore 4:52 · Générer un nouveau code ». Le code vaut 5 min.
   - Deux consignes côte à côte :
     - « **L'app est installée** — Ouvrez Mon Album PC sur le téléphone et scannez ce code. »
     - « **L'app n'est pas installée** — Scannez avec l'appareil photo du téléphone pour la télécharger. »
   - Le QR encode donc une URL qui ouvre l'app si elle est installée, ou sa page de téléchargement sinon.
3. **En attente**
   - Animation discrète : 3 points qui pulsent en 1,2 s, décalés de 0,2 s.
   - « Le téléphone a scanné le code » / « Connexion avec le téléphone… Gardez l'app ouverte sur le téléphone quelques secondes. »
4. **Réussi**
   - Coche verte de 56 px, puis « A35 de Marie est relié à l'album Marie. » et « La première sauvegarde va commencer. Vous pouvez fermer cette fenêtre. »
   - Bouton [Terminer], qui ouvre Albums et téléphones.

**Erreurs** : elles remplacent le contenu, avec une icône de 48 px, un titre semi-gras de 16 px et un texte. Le bouton principal varie :
- *Code expiré* (orange) : « Ce code a expiré » / « Pour votre sécurité, un code n'est valable que 5 minutes. Générez-en un nouveau et scannez-le. » → [Générer un nouveau code].
- *Injoignable* (rouge) : « Le téléphone ne répond pas » / « Vérifiez qu'il est allumé, connecté au même Wi-Fi que ce PC, et que l'app Mon Album PC est ouverte, puis réessayez. » → [Réessayer].
- *Réseau bloqué* (rouge, icône bouclier) : « Le réseau empêche le téléphone de joindre ce PC » / « Le téléphone a bien scanné le code, mais la connexion est bloquée. Le diagnostic réseau peut trouver la cause et la corriger. » → [Ouvrir le diagnostic réseau].

Dans le prototype, le scan est simulé : il se déclenche environ 7 s après l'affichage du code.

### 5. Historique
- Filtres sur une ligne :
  - ComboBox « Album » (Tous les albums, …) ;
  - ComboBox « Téléphone » (Tous les téléphones, …) ;
  - ToggleSwitch « Erreurs uniquement ».
- Les sessions sont groupées par jour, avec un titre de groupe en 14 px semi-gras : « Aujourd'hui · dimanche 18 octobre », « Hier · samedi 17 octobre », « Mercredi 14 octobre »…
- **Ligne de session** : grille `44px | 130–200px | 1fr | 72px`, rembourrage de 12/16 px, coins 4 px, bordure fine. Colonnes : heure | téléphone (semi-gras) + « Album X » | icône d'état + résumé (« 52 photos, 3 vidéos ») | étiquette de canal (icône + « Wi-Fi » ou « USB »).
- **Erreurs** : fond #FDE7E9 (`SystemFillColorCriticalBackground`), icône et titre rouges en semi-gras, puis le motif (« Le dossier D:\Photos\Marie était introuvable. ») et « Conseil : … » en 12 px gris. Si c'est utile, un lien « Ouvrir le diagnostic réseau ».
- Sans résultat : « Aucune session ne correspond à ces filtres. »

### 6. Réglages
Style de l'app Paramètres de Windows. Les titres de groupe font 14 px semi-gras. Chaque carte fait 68 px minimum, avec icône 20 px, titre, description en 12 px gris et contrôle à droite (`SettingsCard`).
- **Démarrage** — « Lancer avec Windows » / « L'app démarre réduite dans la zone de notification pour recevoir les photos. » ToggleSwitch activé par défaut (« Activé » / « Désactivé »).
- **Notifications** — « Me prévenir » / « Choisissez les messages que Windows affiche. » Trois cases (cochées par défaut) : Fin d'une sauvegarde, Erreurs, Téléphone branché en USB.
- **Réseau** — carte cliquable « Diagnostic réseau » / « Vérifier pourquoi un téléphone n'arrive pas à joindre ce PC. ». Si des erreurs existent, un badge rouge « 2 problèmes » s'affiche, avec un chevron.
- **Mises à jour**
  - Carte « Version 1.2.0 » / « Dernière vérification : aujourd'hui à 08:00 », avec un bouton [Rechercher une mise à jour]. Pendant la recherche : ProgressRing et « Recherche en cours… », puis « Vous utilisez la dernière version. »
  - Sous-carte « Installer les mises à jour automatiquement » avec un ToggleSwitch.
- **Application Android** — « Mon Album PC pour Android » / « Version fournie avec ce PC : 1.2.0 » + [Afficher le QR code de téléchargement]. Ce bouton ouvre un dialogue : QR de 180 px, « Installer l'app sur un téléphone », « Scannez ce code avec l'appareil photo du téléphone Android pour télécharger Mon Album PC (version 1.2.0). » et [Fermer].
- **À propos** — « Mon Album PC » / « Logiciel libre, sous licence MIT. » + lien « Code source sur GitHub ↗ ».

### 7. Diagnostic réseau (sous-page de Réglages)
- Fil d'Ariane en 28 px : « Réglages › Diagnostic réseau ». « Réglages » est en gris et cliquable.
- Introduction : « Ces vérifications expliquent pourquoi un téléphone n'arrive pas à envoyer ses photos à ce PC. Quand c'est possible, « Corriger » règle le problème pour vous (Windows peut demander une autorisation). »
- **InfoBar de synthèse**, selon le résultat :
  - erreurs : « N problème(s) empêche(nt) les téléphones de joindre ce PC. » ;
  - avertissements seulement : « Plus de blocage détecté. N point(s) méritent encore votre attention. » ;
  - tout va bien (succès) : « Tout est en ordre. Les téléphones peuvent joindre ce PC. »
  - Bouton [Relancer les vérifications].
- **Ligne de diagnostic** (72 px minimum) : icône d'état 20 px (OK vert #0F7B0F, Attention orange #9D5D00, Erreur rouge #C42B1C), titre semi-gras, explication d'une phrase, libellé d'état coloré, puis l'action.

| Vérification | État KO | Explication | Action | État OK |
|---|---|---|---|---|
| Profil réseau | Erreur — « Le réseau Windows est en mode « Public » » | Dans ce mode, Windows rend le PC invisible aux autres appareils : les téléphones ne peuvent pas le trouver. | [Corriger] (accent) → passage en Privé, avec élévation | « Le réseau « Livebox-4F2A » est en mode « Privé » » |
| Pare-feu | Erreur — « La règle du pare-feu est absente » | Le pare-feu de Windows bloque les photos envoyées par les téléphones. Mon Album PC a besoin d'une autorisation pour les recevoir. | [Corriger] → crée la règle entrante (port 53317), avec élévation | « Le pare-feu autorise Mon Album PC » |
| VPN | Attention — « Un VPN est actif » | Un VPN fait passer la connexion par un autre réseau : les téléphones de la maison risquent de ne plus joindre le PC. Désactivez-le pendant les sauvegardes. | [Ouvrir les paramètres VPN] (`ms-settings:network-vpn`) | « Aucun VPN actif » |
| Wi-Fi invité | Attention — « Le Wi-Fi semble être un réseau « invité » » | Les Wi-Fi invités empêchent les appareils de se voir entre eux. Connectez le PC et les téléphones au Wi-Fi principal de la box. | Aucune : texte « Pas de correction automatique » | — |
| Connexion | OK — « Le PC est connecté au réseau » | Wi-Fi « Livebox-4F2A », adresse 192.168.1.24. | — | — |

Pendant une correction : ProgressRing et « Correction… », puis la ligne passe à OK.

## Interactions & Behavior (résumé)
- Échap ferme le dialogue ouvert ou le plein écran. Les flyouts se ferment au clic extérieur.
- Les messages de confirmation (4 s, en bas au centre, coins 8 px, ombre) ne servent qu'au prototype, pour simuler les actions système. Dans l'app réelle, préférez un retour natif : InfoBar transitoire ou notification Windows.
- Transitions : bascule de la navigation (largeur, 200 ms), rotation (transform, 250 ms), ToggleSwitch (150 ms). Pour le reste, gardez les animations par défaut de Fluent.
- La sortie de veille avec un transfert interrompu reprend toute seule, sans message.

## State Management
- `currentPage` : Photos | Albums | Historique | Réglages | Diagnostic.
- `navPaneMode` : automatique selon la largeur, ou choix de l'utilisateur.
- Photos : `albumFilter`, `monthFilter`, `thumbnailSize`, `selectedItem`, `viewerIndex`, `infoPaneOpen`. La rotation s'écrit dans le fichier (EXIF ou réécriture) ; elle n'est pas seulement visuelle.
- Albums : liste des utilisateurs `{ id, nom, dossier, couleur, nbPhotos, téléphones[] }` ; téléphone `{ id, nom, modèle, dernièreSauvegarde, nbReçus }`.
- Assistant : `étape`, `albumChoisi | nouvelAlbum{nom}`, `codeExpireÀ`, `erreur` (expired | unreachable | blocked).
- Historique : sessions `{ horodatage, téléphone, album, nbPhotos, nbVidéos, canal (WiFi|USB), erreur?{motif, conseil, lienDiagnostic} }` et filtres.
- Réglages : `lancerAvecWindows`, `notif{finSauvegarde, erreurs, usb}`, `majAuto`.
- Diagnostic : `{ profil, pareFeu, vpn, wifiInvite, connexion } → ok|warn|err`.
- État global : `destinationIndisponible`, `réceptionEnCours{téléphone, n, total}`. Il alimente aussi l'icône systray.

## Design Tokens (thème clair, tels que dessinés ; à mapper sur les ressources Fluent WPF)
- **Accent** #005FB8 (survol #1A6CC0, pressé #3279C5, bord inférieur #003E92) → `AccentFillColor*`. Utilisez l'accent système de l'utilisateur.
- **Texte** primaire #1B1B1B, secondaire #5D5D5D, tertiaire #8A8A8A → `TextFillColorPrimary/Secondary/Tertiary`.
- **Fonds**
  - Mica #EEF0F4 ; calque de contenu blanc à 50 % ;
  - carte #FFFFFF, bordure rgba(0,0,0,0.0578) ;
  - bouton standard #FDFDFD, bordure #E5E5E5, bord inférieur #CCCCCC, survol #F6F6F6, pressé #F3F3F3 ;
  - survol discret rgba(0,0,0,0.0373), pressé rgba(0,0,0,0.0241) ;
  - pied de dialogue #F3F3F3.
- **États**
  - succès #0F7B0F sur #DFF6DD ;
  - attention #9D5D00 sur #FFF4CE ;
  - critique #C42B1C sur #FDE7E9 ;
  - info : InfoBar #F6F6F6 avec icône accent.
- **Couleurs d'album** : Marie #005FB8, Paul #8764B8, Jeanne #CA5010, Lucas #038387, nouveaux albums #C239B3.
- **Typographie**
  - Segoe UI Variable Text pour le corps : 14/20 ;
  - légendes 12/16 ;
  - sous-titre 20/28 semi-gras ;
  - titre de page Segoe UI Variable Display 28/36 semi-gras ;
  - titres de groupe 16/22 ou 14/20 semi-gras ;
  - chemins en Cascadia Mono 13 px.
- **Rayons** : contrôles 4 px, cartes, flyouts, dialogues et barre de commandes 8 px, avatars en cercle.
- **Ombres**
  - flyout et infobulle : 0 8 16 rgba(0,0,0,.14) ;
  - dialogue : 0 32 64 rgba(0,0,0,.19) et 0 2 21 rgba(0,0,0,.15) ;
  - en WPF, utilisez l'élévation native.
- **Espacements** : marges de page de 20/24 px, écart entre cartes de 16 px, de 4 px entre les cartes de réglages, de 4 px dans la grille de miniatures. Contrôles de 32 px, éléments de navigation de 36 px, lignes de 56 à 72 px.

## Assets
- `assets/logo.jpg` : icône de l'app (généré avec Gemini, identique à l'app Android), en 1024×1024 JPEG sans transparence. **Il faudra une version PNG/ICO détourée** aux tailles 16, 20, 24, 32, 48 et 256 pour la barre de titre, la barre des tâches et le systray.
- Icônes : **Fluent UI System Icons** (taille 20, regular ou filled), chargées en ligne dans le prototype depuis `cdn.jsdelivr.net/npm/@fluentui/svg-icons`. En WPF, utilisez **Segoe Fluent Icons** (police système) ou le paquet Fluent System Icons. Équivalents : image_multiple, phone, history, settings, arrow_counterclockwise, arrow_clockwise, delete, folder_open, share, info, full_screen_maximize, calendar, location, folder, edit, more_horizontal, plug_disconnected, add, qr_code, router, alert, window_new, arrow_sync, usb_stick, wifi_1, checkmark_circle, warning, error_circle, shield_error, phone_checkmark, arrow_download, open.
- Les miniatures du prototype sont des emplacements vides (`image-slot.js`, propre au prototype, à ne pas porter).
- Le QR code du prototype est factice : il faut le générer réellement (QRCoder, par exemple).

## Files
- `Mon Album PC - Fenêtre principale.dc.html` : le prototype. Le balisage et les textes sont dans le gabarit ; la logique, les données d'exemple (albums, photos, historique, diagnostic) et les transitions d'état sont dans la classe JS en bas du fichier.
- `support.js` : l'environnement d'exécution du prototype (à ne pas porter).
- `image-slot.js` : les emplacements d'image (à ne pas porter).
- `assets/logo.jpg` : le logo.
