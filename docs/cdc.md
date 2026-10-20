# Cahier des charges — Mon Album PC

Version 0.1 — 2026-10-17 — en cours de cadrage, aucun développement avant validation.

## 1. Contexte et objectif

Sauvegarder les photos d'un téléphone Android sur un PC Windows est pénible : l'explorateur rame et plante
en MTP, et les médias sont éparpillés (appareil photo dans un dossier, WhatsApp au fond de
`Android/media/…`). Nicolas sauvegarde régulièrement les téléphones de son entourage.

Le projet livre deux applications :

- **App Android** : choisit les dossiers à sauvegarder et envoie automatiquement les nouveautés au PC
  en Wi-Fi.
- **App Windows** : app systray qui reçoit les médias, les range dans le dossier de destination de
  chaque utilisateur, permet l'import USB et offre une visionneuse rapide.

Pas d'iOS. Pas de cloud : tout reste sur le réseau local.

## 2. Concepts

| Terme | Définition |
| --- | --- |
| **PC** | Un poste Windows avec l'app installée. |
| **Utilisateur** | Une personne déclarée sur le PC (« Marie », « Paul »). Pas de compte ni de mot de passe : juste un nom et **un dossier de destination choisi** par la personne. |
| **Téléphone** | Un appareil Android associé à **un** utilisateur d'**un** PC. Un utilisateur peut avoir plusieurs téléphones. |
| **Dossier suivi** | Un dossier du téléphone dont le contenu doit être sauvegardé. |
| **Historique d'import** | La base, côté PC, de tout ce qui a déjà été importé (par téléphone et par hash). C'est elle qui fait foi pour les doublons. |

Un téléphone n'est associé qu'à un seul PC.

## 3. Périmètre v1

**Inclus** : photos et vidéos ; transfert Wi-Fi automatique ; import USB (MTP) ; association par QR
code ; galerie Android avec état de sauvegarde ; app systray Windows avec réglages, historique et
visionneuse simple ; conversion HEIC → JPEG ; installeur Windows ; distribution de l'APK par le PC.

**Exclu** : iOS, Play Store, transfert par Internet sans VPN (voir §18.4), suppression de médias sur le
téléphone, mode « import ponctuel » sans association, synchronisation vers plusieurs PC, langues autres
que le français.

## 4. App Android

### 4.1 Dossiers suivis

- L'app liste les dossiers du téléphone qui contiennent des médias, avec une miniature et le nombre de fichiers.
  Cela couvre la mémoire interne **et la carte SD**.
- **Cochés par défaut** : appareil photo (quel que soit son emplacement), captures d'écran, WhatsApp
  Images, WhatsApp Video.
- **Proposés, non cochés** : sous-dossiers WhatsApp « Sent », statuts WhatsApp, Snapchat, autres apps
  détectées.
- **Parcourir** : l'utilisateur peut aller chercher n'importe quel dossier à la main.
- Pas de filtre de taille ni de type : tout ce qui est photo ou vidéo dans un dossier suivi est sauvegardé.
- Seuls les fichiers réellement présents sur le téléphone sont sauvegardés (pas ceux qui ne sont que
  dans le cloud).

### 4.2 Déclenchement

- L'app est réveillée par le système à l'arrivée d'un nouveau média ; elle n'a pas de service permanent.
- Contrainte : Wi-Fi. Option **« uniquement en charge »**, désactivée par défaut.
- Synchronisation périodique de secours et bouton **« Synchroniser maintenant »**.
- Le téléphone détecte le PC sur le réseau local, sans restriction sur le nom du Wi-Fi (donc sans
  permission de localisation).
- Pas d'envoi en 4G, sauf si l'option « Autoriser les données mobiles » est activée (VPN, voir §18.4).

### 4.3 Galerie

- Galerie des médias des dossiers suivis, avec un **badge d'état façon git** :
  sauvegardé ✓, en attente, en cours, erreur, dossier non suivi.
- Filtre par état (par exemple « en attente uniquement ») et compteur global.

### 4.4 Notifications

- Fin de synchronisation (« 52 photos sauvegardées sur PC-SALON ») et erreurs. Rien d'autre.

### 4.5 Gestion de l'association

- Écran avec le PC associé, l'utilisateur, la date de la dernière synchronisation.
- **Corbeille** pour dissocier le téléphone.

### 4.6 Suppression

- L'app ne supprime **jamais** rien, ni sur le téléphone ni sur le PC. Une photo supprimée du téléphone reste
  sur le PC.

### 4.7 Cible

- Android 10 minimum. Appareil de référence pour les tests : Samsung Galaxy A35.

## 5. App Windows

