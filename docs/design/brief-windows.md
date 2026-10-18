# Brief design — Mon Album PC, app Windows

Brief destiné à Claude Design. Référence fonctionnelle complète : [`../cdc.md`](../cdc.md).

## 1. Le produit en une phrase

**Mon Album PC** reçoit automatiquement, en Wi-Fi, les photos et vidéos des téléphones Android de la
famille, et les range dans le dossier de chacun. Elle sait aussi importer un téléphone branché en USB,
et propose une visionneuse rapide.

## 2. Pour qui

- **L'installateur** est une personne de l'entourage qui s'y connaît. Il installe l'app, crée les
  utilisateurs, choisit les dossiers et associe les téléphones.
- **Les membres de la famille** ne sont pas informaticiens. Ils ouvrent l'app pour regarder leurs photos ou vérifier
  que leur téléphone est bien sauvegardé.
- L'app vit dans la **zone de notification** (systray) et se lance avec Windows, fenêtre réduite. On ne
  l'ouvre que de temps en temps.

## 3. Ton et identité

- Sérieux, rassurant, clair. Pas ludique, pas technique.
- Interface **en français uniquement**, vouvoiement.
- Vocabulaire du quotidien. Les termes techniques (port, pare-feu, profil réseau) n'apparaissent que dans
  l'écran de diagnostic, et toujours avec une explication et un bouton « Corriger ».
- Même icône et même logo que l'app Android, générés avec Gemini (voir
  [`prompt-logo-gemini.md`](prompt-logo-gemini.md)).

## 4. Contraintes de plateforme

- Windows 10 et 11. Style **Fluent / Windows 11** : l'app est faite en WPF avec le thème Fluent intégré
  à .NET. Coins arrondis, typographie Segoe UI Variable, effet Mica si disponible, sinon fond uni.
- Thème clair et sombre, qui suit le réglage Windows.
- Fenêtre redimensionnable. Taille par défaut d'environ 1200×800, et utilisable jusqu'à 1024×640.
- Une seule instance par PC.

## 5. Icône de la zone de notification et menu

**Icône avec quatre états** :
- normal (au repos) ;
- transfert en cours (animée ou avec une pastille) ;
- attention (destination indisponible, problème réseau) ;
- erreur.

**Menu contextuel (clic droit)** :
- une ligne d'état non cliquable, par exemple « Réception depuis A35 de Marie… 12 / 52 » ou
  « Tout est à jour » ;
