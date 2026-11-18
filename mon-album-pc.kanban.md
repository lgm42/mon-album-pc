<!-- This is a Kanban Board file created with MD Kanban extension -->
<!-- GitHub: https://github.com/jebakumarj/md-kanban -->
<!-- VS Code Extension: Search "MD Kanban" in the extension store (ID: jeddak.md-kanban) -->

# Mon Album PC

## Backlog

#### Installeur Windows construit par GitHub Actions
<!-- id: task-1794400000000-1 -->
Installeur et mises à jour (Velopack), embarquant l'APK Android. Construit et publié par la CI.
Tags: `windows` `ci`
<!-- workload: hard -->
<!-- assignee: Claude -->

#### Proposer l'app au catalogue officiel truenas/apps
<!-- id: task-1794400000000-2 -->
Pull request vers le train community de github.com/truenas/apps, une fois l'app stable. Demande des versions numérotées de l'image plutôt que le tag develop.
Tags: `nas`
<!-- priority: low -->
<!-- workload: hard -->
<!-- assignee: Claude -->

#### Import USB (MTP / WPD) avec manifeste et accusés
<!-- id: task-1794400000000-3 -->
Le téléphone en mode partage de fichiers, sans doublon avec la sauvegarde Wi-Fi.
- [ ] Windows : lecture MTP / WPD et import
- [ ] Android : manifeste USB et lecture des accusés
Tags: `windows` `android`
<!-- priority: low -->
<!-- workload: extreme -->
<!-- assignee: Claude -->

#### Diagnostic réseau et règles pare-feu
<!-- id: task-1794400000000-4 -->
Tags: `windows`
<!-- priority: low -->
<!-- assignee: Claude -->

#### Ajouter un dossier à sauvegarder (sélecteur SAF)
<!-- id: task-1794400000000-5 -->
Tags: `android`
<!-- assignee: Claude -->

#### Écran « Téléphone dissocié »
<!-- id: task-1794400000000-6 -->
Ce que voit le téléphone quand le serveur l'a dissocié.
Tags: `android`
<!-- workload: easy -->
<!-- assignee: Claude -->

#### Navigation compacte sous 1280 px
<!-- id: task-1794400000000-7 -->
Tags: `windows`
<!-- priority: low -->
<!-- assignee: Claude -->

#### Partager une photo avec Windows
<!-- id: task-1794400000000-8 -->
Tags: `windows`
<!-- priority: low -->
<!-- workload: easy -->
<!-- assignee: Claude -->

#### Lire les vidéos HEVC
<!-- id: task-1794400000000-9 -->
Tags: `windows`
<!-- assignee: Claude -->

#### Annuler une suppression
<!-- id: task-1794400000000-10 -->
Remet la photo en place depuis la corbeille de l'album ou la Corbeille Windows. Remplace la corbeille de la visionneuse web du NAS, retirée.
Tags: `windows`
<!-- assignee: Claude -->

#### Vérifier le thème sombre
<!-- id: task-1794400000000-11 -->
Tags: `windows`
<!-- priority: low -->
<!-- workload: easy -->
<!-- assignee: Claude -->

#### Barre d'outils Photos : boutons sous le curseur à 1200 px
<!-- id: task-1794400000000-77 -->
Explorateur et Plein écran passent sous le curseur de taille.
Tags: `windows`
<!-- priority: low -->
<!-- workload: easy -->
<!-- assignee: Claude -->

#### Vignettes web périmées après une rotation
<!-- id: task-1794400000000-78 -->
La rotation garde la date du fichier : le navigateur garde l'ancienne vignette jusqu'à 24 h.
Tags: `nas`
<!-- priority: low -->
<!-- workload: easy -->
<!-- assignee: Claude -->

#### Réserver l'adresse IP du PC dans la box
<!-- id: task-1794400000000-79 -->
Le PC a changé d'adresse après l'association du téléphone.
<!-- priority: low -->
<!-- workload: easy -->
<!-- assignee: Nicolas -->

## TODO