### 5.1 Forme

- App systray unique, lancée **au démarrage de Windows, réduite**.
- Un **installeur** (qui crée aussi la règle de pare-feu).
- Windows 10 et 11.
- **Une seule instance par PC**, qui gère tous les utilisateurs de l'app. Si une deuxième session Windows
  lance l'app, elle se ferme et signale que l'app tourne déjà ailleurs. Les utilisateurs de l'app ne
  sont pas des comptes Windows.

### 5.2 Menu systray

- État (en attente, transfert en cours depuis X, erreur).
- Ouvrir la fenêtre principale : utilisateurs et téléphones, réglages, historique, visionneuse.
- Ajouter un téléphone.

### 5.3 Utilisateurs et téléphones

- Créer, renommer et supprimer un utilisateur ; choisir son dossier de destination (disque local, disque
  externe, NAS ou lecteur réseau).
- Liste des téléphones de chaque utilisateur, avec la date de dernière synchronisation et un bouton
  « dissocier ».
- Un utilisateur ne peut pas être supprimé tant que des téléphones lui sont associés.
- Un téléphone ne peut pas être déplacé vers un autre utilisateur : il faut le dissocier, puis
  l'associer à nouveau.
- Si on change le dossier de destination, les imports déjà faits restent où ils sont ; seuls les
  nouveaux vont dans le nouveau dossier.

### 5.3 bis Notifications Windows

- Fin de synchronisation, erreurs, et téléphone branché en USB (avec le bouton Importer).

### 5.4 Historique et journaux

- Journal consultable : « 14/10 18:32 — 52 photos, 3 vidéos depuis A35 de Marie (Wi-Fi) », ainsi que
  les erreurs.

### 5.5 Veille et disponibilité

- Si le PC passe en veille, les transferts sont mis en pause puis reprennent au réveil.
- Si la destination est indisponible (disque externe débranché, NAS éteint), l'app refuse les fichiers avec le
  motif « destination indisponible ». Le téléphone les garde en attente et réessaie plus tard. L'app
  signale le problème dans le systray.

### 5.6 Diagnostic réseau

- L'app détecte les cas bloquants et propose une correction : profil réseau Windows « Public », pare-feu,
  VPN actif, Wi-Fi invité qui isole les appareils.

## 6. Association et sécurité

### 6.1 Parcours (objectif : 2 secondes quand l'app est déjà installée)

1. Sur le PC : **Ajouter un téléphone**, puis choisir ou créer l'utilisateur. Un **QR code** s'affiche.
2. Le QR code contient l'URL du PC (IP:port), un **jeton d'association à usage unique** (avec une durée
   de validité courte) et l'**empreinte du certificat** du PC.
3. **Si l'app est déjà installée**, l'utilisateur scanne le QR code depuis l'app. Elle contacte le PC,
   vérifie le certificat et l'association est faite.
4. **Si l'app n'est pas installée**, l'utilisateur scanne avec l'appareil photo. Une page web servie par
   le PC s'ouvre, avec un bouton pour télécharger l'APK. Après installation, il scanne à nouveau le même
   QR code depuis l'app (ou ouvre l'app avec un lien qui transmet le jeton).
   Si la caméra ne fonctionne pas, le PC affiche aussi un **code à 6 chiffres** à saisir dans l'app.
   Le QR code et le code sont valables **5 minutes**.
5. Le premier écran de l'app propose les dossiers suivis (§4.1), puis la première synchronisation démarre.

### 6.2 Sécurité

- Les transferts sont chiffrés en TLS, avec un certificat auto-signé du PC **épinglé** lors de
  l'association. Le surcoût est négligeable : les téléphones récents chiffrent en matériel, et c'est le
  débit Wi-Fi qui limite.
- Chaque téléphone reçoit à l'association un identifiant et un secret qui l'authentifient ensuite.
- Le PC peut révoquer un téléphone, et le téléphone peut se dissocier (corbeille).
- Le PC est retrouvé sur le réseau par mDNS, ce qui tolère un changement d'IP.

## 7. Transfert Wi-Fi

- Le téléphone envoie les fichiers au PC, qui les reçoit.
- Les transferts sont **reprenables** : une vidéo de 4 Go coupée à 80 % reprend à 80 %.
- Le hash de bout en bout (SHA-256) est vérifié avant de marquer un fichier comme sauvegardé.
- Au moment de se synchroniser, le téléphone envoie la liste des hash en attente. Le PC répond avec ceux
  qu'il possède déjà, ce qui couvre les médias importés en USB entre-temps et met à jour les badges de la
  galerie.

## 8. Import USB

