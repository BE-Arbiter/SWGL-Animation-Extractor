# SWGL Animation Extractor — design

Date : 2026-11-12

## Intention

Comparer les animations de deux squelettes Ghoul2 (`.gla` + `animation.cfg`) pour repérer celles qui
diffèrent, puis exporter ces animations en dotXSI 3.0. Les fichiers exportés sont ensuite réimportés
dans Blender avec le plugin « JA dotXSI NLA Import/Export » (`io_scene_dotXsi3`), puis réexportés en
GLA. Le but est d'extraire uniquement le diff entre deux GLA.

L'application et toute l'interface sont en **anglais**.

## Objectifs

- Charger deux GLA : **Reference** à gauche, **Compared** à droite.
- Lister les animations définies dans le `animation.cfg` de chaque côté, classées et colorées selon
  qu'elles sont identiques ou différentes de celles de l'autre côté.
- Voir une animation en 3D, comparer deux animations visuellement (A/B avec une barre de séparation)
  et chiffrer leurs différences (tableau par os).
- Exporter en XSI l'animation sélectionnée d'un côté, dans un fichier qui porte son nom.

## Hors périmètre

- Lecture dans des `.pk3`. On ne lit que des fichiers posés sur le disque.
- Modification ou écriture de GLA.
- Animations présentes dans le GLA mais absentes du `animation.cfg` : elles sont ignorées.
- Textures et skins sur le GLM : il est affiché sans texture.

## À éviter absolument

- Un XSI que le plugin Blender relit mal : axes, échelle ou nom d'os faux, décalage d'une frame.
- Une animation classée « verte » alors qu'elle diffère au-delà de la tolérance.

## Contraintes techniques

- C# / .NET 9, MonoGame DesktopGL 3.8 et ImGui.NET 1.89.7.1, comme
  `C:\Users\lambe\source\repos\SWGL-Character-Creator`.
- On copie et on adapte dans ce projet le code utile de Character Creator : `GlaFile.cs`
  (`AnimationCfg`), `GlmModel.cs`, `ImGuiRenderer.cs`, `FilePicker.cs`, `ModelViewer.cs`, le squelette
  de `Game1` et `Settings.cs`. *(Mon choix : copier plutôt que créer une bibliothèque partagée.)*
- Tests xUnit dans `tests/`, sur le modèle de Character Creator, pour la comparaison et l'écriture XSI.

## Chargement

- Chaque panneau latéral contient un champ de chemin vers un `.gla` et un bouton **Open** (sélecteur
  de fichier).
- Le `animation.cfg` est cherché dans le même dossier que le `.gla`. S'il est absent, on affiche une
  erreur dans le panneau et la liste reste vide.
- La liste contient les entrées du cfg, dans l'ordre du fichier : `name start length loop fps`.
  Le parseur `AnimationCfg.Parse` existant convient.
- Le lecteur GLA doit être étendu pour lire la **pose de repos** de chaque os. Dans l'entrée d'os à
  l'offset `p` : nom `p+0` (64 octets), flags `p+64`, parent `p+68`, `BasePoseMat` en 3×4 floats
  `p+72`, `BasePoseMatInv` `p+120`. Référence : `JAG2GLA.py` du plugin `jediacademy`.
- Les derniers chemins utilisés (GLA gauche, GLA droit, GLM, dossier d'export) sont mémorisés entre
  deux sessions.

## Panneaux latéraux (gauche = Reference, droite = Compared, identiques)

1. Le chemin du GLA et le bouton **Open**.
2. Trois onglets : **All Animations**, **Common Animations**, **Exclusive Animations**.
   - *Common* : le nom existe dans les deux cfg (comparaison sensible à la casse — mon choix).
   - *Exclusive* : le nom n'existe que de ce côté.
   - Avec un seul GLA chargé, tout est exclusif et rien n'est coloré.
3. La liste d'animations, colorée (voir Classement), avec la durée en frames à côté du nom.
4. Le bouton **Export XSI...** exporte **uniquement l'animation sélectionnée** de ce côté (pas d'export
   par lot). Il est grisé si aucune animation n'est sélectionnée de ce côté. Il ouvre une boîte
   « Enregistrer sous » placée dans le dernier dossier d'export, avec le nom pré-rempli
   `<NOM_ANIM>.xsi`. Le fichier est écrit à partir du GLA de ce côté, et le résultat (succès ou erreur)
   s'affiche dans le panneau. *(Mon choix : une boîte « Enregistrer sous » plutôt qu'un simple choix de
   dossier, pour pouvoir confirmer l'écrasement d'un fichier existant.)*

**Sélection** : cliquer une animation la sélectionne. Si une animation du même nom existe de l'autre
côté, elle y est sélectionnée aussi. Sinon, l'autre côté n'a plus de sélection.

## Classement et couleurs