#### Mettre la clé de release Android en lieu sûr
<!-- id: task-1794400000000-20 -->
Sans cette clé, plus aucune mise à jour de l'app ne pourra s'installer sur les téléphones. Fichiers dans Documents\MonAlbumPC.
- [ ] Copier release.keystore en lieu sûr (clé USB, et pas seulement le NAS)
- [ ] Ranger son mot de passe dans Vaultwarden
- [ ] Ensuite seulement, supprimer les deux fichiers .txt des secrets
Tags: `android`
<!-- priority: high -->
<!-- workload: easy -->
<!-- assignee: Nicolas -->

#### Réglages Xiaomi du téléphone
<!-- id: task-1794400000000-21 -->
Pour que la sauvegarde reparte seule, sans ouvrir l'app.
- [ ] Réglages de l'app › Démarrage automatique activé
- [ ] Économiseur de batterie › Pas de restrictions
- [ ] Dans l'app, carte « La sauvegarde automatique est bridée » › Autoriser
Tags: `android`
<!-- workload: easy -->
<!-- assignee: Nicolas -->

#### Vérifier le déploiement NAS
<!-- id: task-1794400000000-22 -->
Après l'installation : journaux du conteneur, droits des fichiers écrits, reconnaissance des photos sans renvoi, notification reçue par la visionneuse.
Tags: `nas`
<!-- priority: high -->
<!-- assignee: Claude -->

### Ménage après les tests

#### Dissocier le faux téléphone « A35 de test »
<!-- id: task-1794400000000-24 -->
App Windows › Albums et téléphones.
Tags: `windows`
<!-- priority: low -->
<!-- workload: easy -->
<!-- assignee: Nicolas -->

#### Supprimer le dossier Pictures\Test Claude
<!-- id: task-1794400000000-25 -->
S'il existe encore (vide).
Tags: `windows`
<!-- priority: low -->
<!-- workload: easy -->
<!-- assignee: Nicolas -->

#### Garder ou supprimer les outils Android de Claude
<!-- id: task-1794400000000-26 -->
Gradle et SDK Android (quelques Go) dans le dossier temporaire de session de Claude. Les garder permet à Claude de vérifier le code Android avant de pousser.
<!-- priority: low -->
<!-- workload: easy -->
<!-- assignee: Nicolas -->

## DOING

#### Provenance des photos (WhatsApp, captures)
<!-- id: task-1794400000000-75 -->
Mot-clé XMP sans réencodage, filtre « Provenance » dans les visionneuses, marquage des fichiers existants depuis l'administration (sur confirmation).
Tags: `nas` `windows`
<!-- priority: high -->
<!-- workload: hard -->
<!-- assignee: Claude -->

#### Carte dans l'administration web du NAS
<!-- id: task-1794400000000-76 -->
Même carte que sous Windows (Leaflet, OpenStreetMap).
Tags: `nas`
<!-- workload: hard -->
<!-- assignee: Claude -->

### Déploiement TrueNAS

#### 5. Passer l'app Windows en Visionneuse
<!-- id: task-1794400000000-34 -->
Réglages › Bibliothèque › Utiliser une bibliothèque sur le NAS… Choisir le chemin réseau complet du dossier Telephones sur le partage donnees, pas le lecteur Y:.
Tags: `windows`
<!-- priority: high -->
<!-- workload: easy -->
<!-- assignee: Nicolas -->

#### 6. Tester la notification
<!-- id: task-1794400000000-35 -->
Prendre une photo : moins de 3 minutes plus tard, Windows annonce que le téléphone est entièrement synchronisé.
Tags: `windows`
<!-- priority: high -->
<!-- workload: easy -->
<!-- assignee: Nicolas -->

#### 7. Redémarrer Backrest
<!-- id: task-1794400000000-36 -->
Pour qu'il sauvegarde le nouveau dataset : il contient le certificat mémorisé par les téléphones.
Tags: `nas`
<!-- workload: easy -->
<!-- assignee: Nicolas -->

#### 8. Données mobiles via VPN
<!-- id: task-1794400000000-37 -->
Facultatif : dans l'app Android, activer « Autoriser les données mobiles » pour sauvegarder en 4G/5G à travers WireGuard.
Tags: `android`
<!-- priority: low -->
<!-- workload: easy -->
<!-- assignee: Nicolas -->

## DONE