- Ouvrir Mon Album PC ;
- Ajouter un téléphone… ;
- Quitter (avec une confirmation qui explique que les sauvegardes s'arrêteront).

**Clic gauche** : ouvre la fenêtre principale.

## 6. Fenêtre principale

Navigation latérale (NavigationView Fluent) :

1. **Photos** : la visionneuse (écran par défaut).
2. **Albums et téléphones** : les utilisateurs et leurs téléphones.
3. **Historique**
4. **Réglages**

### 6.1 Photos (visionneuse)

- Volet de gauche pour filtrer : **Album** (utilisateur), puis **Année / Mois**, avec le nombre
  d'éléments.
- Grille de miniatures **très rapide**, groupée par mois, avec un curseur de taille des miniatures. Les
  vidéos montrent leur durée.
- **Vue plein écran** : photo ou lecteur vidéo, flèches et clavier pour passer d'un média à l'autre.
- Barre d'actions, en grille comme en plein écran :
  - rotation à gauche et à droite (90°) ;
  - supprimer (vers la corbeille Windows) ;
  - ouvrir dans l'Explorateur ;
  - partager.
- Panneau d'informations repliable : date de prise de vue, téléphone d'origine, taille, dimensions.
  Prévoir une petite **carte de la position GPS**, en v2.
- États :
  - vide : « Aucune photo pour l'instant. Ajoutez un téléphone pour commencer. » ;
  - indexation en cours ;
  - dossier de destination indisponible.

### 6.2 Albums et téléphones

- Une **carte par utilisateur** : nom, dossier de destination (chemin + bouton « Ouvrir »), nombre de
  photos, et la liste de ses téléphones.
- Une **ligne par téléphone** : nom (« A35 de Marie »), modèle, dernière sauvegarde (« il y a 2 h »,
  en orange au-delà de 7 jours), nombre d'éléments reçus, et un menu (Dissocier).
- Boutons **« Nouvel album »** et **« Ajouter un téléphone »**.
- **Créer ou modifier un album** :
  - nom ;
  - dossier de destination (sélecteur de dossier) ;
  - aperçu du rangement : « Les photos seront rangées ainsi : `D:\Photos\Marie\2026\2026-10\` ».

  Si on change le dossier d'un album existant, un avertissement précise que les photos déjà importées
  restent où elles sont.
- **Supprimer un album** est impossible tant qu'il a des téléphones : le bouton est désactivé et une
  infobulle explique pourquoi.

### 6.3 Ajouter un téléphone (assistant)

1. **Pour quel album ?** Choisir un album existant ou en créer un.
2. **Scannez ce QR code** : grand QR code au centre, avec deux consignes côte à côte :
   - « L'app est installée : ouvrez Mon Album PC sur le téléphone et scannez. »
   - « L'app n'est pas installée : scannez avec l'appareil photo du téléphone pour la télécharger. »

   Un compte à rebours de validité du code est affiché, avec un lien « Générer un nouveau code ».
3. **En attente** du téléphone (animation discrète).
4. **Réussi** : « A35 de Marie est relié à l'album Marie. La première sauvegarde va commencer. »

Erreurs à dessiner :
- code expiré ;
- téléphone injoignable ;
- **réseau qui bloque** les téléphones, avec un lien vers le diagnostic (§6.6).

### 6.4 Historique

- Journal chronologique, avec une ligne par session : « 14/10 18:32 — A35 de Marie — 52 photos,
  3 vidéos — Wi-Fi » (ou « USB »).
- Les erreurs sont mises en évidence, avec leur motif et un éventuel conseil.
- Filtres par album, par téléphone et « Erreurs uniquement ».

### 6.5 Réglages

- Lancer avec Windows : interrupteur, activé par défaut.
- Notifications : fin de sauvegarde, erreurs, téléphone branché en USB (une case par type).
- **Diagnostic réseau** : un bouton qui ouvre l'écran du §6.6.
- Mises à jour : version actuelle, « Rechercher une mise à jour », mise à jour automatique.
- Application Android : version embarquée, bouton « Afficher le QR code de téléchargement ».
- À propos : licence MIT, lien GitHub.

### 6.6 Diagnostic réseau

Une liste de vérifications, chacune marquée OK, attention ou erreur, avec une explication en une phrase
et un bouton **« Corriger »** quand c'est possible :
- le réseau Windows est en mode « Public » : les téléphones ne peuvent pas trouver le PC ;
- règle du pare-feu absente ;
- VPN actif ;
- Wi-Fi « invité » qui isole les appareils (pas de correction possible : l'expliquer simplement).

## 7. Import USB

1. **Notification Windows** au branchement d'un téléphone associé : « A35 de Marie est branché —
   340 nouvelles photos et vidéos », avec un bouton **Importer**.
2. Si le téléphone est en « recharge uniquement » ou verrouillé, la notification ou une petite fenêtre
   guide l'utilisateur : « Déverrouillez le téléphone, touchez la notification USB et choisissez
   *Transfert de fichiers* ». Une illustration serait idéale.
3. **Fenêtre de progression de l'import** : compteur, barre de progression, vitesse, temps restant,
   bouton Annuler. Elle est compacte, et l'import continue si on la ferme.
4. **Fin** : « 340 éléments importés dans l'album Marie », avec un bouton « Voir les photos ».
5. **Téléphone non associé** : « Un téléphone inconnu est branché. L'associer à un album ? » ouvre
   l'assistant du §6.3.

## 8. Premier lancement

1. Bienvenue, avec une phrase sur ce que fait l'app.
2. Créer le premier album : nom + dossier de destination.
3. **Indexation** : si le dossier contient déjà des photos, l'app les recense pour ne pas les reprendre en
   double. Afficher une progression, et préciser qu'on peut continuer pendant ce temps.
4. Ajouter un téléphone (assistant §6.3), ou « Plus tard ».

## 9. Autres états et messages

- L'app est déjà ouverte dans une autre session Windows : petit dialogue, puis fermeture.
- Destination indisponible (disque externe débranché, NAS éteint) : bandeau d'avertissement dans la
  fenêtre et icône systray en attention. Les téléphones attendent et réessaient plus tard.
- PC qui sort de veille avec un transfert interrompu : il reprend tout seul, sans message.

## 10. Livrables attendus

- Les maquettes des écrans du §5 au §9, en clair et en sombre.
- Les icônes systray (4 états) en 16, 20, 24 et 32 px.
- Les composants : carte d'album, ligne de téléphone, ligne d'historique, ligne de diagnostic.
- Les gabarits de notification Windows (fin de sauvegarde, erreur, téléphone branché).