Seuils **fixes** (constantes dans le code, pas de réglage dans l'interface) :

| Couleur | Règle |
|---|---|
| 🟢 Vert | Même nombre de frames et, pour chaque os commun à chaque frame, écart d'angle ≤ 0,5° et écart de position ≤ 0,05 unité. |
| 🟠 Orange | Pas vert, et écart moyen d'angle (sur tous les os et toutes les frames) < 10°. |
| 🔴 Rouge | Écart moyen d'angle ≥ 10°. |
| ⚪ Gris | Animation exclusive. |

Les quatre valeurs (0,5°, 0,05, 10°) sont des estimations à confirmer sur de vrais fichiers. Elles
sont regroupées dans une seule classe de constantes.

**Méthode de comparaison** *(mes choix)* :
- On compare les transformations des os **en espace modèle** (pose animée × pose de repos), pas les
  deltas stockés dans le GLA. Deux GLA avec des poses de repos différentes restent ainsi comparables.
- Les os sont appariés **par nom**. Les os présents d'un seul côté sont ignorés dans le calcul, mais
  signalés dans l'en-tête du tableau des différences.
- Écart d'angle = angle de la rotation relative entre les deux quaternions. Écart de position =
  distance entre les positions des os, en unités Ghoul2.
- Durées différentes : on rééchantillonne les deux animations sur `max(lenA, lenB)` frames en temps
  normalisé, avec interpolation (slerp/lerp, comme `GlaFile.Pose`).
- Toutes les animations communes sont comparées au chargement, en tâche de fond. Pendant le calcul,
  les animations restent non colorées.

## Zone centrale (quatre onglets)

Contrôles communs sous les vues 3D : Play/Pause, curseur de frame, numéro de frame / total, vitesse.
Les vues 3D gardent la caméra orbitale de Character Creator (gauche = orbite, droite = déplacement,
molette = zoom).

**Affichage 3D** : le squelette, en segments parent→enfant avec un point par os, couleur Reference ≠
couleur Compared. Une case **Show GLM** et un bouton **Load GLM...** (un seul GLM pour toute l'app)
affichent en plus le maillage, sans texture et en gris, animé par le GLA du côté concerné. Le GLM est
relié aux os par index, comme dans Character Creator. Si le nombre d'os ne correspond pas au GLA, le
maillage n'est pas affiché pour ce côté et un avertissement apparaît.

1. **Reference Animation** : l'animation sélectionnée à gauche, ou « No animation selected ».
2. **Differences (View)** : une seule vue 3D, une caméra commune. Les deux animations jouent en même
   temps, synchronisées en temps normalisé. Une **barre verticale déplaçable** coupe la vue : à gauche
   de la barre on voit la Reference, à droite la Compared. Si l'une des deux manque :
   « Select an animation present in both GLAs ».
3. **Differences (3D)** : en-tête avec les frames et fps de chaque côté, un avertissement si les
   durées diffèrent, et la liste des os présents d'un seul côté. Ensuite un tableau, une ligne par os
   au-dessus de la tolérance, trié par écart d'angle décroissant. Colonnes : Bone, Max angle (°),
   Frame, Max offset (units), Frame. Cliquer une ligne surligne l'os dans les vues 3D et place la
   lecture sur la frame du pic d'angle.
4. **Compared Animation** : comme l'onglet 1, pour la sélection de droite.

## Export XSI

Le fichier doit correspondre à ce qu'écrit `xsi3_blender_exporter.py`
(`%APPDATA%\Blender Foundation\Blender\5.1\scripts\addons\io_scene_dotXsi3\`), car c'est l'inverse
exact de l'importeur :
- En-tête `xsi 0300txt 0032`, puis `SI_FileInfo`, `SI_Scene <nom> { "FRAMES", 1, N, fps }`,
  `SI_CoordinateSystem`, `SI_Angle`, `SI_Ambience`.
- Un `SI_Model MDL-<os>` par os, imbriqués selon la hiérarchie. Chacun contient neuf `SI_FCurve`
  LINEAR (SCALING à 1, ROTATION en degrés Euler XYZ continus, TRANSLATION en local par rapport au
  parent), des clés numérotées de 1 à N, puis `SI_Transform SRT-` (local, frame 1),
  `SI_Transform BASEPOSE-` (pose de repos, en monde), `SI_Visibility` et `SI_Null`.
- Renommage : l'os `face` s'écrit `face_always_`.
- Espace : on applique les conventions des **valeurs par défaut** de l'importeur (échelle 0,64,
  décalage d'origine (0,0,-24), conversion Z-up/Y-up, axes d'os Ghoul2) pour qu'un import avec les
  options par défaut retombe exactement sur le GLA. Le bâtisseur reprend les formules de
  `Exporter.to_xsi` / `sample_strip`.
- Encodage latin-1, fins de ligne CRLF, nombres au format invariant (`%f`).
- Frames exportées : `start` à `start+length-1` du cfg ; fps du cfg (valeur absolue, 20 si nulle).

**Validation** : un test d'aller-retour, qui relit le XSI produit avec un petit parseur et compare au
GLA source. Puis une vérification manuelle dans Blender sur une ou deux animations.

## Mise en page

Fenêtre redimensionnable. Panneaux latéraux d'environ 300 px, redimensionnables ; la zone centrale
prend le reste. Titre : « SWGL Animation Extractor ».

## Laissé au bâtisseur

- Le style visuel exact (couleurs du squelette, des lignes de tableau, épaisseur de la barre A/B).
- Le découpage en classes et la façon de lancer la comparaison en tâche de fond.
- Le comportement de la caméra au changement d'animation (recadrage ou non).
