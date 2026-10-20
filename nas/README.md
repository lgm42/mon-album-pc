# Mon Album PC — version NAS (TrueNAS SCALE)

Serveur Mon Album PC dans un conteneur Docker, pour un NAS toujours allumé. Les téléphones lui envoient leurs
photos exactement comme à l'app Windows ; une interface web permet de tout administrer (albums, téléphones,
historique, visionneuse).

## Installer sur TrueNAS SCALE 24.10 ou plus récent

1. **Préparer deux datasets** (Datasets › Add Dataset), par exemple :
   - `tank/apps/monalbumpc` : configuration (catalogue, certificat du serveur) ;
   - `tank/photos` : les photos. Les dossiers des albums seront créés dessous.

   Donnez à l'utilisateur **`apps` (568)** le droit de modifier ces deux datasets (Edit Permissions / ACL).
2. **Installer l'app** : Apps › Discover Apps › menu ⋮ › **Install via YAML**, puis collez
   [`docker-compose.truenas.yml`](docker-compose.truenas.yml) en adaptant les deux chemins `/mnt/...` et le
   nom `MONALBUMPC_NAME`.
3. Ouvrez **`https://<adresse-du-nas>:48622`**. Le navigateur signale un certificat auto-signé : c'est normal,
   acceptez-le. Au premier accès, choisissez le mot de passe administrateur.
4. Créez un album, puis **Ajouter un téléphone** : scannez le QR code avec le téléphone.

> L'image `ghcr.io/lgm42/mon-album-pc` doit être publique sur GitHub (Packages › mon-album-pc › Package
> settings › Change visibility), sinon TrueNAS ne peut pas la télécharger.

## Ports et réseau

Le conteneur utilise le **réseau de l'hôte** (`network_mode: host`), indispensable pour que les téléphones
trouvent le serveur sur le réseau local (mDNS).

| Port | Usage |
| --- | --- |
| 48620 (HTTPS) | API des téléphones |
| 48621 (HTTP) | Page de téléchargement de l'app Android |
| 48622 (HTTPS) | Interface web d'administration |

**Depuis l'extérieur, via un VPN** (WireGuard vers la maison) : le téléphone joint le NAS à l'adresse
mémorisée lors de l'association. Activez dans l'app Android « Autoriser les données mobiles » pour que la
sauvegarde parte aussi en 4G/5G.

## Variables d'environnement

| Variable | Défaut | Rôle |
| --- | --- | --- |
| `MONALBUMPC_NAME` | nom de l'hôte | Nom affiché sur les téléphones |
| `MONALBUMPC_ADMIN_PASSWORD` | — | Mot de passe administrateur initial (sinon demandé au premier accès) |
| `MONALBUMPC_ADMIN_PORT` | `48622` | Port de l'interface web |
| `MONALBUMPC_CONFIG` | `/config` | Dossier de configuration |
| `MONALBUMPC_PHOTOS` | `/photos` | Dossier parent des albums |

Les photos supprimées depuis l'interface web vont dans `/photos/.corbeille`.

**Gardez le dossier `/config`** : il contient le certificat que les téléphones ont mémorisé. Le perdre oblige à
réassocier chaque téléphone.

## Construire l'image soi-même

```sh
docker build -f nas/Dockerfile -t mon-album-pc .
```

Depuis la racine du dépôt. Pour embarquer l'app Android, placez `monalbumpc.apk` et `monalbumpc.apk.json`
dans `nas/android/` avant la construction (la CI le fait automatiquement).
