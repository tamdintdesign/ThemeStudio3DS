# Format statique pour Anemone3DS

Le ZIP contient `body_LZ.bin` à la racine. Anemone recherche ce nom sans tenir compte de la casse et installe le fichier dans le cache HOME Menu. Le ZIP inclut aussi un `info.smdh` v0 de 0x36C0 octets avec le titre, l’auteur et une icône neutre. Anemone3DS v2.1.0 a exigé ce fichier. Les sources actuelles prévoient un nom de secours. Le ZIP fonctionne selon ces deux conventions. La musique et l’aperçu PNG sont facultatifs. La limite du cache de corps utilisée par Anemone pour les thèmes en rotation est de `0x150000` octets compressés.

Le corps décompressé utilise des entiers little-endian et une version de 1. L’octet `0x05` vaut zéro (sans BGM). Les deux modes de dessin valent 3 (texture) et les deux types de trame valent 1 (statique). Le profil ci-dessous laisse les champs réservés et les sections facultatives à zéro.

| Offset | Valeur | Rôle |
| --- | --- | --- |
| `0x00` | `1` | Version |
| `0x0C`, `0x20` | `3` | Modes de dessin haut et bas |
| `0x10`, `0x24` | `1` | Types de trame haut et bas |
| `0x18` | `0xD0` | Offset de texture haute |
| `0x28` | `0x400D0` | Offset de texture basse |

Chaque texture occupe `0x40000` octets (512 × 256 × 2). Les pixels RGB565 little-endian ont le rouge dans les 5 bits forts, le vert dans les 6 suivants et le bleu dans les 5 faibles. Les tuiles 8 × 8 se suivent de gauche à droite et de haut en bas. Dans chaque tuile, les trois bits de x et de y sont entrelacés dans l’ordre x0, y0, x1, y1, x2, y2 du bit faible au bit fort. L’origine est en haut à gauche. Les zones utiles sont 412 × 240 en haut et 320 × 240 en bas. Le corps entier mesure `0x800D0` octets, aligné à 16 octets.

Le flux LZ11 commence par `0x11` puis la taille décompressée sur 24 bits little-endian. Les groupes de huit symboles partagent un octet de drapeaux lu du bit fort au bit faible. Un drapeau à zéro indique un octet littéral. Un drapeau à un indique une référence arrière encodée sur deux à quatre octets, avec une distance de 1 à 4096. La borne avec uniquement des littéraux et l’alignement de quatre octets est de 590 064 octets.

## Références

- [3dbrew, Home Menu/Themes](https://www.3dbrew.org/wiki/Home_Menu/Themes) et [miroir Mikage](https://docs.mikage.app/Home_Menu/Themes/).
- [Anemone3DS, installation et cache](https://github.com/astronautlevel2/Anemone3DS/blob/master/source/themes.c) et [lecture des ZIP et du LZ11](https://github.com/astronautlevel2/Anemone3DS/blob/master/source/fs.c).
- [Usagi](https://github.com/usagirei/3DS-Theme-Editor), dépôt local au commit `da1ac2d`. `Theme.cs` confirme la version, les offsets et les dimensions, `RawTexture.cs` confirme l’ordre des canaux et des tuiles et `LZ11.cs` fournit un décodeur externe. Le dépôt est sous licence MIT. Les sources servent de référence et de lecteur externe, sans intégration dans 3DS Theme Studio.

- [Version Anemone3DS exigeant les métadonnées](https://github.com/astronautlevel2/Anemone3DS/releases/tag/v2.1.0) et [format SMDH](https://www.3dbrew.org/wiki/SMDH).

## Rendu du projet

Les deux fonds doivent être présents. Le rendu applique le zoom et le pan du projet à la taille affichée dans Studio (400 × 240 et 320 × 240). Une image de toute autre taille est redimensionnée proportionnellement pour couvrir le cadre. Sans recadrage enregistré, le rendu est centré. La transparence est composée sur du noir avant la conversion RGB565. Pour la zone haute de 412 pixels, les colonnes aux extrémités sont prolongées de six pixels de chaque côté. Le reste des textures est rempli de noir (zéro).

Seuls les deux fonds statiques sont exportés. Les PNG originaux, les canevas auxiliaires et les paramètres restent dans le projet. Le ZIP omet la musique, les effets sonores, les icônes du projet, les couleurs personnalisées et la vignette.
