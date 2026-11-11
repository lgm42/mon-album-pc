# Mon Album PC — version NAS (TrueNAS SCALE)

Serveur Mon Album PC dans un conteneur Docker, pour un NAS toujours allumé. Les téléphones lui envoient leurs
photos exactement comme à l'app Windows ; une interface web permet d'administrer les albums, les téléphones et
l'historique. Pour regarder les photos, utilisez l'app Windows en mode **Visionneuse** (voir plus bas) : elle lit la
bibliothèque à travers le partage réseau.

## Installer sur TrueNAS SCALE 24.10 ou plus récent

1. **Préparer la configuration** : créez le dataset `main/data/monalbumpc` (Datasets › Add Dataset, préréglage
   *Generic*). L'utilisateur **`apps` (568)** doit pouvoir y écrire : c'est déjà le cas s'il hérite d'une ACL qui le
   lui permet, sinon Edit Permissions › Owner et Group `apps`. Il contient le catalogue et le certificat du serveur.
2. **Préparer la bibliothèque** : c'est le dossier `/mnt/main/data/donnees/Photos/Telephones`, avec un sous-dossier par
   personne (`Nico`, `Marie`…), partagé en SMB avec le reste de `donnees`. Dans l'ACL du dataset (Edit Permissions),
   vérifiez ou donnez le droit **Modify, avec héritage** (flags *Inherit*) :
   - à l'utilisateur **`apps` (568)**, qui écrit les photos ;
   - à votre utilisateur ou groupe SMB, qui les lit, les pivote et les supprime depuis Windows.

   Les fichiers créés par le conteneur **héritent de cette ACL** : c'est elle qui les garde lisibles à travers le
   partage, pas leur propriétaire (`apps`). Sans héritage, ils appartiendraient à `apps` seul et l'app Windows ne
   pourrait pas les ouvrir. Si des dossiers existent déjà, appliquez l'ACL récursivement.
3. **Installer l'app** : Apps › Discover Apps › menu ⋮ › **Install via YAML**, puis collez
   [`docker-compose.truenas.yml`](docker-compose.truenas.yml) en adaptant au besoin les deux chemins `/mnt/...` et le
   nom `MONALBUMPC_NAME`.
4. Ouvrez **`https://<adresse-du-nas>:48622`**. Le navigateur signale un certificat auto-signé : c'est normal,
   acceptez-le. Au premier accès, choisissez le mot de passe administrateur.
5. Créez un album (son dossier est choisi sous `/photos`), puis **Ajouter un téléphone** : scannez le QR code avec le
   téléphone.
6. Depuis un PC, vérifiez les droits : `\\<adresse-du-nas>\donnees\Photos\Telephones\.monalbumpc\library.json` doit
   s'ouvrir (dossier caché), et une photo reçue doit s'afficher.

> L'image `ghcr.io/lgm42/mon-album-pc` doit être publique sur GitHub (Packages › mon-album-pc › Package
> settings › Change visibility), sinon TrueNAS ne peut pas la télécharger.

## Regarder les photos : le mode Visionneuse de l'app Windows

Le NAS est le seul à recevoir les photos ; les PC les consultent directement dans le partage réseau, sans serveur ni
association.

1. Installez l'app Windows, puis ouvrez **Réglages › Bibliothèque › Utiliser une bibliothèque sur le NAS…**
2. Choisissez le dossier partagé, par exemple `\\<adresse-du-nas>\donnees\Photos\Telephones`. L'app redémarre en mode
   Visionneuse : Photos, Carte, Réglages, et une notification Windows à chaque sauvegarde.

Le NAS publie pour cela, dans `/photos/.monalbumpc/`, la liste des albums (`library.json`, en chemins relatifs) et un
fil d'activité (`activity/`). Il est seul à écrire ces fichiers. Le catalogue d'un PC autonome reste intact : vous pouvez
revenir au mode autonome à tout moment.

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
| `MONALBUMPC_PHOTOS` | `/photos` | Bibliothèque : dossier parent des albums |

Les photos supprimées depuis l'app Windows vont dans la corbeille de leur album : `<album>/.monalbumpc/corbeille`.

**Gardez le dossier `/config`** : il contient le certificat que les téléphones ont mémorisé. Le perdre oblige à
réassocier chaque téléphone.

## Construire l'image soi-même

```sh
docker build -f nas/Dockerfile -t mon-album-pc .
```

Depuis la racine du dépôt. Pour embarquer l'app Android, placez `monalbumpc.apk` et `monalbumpc.apk.json`
dans `nas/android/` avant la construction (la CI le fait automatiquement).