- Le téléphone doit être en mode **« Transfert de fichiers »** (MTP) et déverrouillé. L'app Windows
  guide l'utilisateur si ce n'est pas le cas.
- **Manifeste** : l'app Android tient à jour un fichier dans `Android/media/<package>/`, lisible par
  MTP. Il contient l'identité du téléphone et la liste des médias suivis (chemin, taille, date, hash).
- Au branchement d'un téléphone associé, l'app Windows affiche une notification
  « A35 de Marie branché — 340 nouveautés », avec un bouton **Importer**.
- Le PC lit le manifeste, télécharge uniquement les fichiers absents de l'historique et vérifie leur hash.
  Il écrit ensuite un accusé de réception sur le téléphone (par MTP) pour que l'app mette ses badges à jour.
- Si le manifeste n'est pas à jour, le PC compare chemin + taille + date avec son historique, sans
  télécharger pour vérifier.
- Un téléphone **non associé** branché en USB déclenche une proposition d'association (§6.1).
- **Priorité à l'USB** : si un transfert Wi-Fi est en cours, il s'interrompt proprement et l'USB prend le
  relais. Un verrou par téléphone empêche deux imports simultanés.
- Si possible, l'app s'enregistre comme gestionnaire d'**exécution automatique** Windows pour les
  téléphones, à la place du dialogue d'import de Windows.

## 9. Dédoublonnage

- **(a)** Un fichier déjà importé depuis le même téléphone n'est jamais repris. On le reconnaît par son
  **hash de contenu**, donc même s'il a été déplacé ou renommé sur le téléphone.
- **(c)** Au premier lancement, et à chaque ajout d'utilisateur, l'app **indexe le dossier de destination**
  en tâche de fond. Une photo déjà présente (import manuel fait avant) n'est pas reprise : soit même
  hash, soit **même nom de fichier et même date de prise de vue**. Pour les HEIC convertis par l'app,
  l'historique garde le hash de l'original.
- Ce n'est **pas** la présence du fichier qui fait foi : un fichier supprimé ou déplacé côté PC n'est pas
  ré-importé.
- Le dédoublonnage entre téléphones différents (b) n'est pas fait en v1. La détection de doublons dans
  plusieurs dossiers est prévue plus tard (§13).

## 10. Rangement et nommage

- L'utilisateur choisit la **racine** (son dossier de destination). L'app impose l'arborescence en
  dessous : `<Destination>\AAAA\AAAA-MM\fichier`. Pas de sous-dossier par téléphone.
- **Date de classement** : date de prise de vue EXIF, sinon date contenue dans le nom de fichier
  (`IMG-20240512-WA0003.jpg`), sinon date du fichier.
- Les **noms d'origine sont conservés**. En cas de collision de noms avec un contenu différent, on ajoute
  le suffixe `_1`, `_2`…
- La date de modification d'origine est conservée sur les fichiers copiés.

## 11. Formats

- Photos : JPEG, PNG, **HEIC/HEIF converti en JPEG** (qualité 92, l'original HEIC n'est pas conservé),
  RAW/DNG tels quels, Motion Photos
  telles quelles (vidéo intégrée conservée).
- Vidéos : copiées telles quelles, sans conversion.
- La conversion est faite **sur le PC**, puisque l'import USB récupère les fichiers bruts. Les métadonnées
  EXIF (date, GPS, orientation) sont conservées.

## 12. Visionneuse Windows (v1)

- Grille de miniatures rapide, plein écran, lecture vidéo.
- Navigation par utilisateur, par téléphone, par date (année / mois).
- Actions : rotation 90° dans un sens ou dans l'autre (sans perte, via l'orientation EXIF), suppression
  (corbeille Windows), ouvrir dans l'Explorateur, partager.
- La lecture vidéo est embarquée et ne dépend pas des codecs installés sur Windows : les vidéos HEVC des
  téléphones récents se lisent sans l'extension payante de Microsoft.

## 13. Plus tard (ne pas fermer la porte dans l'architecture)

- Filtre par carte GPS.
- Détection de doublons dans plusieurs dossiers.
- Recherche, reconnaissance de visages : non demandés à ce stade.

## 14. Distribution et mises à jour

- **Windows** : installeur. Mise à jour automatique depuis les releases GitHub. Le repo est **public**,
  donc rien de sensible dedans : ni secrets, ni clés de signature, ni données personnelles.
- **Android** : pas de Play Store. L'APK est **embarqué dans l'app Windows** et servi par son serveur
  web. Une page de téléchargement est accessible par QR code.
