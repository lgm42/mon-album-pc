# Protocole PC ↔ Android — v1

Ce document décrit comment l'app Android et l'app Windows de Mon Album PC communiquent. Il fait
référence pour les deux implémentations ; toute évolution incompatible incrémente `v`.

## 1. Principes

- Le **PC est serveur**, le **téléphone est client** : c'est toujours le téléphone qui pousse.
- Tout passe en **HTTPS** sur le réseau local, avec le certificat auto-signé du PC **épinglé** à
  l'association. Seule exception, le port HTTP en clair, qui ne sert que la page de téléchargement et l'APK
  (§7).
- Les fichiers sont identifiés par le **SHA-256 de leurs octets d'origine**, en hexadécimal minuscule (64 caractères) : c'est la clé de
  dédoublonnage. Elle ne change pas si le fichier est renommé ou déplacé, et le PC la garde même après une
  conversion HEIC → JPEG.
- Les dates sont en ISO 8601 avec fuseau (`2026-10-17T13:17:05+02:00`). Les tailles sont en octets.
- Les corps sont en JSON UTF-8, en `camelCase`.

## 2. Réseau

| Usage | Port | Protocole |
| --- | --- | --- |
| API | `48620` TCP | HTTPS (TLS 1.2 minimum) |
| Page de téléchargement + APK | `48621` TCP | HTTP |
| Découverte | `5353` UDP | mDNS |

L'installeur Windows crée les règles de pare-feu entrantes pour `48620` et `48621`, sur les profils
privé et domaine.

### Découverte mDNS

- Service : `_monalbumpc._tcp.local.`, port `48620`.
- Enregistrements TXT :
  - `v=1` : version du protocole ;
  - `id=<pcId>` : identifiant stable du PC (UUID) ;
  - `n=<nom du PC>`.
- Après l'association, le téléphone retrouve « son » PC par `id`, ce qui tolère un changement d'adresse
  IP. En secours, il essaie la dernière adresse connue.

## 3. Association

### 3.1 QR code

Le PC génère un **jeton d'association** : 128 bits aléatoires, encodés en base32 sans remplissage. Le
jeton est à usage unique et valable **5 minutes**. Le QR code encode l'URL :

```text
http://<ip>:48621/p?t=<jeton>&f=<empreinte>&h=<ip>&n=<nom du PC urlencodé>
```

- `f` : SHA-256 du certificat du PC (DER), en base64url.
- `p` : port de l'API, présent seulement s'il diffère de `48620`.
- **Scanné depuis l'app** : l'app lit directement les paramètres, sans ouvrir de navigateur, puis appelle
  `POST /api/v1/pair` en vérifiant que le certificat présenté correspond à `f`.
- **Scanné avec l'appareil photo** : le navigateur ouvre la page de téléchargement (§7). Elle propose
  « Ouvrir l'app », un lien `intent://` qui transmet les mêmes paramètres et se rabat sur l'APK si l'app
  est absente, ainsi que « Télécharger l'application ».

### 3.2 Code à 6 chiffres (téléphone sans caméra)

- En même temps que le QR code, le PC affiche un code à 6 chiffres, lui aussi valable 5 minutes.
- Le téléphone découvre les PC en mDNS. S'il en trouve plusieurs, il les propose par nom.
- Il appelle ensuite `POST /api/v1/pair` avec `code`.
- Sans `f`, le certificat est accepté à la première connexion. L'app Windows affiche alors le nom du
  téléphone qui s'associe, ce qui permet de vérifier.
- Protection contre l'essai de codes au hasard : 5 essais faux par code au maximum ; ensuite, le code est
  invalidé et l'assistant Windows affiche « Ce code a expiré ».

### 3.3 `POST /api/v1/pair` (sans authentification)

Requête :

```json
{
  "token": "K7Q2…",
  "code": null,
  "device": {
    "name": "A35 de Marie",
    "model": "Samsung Galaxy A35",
    "androidVersion": "15",
    "appVersion": "1.0.0",
    "appVersionCode": 1
  }
}
```

Un seul champ parmi `token` et `code` est renseigné.

Réponse `200` :

```json
{
  "deviceId": "8f1c…",
  "secret": "base64url de 32 octets",
  "pcId": "3b2e…",
  "pcName": "PC-SALON",
  "albumName": "Marie",
  "certSha256": "base64url"
}
```

- Le téléphone stocke `secret` dans le Keystore Android. Le PC ne garde que son SHA-256.
- Erreurs possibles : `pairing_expired`, `pairing_invalid`.

