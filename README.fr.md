# 3DS Theme Studio 0.2

Éditeur de ressources pour les thèmes Nintendo 3DS. L’application fonctionne sous Linux avec .NET 10 et Avalonia.

## Démarrer et vérifier

```sh
dotnet build ThemeStudio3DS.csproj
dotnet run --project ThemeStudio3DS.csproj
dotnet run --project Tests/ThemeStudio3DS.Tests.csproj
```

La suite de tests s’exécute sans serveur graphique. Pour tester aussi le pack Frieren, passez le chemin du ZIP après `--`.

## Importer et modifier

- Importez un pack avec **Ctrl+I**, ou déposez un fichier ZIP ou PNG dans la fenêtre.
- Dans **Écrans**, importez séparément les deux fonds et recadrez-les à 400 × 240 ou 320 × 240. Le zoom conserve les proportions. Les curseurs de position parcourent la partie de l’image qui déborde du cadre.
- La section **Images** affiche les fonds, les textures sources, les dossiers, la miniature et les autres PNG. Chaque image peut être remplacée.
- **Ctrl+N** crée un projet, **Ctrl+O** l’ouvre, **Ctrl+S** l’enregistre et **Ctrl+Maj+S** l’enregistre sous un autre nom.

Les noms de fichiers sont reconnus dans les sous-dossiers, sans distinction de casse. Les noms standard sont `top_screen.png`, `bottom_screen.png`, `top_canvas_512x256.png`, `bottom_canvas_512x256.png`, `folder_NN.png` et `thumbnail.png` (alias `preview.png` ou `icon.png`). Les autres PNG restent disponibles dans la galerie. Le rapport indique les ressources importées, les fichiers inconnus, les dimensions attendues et les emplacements manquants.

Un manifeste facultatif `theme.json` peut associer un identifiant à une image. Les chemins sont relatifs à la racine du ZIP, même si le manifeste est dans un sous-dossier.

```json
{
  "resources": {
    "top_screen": "art/upper.png",
    "folder_01": "icons/closed.png"
  }
}
```

Deux fichiers associés au même identifiant constituent un conflit. L’import refuse le pack avant de modifier le projet. Les ZIP sont lus sans extraction sur disque. Ils sont limités à 100 entrées, 20 Mio par entrée, 100 Mio décompressés et 101 Mio à la lecture. Chaque PNG est limité à 4 096 pixels par axe et 8 388 608 pixels au total.

## Format de projet .3dstudio

Un fichier `.3dstudio` est un ZIP avec `project.json` à sa racine. Le manifeste version 1 stocke le nom, l’auteur, la couleur et les références des PNG originaux.

```json
{
  "version": 1,
  "name": "Mon thème",
  "author": "Auteur",
  "accent": "#849B82",
  "images": [
    {
      "id": "top_screen",
      "fileName": "top_screen.png",
      "path": "images/000.png",
      "crop": { "zoom": 1.5, "x": 0, "y": -0.3 }
    }
  ]
}
```

Le recadrage est facultatif. Le zoom va de 1 à 4 à partir de l’échelle qui couvre l’écran. Les coordonnées de −1 à 1 donnent la position relative sur la plage de déplacement disponible. Leur valeur 0 centre l’image. L’enregistrement conserve le PNG original octet pour octet. Il utilise un fichier temporaire dans le même dossier et remplace le projet après une écriture réussie. Les limites ZIP s’appliquent aussi aux projets, avec une entrée réservée au manifeste (256 Kio au maximum).

## Exporter pour Anemone3DS

1. Ouvrez un projet `.3dstudio` ou importez vos images. Renseignez les deux fonds et ajustez leur recadrage au besoin.
2. Cliquez sur **Exporter pour Anemone3DS** (**Ctrl+E**), choisissez un fichier `.zip` et confirmez le remplacement si la destination existe.
3. Copiez le ZIP dans le dossier `/themes/` à la racine de la carte SD. Laissez-le compressé.
4. Ouvrez Anemone3DS et sélectionnez le thème. Maintenez **A**, appuyez sur **Haut** sur la croix directionnelle, puis relâchez **A** pour l’installation normale.
5. Revenez au menu HOME et vérifiez les deux fonds, leur cadrage, leur orientation, leurs couleurs et l’absence de musique de thème. [Guide Anemone3DS](https://github.com/astronautlevel2/Anemone3DS/wiki/Usage).

Le ZIP contient les deux fonds statiques dans `body_LZ.bin`, ainsi que le titre et l’auteur dans `info.smdh` pour les versions d’Anemone3DS qui l’exigent. La musique, les effets sonores, les icônes importées, les couleurs du projet et la vignette sont omis. Les deux fonds sont obligatoires. Studio ajuste les dimensions au cadre avec le recadrage enregistré et compose les zones transparentes sur du noir. [Détails du format et du rendu](docs/anemone-format.md).

### Vérification

- La suite .NET couvre la lecture des anciens projets, les offsets et drapeaux, les pixels RGB565, le recadrage, la transparence, les références LZ11, le contenu du ZIP et le remplacement. Avec le pack Frieren, elle vérifie l’import, l’enregistrement, la réouverture et l’export ainsi que la conservation des PNG originaux.
- Pour l’export du projet Frieren existant, le lecteur externe d’Usagi (`LZ11.Decompress`, `Theme.Read` et `RawTexture.Decode`) a reconnu les deux textures de 512 × 256. Les 262 144 pixels décodés correspondent aux PNG du projet après conversion RGB565, marges et remplissage compris. Le corps fait 524 496 octets décompressés et 248 088 octets compressés.
- Sous Omarchy, le projet Frieren de la version 0.2 a été rouvert dans Avalonia et exporté via le sélecteur GTK. Les deux entrées du ZIP issu de cette action sont identiques octet pour octet à celles du ZIP contrôlé avec le lecteur externe.
- Validation matérielle en attente.

Pour reproduire la vérification indépendante, indiquez le dépôt Usagi local, le pack, le projet sauvegardé et le ZIP à contrôler. Si le ZIP est absent, la suite l’exporte depuis le projet sauvegardé. Les sources d’Usagi sont lues sur place et servent uniquement à compiler les tests.

```sh
dotnet run --project Tests -p:UsagiPath=/chemin/vers/3DS-Theme-Editor -- \
  "/chemin/vers/Frieren_Minimal_3DS_Usagi_Assets.zip" \
  "/chemin/vers/theme.3dstudio" \
  "/chemin/vers/Frieren-Minimal-Anemone3DS.zip"
```
