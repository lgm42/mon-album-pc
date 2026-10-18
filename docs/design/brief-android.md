# Brief design — Mon Album PC, app Android

Brief destiné à Claude Design. Référence fonctionnelle complète : [`../cdc.md`](../cdc.md).

## 1. Le produit en une phrase

**Mon Album PC** sauvegarde automatiquement les photos et vidéos d'un téléphone Android sur le PC de la
maison, en Wi-Fi, sans câble ni cloud. Une app Windows compagnon reçoit les fichiers et les range.

## 2. Pour qui

- **Les utilisateurs finaux** ne sont pas informaticiens : parents, grands-parents, amis. Ils ne savent
  pas ce qu'est un « dossier DCIM » et ne doivent jamais avoir à le savoir.
- **L'installateur** est une personne de l'entourage qui s'y connaît. Il associe le téléphone en
  quelques secondes, puis l'utilisateur ne touche plus à rien.
- Une fois l'app installée, elle doit se faire oublier. On l'ouvre surtout pour vérifier que « c'est bien
  sauvegardé ».

## 3. Ton et identité

- Sérieux, rassurant, clair. Pas ludique, pas technique.
- Interface **en français uniquement**, vouvoiement.
- Vocabulaire du quotidien : « photos », « vidéos », « sauvegardé », « votre PC ». À proscrire :
  « synchroniser des buckets », « MediaStore », « MTP », « hash », « TLS ». Si un terme technique est
  inévitable (par exemple « Transfert de fichiers » dans le menu USB d'Android), on l'écrit entre
  guillemets, exactement comme le téléphone l'affiche.
- **À proposer** : une icône d'app et un petit logo, communs aux deux apps (Android et Windows).
  Le nom doit rester lisible en petit.

## 4. Contraintes de plateforme

- Material Design 3 (Jetpack Compose), thème clair et sombre, couleurs dynamiques Android 12+ en option
  mais avec une palette de marque par défaut.
- Android 10 minimum. Écran de référence : Samsung Galaxy A35 (6,6", 1080×2340). Doit rester utilisable
  sur des petits écrans et avec une taille de police système agrandie (public âgé).
- Contrastes accessibles (WCAG AA), cibles tactiles de 48 dp minimum.

## 5. Navigation

Barre de navigation en bas, trois onglets :

1. **Photos** (accueil) : la galerie avec l'état de sauvegarde.
2. **Dossiers** : ce qui est sauvegardé.
3. **Réglages**

Un **bandeau d'état** permanent en haut de l'onglet Photos résume la situation en une ligne.

## 6. Écrans

### 6.1 Premier lancement (association)

Objectif : 2 secondes pour l'installateur quand tout va bien.

1. **Bienvenue** : « Sauvegardez vos photos sur votre PC, automatiquement. » Bouton « Commencer ».
2. **Autorisations**, demandées une par une, chaque fois avec une phrase d'explication avant la demande
   système :
   - photos et vidéos : « pour pouvoir les copier sur votre PC » ;
   - notifications : « pour vous prévenir quand c'est sauvegardé ».

   Prévoir le cas où l'utilisateur accorde un **accès partiel** (Android 14 : « sélectionner des
   photos ») : il faut expliquer que tout ne pourra pas être sauvegardé, et proposer de corriger.
3. **Scanner le QR code** : viseur caméra plein écran, consigne « Sur le PC, ouvrez Mon Album PC et
   cliquez sur Ajouter un téléphone ». Lien secondaire « Je n'ai pas de caméra qui fonctionne » (saisie
   d'un code, à valider).
4. **Association réussie** : « Ce téléphone est relié à **PC-SALON**, album de **Marie** ».
5. **Choix des dossiers** : voir §6.3. Les dossiers recommandés sont déjà cochés, l'utilisateur peut juste
   valider.
6. **C'est parti** : la première sauvegarde démarre. On montre une progression globale (« 1 240 / 10 312 »)
   et on rassure : « Vous pouvez fermer l'app, la sauvegarde continue. »

États d'erreur à dessiner :
- QR code expiré ou déjà utilisé ;
- PC introuvable : téléphone et PC pas sur le même Wi-Fi ;
- ce téléphone est déjà associé à un autre PC (proposer de le dissocier d'abord).

### 6.2 Photos (accueil) — galerie avec état de sauvegarde

C'est l'écran signature de l'app. L'idée vient des badges de statut de git : chaque miniature porte un
petit badge indiquant son état.

| État | Signification | Remarque |
| --- | --- | --- |
| Sauvegardé | Le fichier est sur le PC, vérifié | Badge discret, c'est l'état normal |
| En attente | Sera envoyé à la prochaine occasion | |
| En cours | Transfert en cours | Peut montrer une progression pour les grosses vidéos |
| Erreur | Échec, sera réessayé | Toucher la miniature explique le problème |
| Non suivi | Le dossier n'est pas sauvegardé | Miniature atténuée |

- Grille de miniatures groupées par **mois** (en-têtes « Octobre 2026 »), défilement rapide avec une
  poignée de date.
- Les vidéos montrent leur durée.
- **Filtres** en puces sous le bandeau : Tout · En attente · Erreurs · Non suivis.
- **Bandeau d'état** en haut, une ligne, avec quelques variantes :
  - « Tout est sauvegardé · dernière sauvegarde il y a 2 h »
  - « 52 photos en attente · sauvegarde dès que le PC est joignable »
  - « Sauvegarde en cours · 12 / 52 »
  - « PC-SALON introuvable depuis 3 jours » (en orange)
  - « Le PC ne peut pas recevoir les photos : son disque de destination est débranché » (en orange)
- Bouton **« Sauvegarder maintenant »** (bouton flottant ou action dans le bandeau).
- **Vue plein écran** d'un média : photo ou lecture vidéo, balayage gauche/droite, et une feuille
  d'information avec l'état détaillé (« Sauvegardé le 14/10/2026 sur PC-SALON », ou le motif de l'erreur).
- État vide : aucune photo dans les dossiers suivis.

### 6.3 Dossiers

Liste des dossiers du téléphone qui contiennent des photos ou des vidéos. Chaque ligne affiche une
miniature (dernière photo), un nom lisible (« Appareil photo », « WhatsApp Images », « Captures
d'écran »), le nombre d'éléments et un interrupteur.

Sections :
1. **Recommandés** (cochés par défaut) : Appareil photo, Captures d'écran, WhatsApp Images,
   WhatsApp Video.
2. **Autres applications** (non cochés) : photos envoyées WhatsApp, statuts WhatsApp, Snapchat, et toute
   autre app détectée.
3. **Autres dossiers** : le reste, y compris la carte SD (avec une petite étiquette « Carte SD »).
4. Bouton **« Ajouter un dossier… »** qui ouvre le sélecteur de dossiers d'Android.

Quand on décoche un dossier, une confirmation explique que les photos déjà sauvegardées restent sur
le PC.

### 6.4 Réglages

- **Mon PC** : nom du PC, nom de l'album (utilisateur), dernière sauvegarde, et une **icône corbeille**
  « Dissocier ce téléphone ». La confirmation doit préciser que rien n'est supprimé, ni sur le téléphone
  ni sur le PC.
- **Sauvegarder uniquement pendant la recharge** : interrupteur, désactivé par défaut.
- **Notifications** : raccourci vers les réglages système.
- **Brancher en USB** : petite aide illustrée pour choisir « Transfert de fichiers » quand on branche le
  téléphone au PC (l'import est plus rapide en USB).
- **À propos** : version, mise à jour disponible, licence.

### 6.5 Mise à jour

Quand le PC propose une version plus récente de l'app : un bandeau ou une carte « Une nouvelle version
de Mon Album PC est disponible », avec un bouton « Mettre à jour ». Ensuite, c'est le dialogue
d'installation système d'Android qui prend le relais.

### 6.6 Téléphone dissocié

Si le PC a retiré ce téléphone, l'app affiche un écran plein : « Ce téléphone n'est plus relié à
PC-SALON », avec un bouton « Associer à nouveau » (qui revient au scanner).

## 7. Notifications Android

- **Pendant la sauvegarde** : notification de progression, discrète.
- **Fin** : « 52 photos et 3 vidéos sauvegardées sur PC-SALON ».
- **Erreur** : message court et actionnable, par exemple « Sauvegarde impossible : le disque du PC est
  plein ».
- Rien d'autre : pas de notification permanente quand rien ne se passe.

## 8. Page web de téléchargement (affichée sur le téléphone)

Quand l'app n'est pas encore installée, l'installateur scanne le QR code du PC avec l'appareil photo du
téléphone. Une **page web mobile**, servie par le PC, s'ouvre. Elle doit afficher :

- le logo et le nom, avec une phrase d'explication ;
- un gros bouton **« Télécharger l'application »** ;
- 3 étapes illustrées :
  1. autoriser l'installation depuis le navigateur ;
  2. installer ;
  3. ouvrir l'app et scanner à nouveau le QR code affiché sur le PC.

C'est une page HTML statique, légère, sans dépendance externe : elle doit fonctionner sans Internet.

## 9. Livrables attendus

- L'icône d'app et le logo.
- Les maquettes des écrans du §6, en clair et en sombre, y compris les états vides et d'erreur listés.
- Les composants : miniature avec badge (5 états), bandeau d'état (variantes), ligne de dossier.
- La page web du §8.