Le nom proposé pour le téléphone (« A35 de Marie ») est construit par l'app à partir du modèle et du nom
de l'album. Il est modifiable côté PC.

## 4. Authentification

Après l'association, chaque requête porte l'en-tête suivant :

```text
Authorization: Bearer <deviceId>.<secret>
```

La connexion TLS est épinglée sur `certSha256`. Si le PC répond `401 device_unknown` (téléphone
dissocié ou révoqué), l'app affiche l'écran « Ce téléphone n'est plus relié à … ».

## 5. API (`https://<pc>:48620/api/v1`)

### 5.1 `GET /status`

Réponse `200` :

```json
{
  "protocol": 1,
  "pcName": "PC-SALON",
  "albumName": "Marie",
  "destinationAvailable": true,
  "busy": null,
  "apk": { "versionCode": 3, "versionName": "1.0.2", "sha256": "…", "size": 9123456 },
  "serverTime": "2026-10-17T13:17:05+02:00"
}
```

- `busy` vaut `"usb"` quand un import USB de **ce** téléphone est en cours. Le téléphone suspend alors
  ses envois et réessaie plus tard.
- Si la version de l'APK du PC est plus récente que celle de l'app, l'app propose la mise à jour, qui se
  télécharge par `GET /apk`.

### 5.2 `POST /check`

Il sert à savoir ce que le PC possède déjà : c'est ce qui met à jour les badges de la galerie et évite les
envois inutiles.

Requête :

```json
{ "items": [ { "sha256": "…", "size": 2621440 } ] }
```

Réponse :

```json
{ "have": [ "…" ] }
```

- 1 000 éléments maximum par appel.
- « Possède » veut dire : présent dans l'historique d'import de ce téléphone, ou reconnu dans l'index du
  dossier de destination (§9 du cahier des charges).

### 5.3 Envoi reprenable

**1. Déclarer le fichier** : `POST /uploads`

```json
{
  "sha256": "…",
  "size": 734003200,
  "name": "VID_20261017_131705.mp4",
  "sourceFolder": "Appareil photo",
  "mime": "video/mp4",
  "takenAt": "2026-10-17T13:17:05+02:00",
  "modifiedAt": "2026-10-17T13:18:40+02:00"
}
```

- `takenAt` : date de prise de vue connue du téléphone (MediaStore), ou `null`. Le PC lit lui-même
  l'EXIF et ne se sert de cette valeur qu'en secours.
- Réponse `200 { "status": "exists" }` : déjà sauvegardé, le téléphone marque le fichier comme sauvegardé.
- Réponse `200 { "status": "upload", "uploadId": "…", "offset": 0 }` : à envoyer. Si un envoi
  interrompu existe pour ce SHA-256 et ce téléphone, `offset` indique où il s'est arrêté.

**2. Envoyer les octets** : `PATCH /uploads/{uploadId}`

- En-têtes : `Upload-Offset: <n>`, `Content-Type: application/octet-stream`.
- Le corps contient les octets à partir de `n`. L'idéal est d'envoyer le reste du fichier d'un seul flux ;
  il peut aussi être découpé en morceaux.
- Réponse `200 { "offset": <nouvel offset> }`. Si `Upload-Offset` ne correspond pas à ce que le PC a
  reçu : `409 offset_mismatch`, avec l'offset attendu.
- Quand `offset == size`, le PC vérifie le SHA-256, convertit si besoin, range le fichier, l'inscrit
  dans l'historique, puis répond `200 { "offset": <taille>, "status": "saved" }` (ou `"exists"` si un fichier
  identique est déjà rangé).
- Si le SHA-256 ne correspond pas : `422 hash_mismatch`. L'envoi est effacé et sera repris de zéro.

**3. Reprendre après une coupure** : `HEAD /uploads/{uploadId}` renvoie l'offset reçu dans l'en-tête
`Upload-Offset`. Un `404` signifie que l'envoi a expiré : il faut recommencer à l'étape 1.

- Les envois partiels sont conservés **7 jours** côté PC, dans un dossier temporaire situé sur le même
  volume que la destination.
- Le téléphone envoie **un fichier à la fois**, du plus récent au plus ancien : les dernières photos sont à l'abri en quelques minutes, même pendant une première sauvegarde de plusieurs milliers de fichiers.

### 5.4 `DELETE /pairing`

C'est la corbeille de l'app Android. Le PC supprime le téléphone de l'album, mais garde son historique
d'import. Réponse `204`.