#### 3. Administration web et album
<!-- id: task-1794400000000-32 -->
- [x] Ouvrir https://adresse-du-nas:48622 et accepter le certificat auto-signé
- [x] Choisir le mot de passe administrateur (le ranger dans Vaultwarden)
- [x] Créer l'album Nicolas sur le dossier existant Nico
- [x] Depuis le PC, ouvrir Telephones\.monalbumpc\library.json sur le partage donnees
Tags: `nas`
<!-- priority: high -->
<!-- workload: easy -->
<!-- assignee: Nicolas -->

#### 4. Basculer le téléphone vers le NAS
<!-- id: task-1794400000000-33 -->
Seulement quand la sauvegarde du téléphone est terminée. Rien ne doit être renvoyé : le NAS lit le journal du PC. S'il envoie des milliers de photos, arrêter et prévenir Claude.
- [x] App Android › Réglages › corbeille : dissocier le PC
- [x] Administration du NAS › Ajouter un téléphone, scanner le QR code
- [x] Le téléphone se dit à jour
Tags: `nas` `android`
<!-- priority: high -->
<!-- workload: easy -->
<!-- assignee: Nicolas -->

#### Miniatures générées par le NAS dans la bibliothèque
<!-- id: task-1794400000000-23 -->
Service de fond du NAS (30 s après le démarrage, toutes les heures, après chaque sauvegarde), priorité minimale ; la visionneuse Windows lit les miniatures.
Tags: `nas` `windows`
<!-- workload: hard -->
<!-- assignee: Claude -->

#### Import des vidéos aux métadonnées illisibles
<!-- id: task-1794400000000-70 -->
Une vidéo WhatsApp mal formée bloquait la sauvegarde depuis le 23/10.
Tags: `windows` `nas`
<!-- assignee: Claude -->

#### Sélection multiple, suppression groupée et corbeille (Windows)
<!-- id: task-1794400000000-71 -->
Ctrl, Maj, Ctrl+A ; « Vider la corbeille » avec confirmation.
Tags: `windows`
<!-- assignee: Claude -->

#### Boîtes de dialogue au style Windows 11
<!-- id: task-1794400000000-72 -->
Toutes les MessageBox remplacées.
Tags: `windows`
<!-- assignee: Claude -->

#### Visionneuse photos dans l'administration web du NAS
<!-- id: task-1794400000000-73 -->
Filtres, sélection, corbeille, plein écran, vidéos, HEIC converti.
Tags: `nas`
<!-- assignee: Claude -->

#### État détaillé des sauvegardes et barre de progression par fichier
<!-- id: task-1794400000000-74 -->
Erreurs dans les journaux du conteneur, fichier en cours, débit, fichiers refusés mis de côté et réessayés.
Tags: `nas` `windows` `android`
<!-- assignee: Claude -->

#### Garder la session d'administration du NAS après un redémarrage
<!-- id: task-1794400000000-12 -->
Clés des cookies conservées dans /config/keys.
Tags: `nas`
<!-- assignee: Claude -->

#### 2. Installer l'app via YAML
<!-- id: task-1794400000000-31 -->
App custom monalbumpc installée par l'API le 18/11, avec le bouton Web UI.
Tags: `nas`
<!-- assignee: Claude -->

#### 1. Créer le dataset de configuration
<!-- id: task-1794400000000-30 -->
main/data/monalbumpc, propriétaire apps (568).
Tags: `nas`
<!-- assignee: Claude -->

#### Suivi du projet dans ce tableau
<!-- id: task-1794400000000-40 -->
Remplace le tableau Trello et le TODO.md personnel (11/11/2026).
<!-- assignee: Claude -->

#### Bibliothèque partagée et mode Visionneuse avec notifications
<!-- id: task-1794400000000-41 -->
library.json, fil d'activité, fin de sauvegarde signalée par le téléphone, visionneuse web du NAS retirée. Build 13, CI verte.
Tags: `windows` `nas` `android`
<!-- assignee: Claude -->

#### Corbeille d'album sur dossier réseau
<!-- id: task-1794400000000-42 -->
Tags: `windows`
<!-- assignee: Claude -->

#### Secrets de la clé de release dans GitHub
<!-- id: task-1794400000000-43 -->
Tags: `ci`
<!-- assignee: Nicolas -->

#### Mise à jour de l'app Android depuis le serveur
<!-- id: task-1794400000000-44 -->
Tags: `android`
<!-- assignee: Claude -->