- Mises à jour Android : à chaque synchronisation, l'app compare sa version à celle de l'APK du PC.
  Si l'APK est plus récent, elle notifie l'utilisateur et lance l'installeur Android (après accord de
  l'utilisateur).
- L'APK est signé avec une clé du projet. Android refuse une mise à jour signée par une autre clé, ce qui
  protège les mises à jour.

## 15. Choix techniques (validés le 2026-10-17)

Objectif : le plus simple et le plus maintenable possible, et compilable sans installer 250 outils.

- **Windows** : C# / .NET (LTS), WPF, publication autonome en un seul exécutable. Installeur et mises à jour
  par Velopack (un outil `dotnet`, rien d'autre à installer). Conversion HEIC et miniatures par
  Magick.NET (package NuGet) ; lecture vidéo par LibVLCSharp (package NuGet).
- **Android** : identifiant d'application `io.github.lgm42.monalbumpc` (définitif). Kotlin + Jetpack Compose, WorkManager (déclenchement sur nouveau média), MediaStore et
  Storage Access Framework (dossier choisi à la main), CameraX + ML Kit pour le scanner de QR code.
- **Protocole** : HTTPS + JSON, documenté dans le repo. Aucun code partagé n'est nécessaire entre les
  deux apps.
- **Compilation** : GitHub Actions compile l'APK et l'installeur Windows à chaque tag. Une release
  contient les deux. En local, il suffit du SDK .NET pour Windows ; pour Android, Android Studio ou
  les outils en ligne de commande, seulement si on veut compiler sur le PC.

## 16. Questions ouvertes

- Aucune pour l'instant.

Nom retenu le 2026-10-17 : **Mon Album PC**, même nom pour les deux apps (Android et Windows).

## 17. Conventions du repo

- Commits unitaires, titre court en anglais, préfixé d'un gitmoji. Aucune mention d'IA dans les messages.
- La clé de signature de l'APK et les secrets de compilation sont dans les secrets GitHub Actions, jamais
  dans le repo.

## 18. Version NAS (TrueNAS SCALE) — ajoutée le 2026-10-20

Une troisième application, **serveur seul**, pour remplacer le PC par un NAS toujours allumé. Le téléphone
reste associé à **un seul serveur** : soit un PC Windows, soit le NAS. Pour lui, le NAS n'est qu'un « PC »
de plus, avec le même protocole.

### 18.1 Forme

- Conteneur Docker publié par la CI (`ghcr.io/lgm42/mon-album-pc`), installé sur **TrueNAS SCALE 24.10+**
  en Custom App (« Install via YAML », docker compose). Un exemple de YAML est fourni dans `nas/`.
- **Réseau de l'hôte** (`network_mode: host`) : indispensable pour l'annonce mDNS sur le réseau local.
- Volumes :
  - `/config` : catalogue, certificat et identité du serveur ;
  - `/photos` : dataset parent des albums. Les dossiers des albums sont choisis **sous `/photos`**.
- Utilisateur du conteneur : `568:568` par défaut (utilisateur `apps` de TrueNAS), modifiable. Le dataset
  doit lui donner le droit d'écrire.
- L'APK Android est embarqué dans l'image, comme dans l'app Windows.

### 18.2 Interface web d'administration

- Même périmètre que l'app Windows, visionneuse comprise : albums et téléphones, ajout d'un téléphone
  (QR code + code à 6 chiffres), historique, réglages, diagnostic, visionneuse (grille par mois, plein écran,
  rotation sans perte, suppression).
- Même style Fluent et même accent indigo, en français.
- Port **48622**, en HTTPS avec le certificat du serveur.
- **Protégée par un mot de passe administrateur**, demandé au premier accès (ou fourni par la variable
  `MONALBUMPC_ADMIN_PASSWORD`).
- La suppression d'une photo depuis le web la déplace dans `/photos/.corbeille` (pas de Corbeille Windows).

### 18.3 Différences avec l'app Windows

- Pas d'icône de zone de notification, pas de lancement avec Windows (c'est le redémarrage du conteneur qui
  s'en charge), pas d'import USB.
- Pas de notification système : l'état et l'historique se consultent dans l'interface web.

### 18.4 Réseau, VPN et données mobiles

- Le téléphone sauvegarde dès que le serveur répond, y compris à travers un VPN (WireGuard vers la maison).
- La découverte mDNS ne traverse pas un VPN : le téléphone utilise l'adresse mémorisée à l'association.
- Nouvelle option Android **« Autoriser les données mobiles »**, désactivée par défaut. Sans elle, la
  sauvegarde reste en Wi-Fi uniquement ; avec elle, elle part aussi en 4G/5G si le serveur est joignable
  (typiquement via le VPN).