### 5.5 `GET /apk`

Renvoie l'APK embarqué dans l'app Windows (`application/vnd.android.package-archive`). L'app vérifie
son SHA-256 avant de lancer l'installeur Android.

## 6. Erreurs

Format :

```json
{ "error": { "code": "destination_unavailable", "message": "…" } }
```

Le champ `message` est lisible, en français, et sert aux journaux. L'interface se base sur `code`.

| HTTP | `code` | Comportement du téléphone |
| --- | --- | --- |
| 401 | `device_unknown` | Écran « téléphone dissocié » |
| 400 | `pairing_invalid` | « Ce QR code n'est plus valable » |
| 410 | `pairing_expired` | idem |
| 409 | `busy_usb` | Réessaie plus tard, sans erreur visible |
| 409 | `offset_mismatch` | Reprend à l'offset indiqué |
| 422 | `hash_mismatch` | Recommence l'envoi (3 essais, puis erreur sur le média) |
| 503 | `destination_unavailable` | Bandeau « Le PC ne peut pas recevoir les photos… », réessaie plus tard |
| 507 | `disk_full` | Erreur « le disque du PC est plein », réessaie plus tard |
| 426 | `protocol_unsupported` | Propose la mise à jour de l'app |

## 7. Port HTTP `48621`

- `GET /p?…` : page de téléchargement mobile. C'est du HTML statique embarqué, sans ressource externe.
- `GET /monalbumpc.apk` : l'APK.
- Le HTTP en clair est accepté pour cette seule étape. L'APK est signé : Android refusera ensuite toute
  mise à jour signée par une autre clé.

## 8. Import USB (MTP)

Le PC ne parle pas à l'app pendant un import USB. Il lit et écrit des fichiers dans le dossier de l'app,
visible en MTP :

```text
Android/media/io.github.lgm42.monalbumpc/
├── manifest.json          écrit par le téléphone
└── ack-<pcId>.json        écrit par le PC
```

### 8.1 `manifest.json`

L'app le réécrit après chaque changement dans les dossiers suivis. Elle le signale ensuite au
MediaScanner, pour qu'il apparaisse tout de suite en MTP.

```json
{
  "format": 1,
  "deviceId": "8f1c…",
  "pcId": "3b2e…",
  "generatedAt": "2026-10-17T13:20:00+02:00",
  "items": [
    {
      "path": "DCIM/Camera/IMG_20261017_131705.jpg",
      "volume": "primary",
      "size": 2621440,
      "modifiedAt": "2026-10-17T13:17:05+02:00",
      "takenAt": "2026-10-17T13:17:05+02:00",
      "sha256": "…",
      "sourceFolder": "Appareil photo"
    }
  ]
}
```

- `path` est relatif à la racine du volume, tel que l'Explorateur le montre en MTP.
- `volume` vaut `primary` pour la mémoire interne, ou l'identifiant de la carte SD.
- Le secret d'authentification n'y figure **jamais**.

### 8.2 Déroulé côté PC

1. Il lit `manifest.json`. S'il ne connaît pas le `deviceId`, il propose l'association.
2. Il pose le verrou `busy=usb` sur ce téléphone ; un envoi Wi-Fi en cours s'interrompt proprement.
3. Il retient les éléments dont le `sha256` est absent de l'historique.
4. Il copie ces fichiers, vérifie leur SHA-256, les range et les inscrit dans l'historique (canal `usb`).
5. Il écrit `ack-<pcId>.json` : `{ "format": 1, "importedAt": "…", "sha256": ["…"] }`.
6. Il lève le verrou.

L'app lit le fichier `ack-*.json` au prochain lancement ou à la prochaine tâche de fond, marque les
fichiers comme sauvegardés, puis supprime le fichier `ack`.

### 8.3 Manifeste absent ou périmé

Pour les fichiers qui ne sont pas dans le manifeste, le PC compare le chemin, la taille et la date de
modification avec son historique. Si tout correspond, le fichier est considéré comme déjà importé, sans
téléchargement. Sinon, il le copie, calcule son SHA-256 et applique le dédoublonnage habituel.

## 9. Points à valider sur un vrai téléphone (Samsung A35)

- Visibilité en MTP de `Android/media/<package>/`, et délai d'apparition après le scan du MediaScanner.
- Droit pour l'app de lire et supprimer un `ack-*.json` créé par MTP dans son propre dossier.
- Comportement de l'appareil photo Samsung face à un QR code `http://` : ouverture directe ou passage par
  une confirmation.