#### Build Android release signé et optimisé (14,5 Mo)
<!-- id: task-1794400000000-45 -->
Tags: `android`
<!-- assignee: Claude -->

#### Icône Gemini exportée (adaptive icon, .ico, zone de notification)
<!-- id: task-1794400000000-46 -->
Tags: `windows` `android`
<!-- assignee: Claude -->

#### Brief Claude Design de l'app Windows
<!-- id: task-1794400000000-47 -->
Tags: `windows`
<!-- assignee: Claude -->

#### Brief Claude Design de l'app Android
<!-- id: task-1794400000000-48 -->
Palette remplacée par celle de l'icône Gemini (docs/design/palette.md).
Tags: `android`
<!-- assignee: Claude -->

#### Nouvel essai quand le serveur ne répond pas (veille)
<!-- id: task-1794400000000-49 -->
Tags: `android`
<!-- assignee: Claude -->

#### Option « Autoriser les données mobiles » (VPN)
<!-- id: task-1794400000000-50 -->
Tags: `android`
<!-- assignee: Claude -->

#### Serveur NAS Docker et administration web (Blazor)
<!-- id: task-1794400000000-51 -->
Tags: `nas`
<!-- assignee: Claude -->

#### Indexation du dossier de destination existant
<!-- id: task-1794400000000-52 -->
Tags: `windows`
<!-- assignee: Claude -->

#### Visionneuse Windows (grille virtualisée, plein écran, vidéo, rotation)
<!-- id: task-1794400000000-53 -->
Tags: `windows`
<!-- assignee: Claude -->

#### Historique des sessions et notifications Windows
<!-- id: task-1794400000000-54 -->
Tags: `windows`
<!-- assignee: Claude -->

#### App de la zone de notification et fenêtre principale
<!-- id: task-1794400000000-55 -->
Tags: `windows`
<!-- assignee: Claude -->

#### Serveur HTTPS protocole v1 et page de téléchargement de l'APK
<!-- id: task-1794400000000-56 -->
Tags: `windows`
<!-- assignee: Claude -->

#### Association par QR code ou code à 6 chiffres, certificat épinglé
<!-- id: task-1794400000000-57 -->
Tags: `windows` `android`
<!-- assignee: Claude -->

#### Découverte mDNS (_monalbumpc._tcp)
<!-- id: task-1794400000000-58 -->
Tags: `windows`
<!-- assignee: Claude -->

#### Catalogue SQLite, dédoublonnage, rangement AAAA/AAAA-MM, conversion HEIC
<!-- id: task-1794400000000-59 -->
Tags: `windows`
<!-- assignee: Claude -->

#### Moteur de sauvegarde Android (MediaStore, WorkManager, envois reprenables)
<!-- id: task-1794400000000-60 -->
Tags: `android`
<!-- assignee: Claude -->

#### Galerie Android avec badges d'état
<!-- id: task-1794400000000-61 -->
Tags: `android`
<!-- assignee: Claude -->

#### Écran Dossiers Android (albums MediaStore)
<!-- id: task-1794400000000-62 -->
Tags: `android`
<!-- assignee: Claude -->

#### Squelette Compose : thème, police Nunito, navigation à 3 onglets
<!-- id: task-1794400000000-63 -->
Tags: `android`
<!-- assignee: Claude -->

#### CI GitHub Actions : Windows, Android, image NAS
<!-- id: task-1794400000000-64 -->
Tags: `ci`
<!-- assignee: Claude -->

#### Protocole v1 rédigé (docs/protocol.md)
<!-- id: task-1794400000000-65 -->
Découverte mDNS, association QR code, TLS épinglé, transfert reprenable, hash.
<!-- assignee: Claude -->

#### Dédoublonnage et historique d'import
<!-- id: task-1794400000000-66 -->
Historique par téléphone, hash de contenu, indexation de la destination existante, verrou USB/Wi-Fi.
<!-- assignee: Claude -->

#### Cahier des charges (docs/cdc.md)
<!-- id: task-1794400000000-67 -->
<!-- assignee: Claude -->

#### Questions de cadrage
<!-- id: task-1794400000000-68 -->
<!-- assignee: Nicolas -->

#### Dépôt GitHub public lgm42/mon-album-pc
<!-- id: task-1794400000000-69 -->
<!-- assignee: Nicolas -->
