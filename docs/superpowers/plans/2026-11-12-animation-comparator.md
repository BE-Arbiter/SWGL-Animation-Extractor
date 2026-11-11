# SWGL Animation Extractor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Une application de bureau MonoGame + ImGui qui charge deux GLA (avec leur `animation.cfg`),
classe et colore leurs animations selon leurs différences, les montre en 3D (seule, en A/B, en tableau)
et exporte l'animation sélectionnée en dotXSI 3.0.

**Architecture:** La logique pure (lecture GLA/cfg, comparaison, écriture XSI) vit dans des classes
sans dépendance graphique, testées en xUnit à partir de GLA synthétiques construits en mémoire.
L'interface (`Game1` partiel, ImGui) et le rendu (`SkeletonViewer`, render targets) consomment ces
classes. Le code réutilisable est copié depuis `C:\Users\lambe\source\repos\SWGL-Character-Creator`.

**Tech Stack:** C# / .NET 9, MonoGame.Framework.DesktopGL 3.8.*, ImGui.NET 1.89.7.1, xUnit 2.9.

**Spec:** `docs/superpowers/specs/2026-11-12-animation-comparator-design.md`

## Global Constraints

- Toute l'interface est en anglais ; on communique avec l'utilisateur en français.
- Namespace et assembly : `SWGLAnimationExtractor` ; titre de fenêtre `SWGL Animation Extractor`.
- Pas de lecture de `.pk3` ; GLA, cfg et GLM sont des fichiers sur le disque.
- `animation.cfg` (sans S) est cherché dans le dossier du `.gla`.
- Seuils fixes, dans `Thresholds` : 0,5° ; 0,05 unité ; écart moyen d'angle de 10°.
- XSI : en-tête `xsi 0300txt 0032`, latin-1, CRLF, nombres au format invariant `0.000000` (même
  sous une culture `fr-FR`), os `face` → `face_always_`, échelle 0,64, décalage d'origine (0,0,-24),
  conversion Z-up → Y-up.
- Export d'une seule animation à la fois (la sélection du côté concerné), pas d'export par lot.
- Le dossier n'est pas un dépôt git : **pas d'étape de commit**. L'utilisateur décidera s'il veut un
  `git init`.

## Review Focus

1. **Entrée du cfg qui dépasse le GLA** (`start+length > NumFrames`) : pas de plantage. La comparaison
   lit les frames bornées, et l'export refuse avec `Animation frames exceed the GLA (N frames).` —
   test dans la tâche 5.
2. **Nom d'animation en double dans un cfg** : on garde la première occurrence partout (liste, ensembles,
   comparaison) — test dans la tâche 3.
3. **Os présents d'un seul côté / hiérarchies différentes** : la comparaison porte sur les os communs
   et liste les autres, sans exception — test dans la tâche 4.
4. **Culture française de Windows** : le cfg (`20.5` fps) et le XSI (`0.640000`) restent au format
   invariant — tests dans les tâches 3 et 5.
5. **Animation d'une seule frame** (`length 1`) des deux côtés ou d'un seul : la comparaison et
   l'export fonctionnent (pas de division par zéro dans le temps normalisé) — tests dans les tâches 4
   et 5.

---

## Structure des fichiers

```
SWGLAnimationExtractor.csproj          projet (copie adaptée de SWGLCharacterCreator.csproj, sans StbImage*)
SWGLAnimationExtractor.slnx            solution : app + tests
Program.cs                             point d'entrée
app.manifest, Icon.ico, Icon.bmp       copiés de Character Creator
Settings.cs                            derniers chemins (JSON dans %APPDATA%\SWGLAnimationExtractor)
Gla/GlaFile.cs                         lecteur GLA (copié) + pose de repos + espace modèle
Gla/AnimationCfg.cs                    AnimDef + parseur cfg (extraits de GlaFile.cs de CC)
Gla/GlmModel.cs                        lecteur GLM (copié, sans SkinFile) + NumBones
Gla/GlaSide.cs                         un côté chargé : GLA + animations du cfg
Compare/AnimSets.cs                    animations communes / exclusives
Compare/Thresholds.cs                  seuils fixes
Compare/AnimComparer.cs                comparaison d'une paire, classement, BoneDiff
Compare/ComparisonStore.cs             comparaison de toutes les communes en tâche de fond
Export/EulerXyz.cs                     matrice → Euler XYZ continu (convention Blender)
Export/XsiWriter.cs                    écriture dotXSI 3.0
Ui/ImGuiRenderer.cs                    copié de CC
Ui/FileDialogs.cs                      Open / Save As natifs (comdlg32), résultats remis au thread UI
Ui/Playback.cs                         lecture partagée (temps normalisé)
Ui/SkeletonViewer.cs                   rendu squelette + GLM optionnel dans un render target
Game1.cs                               fenêtre, boucle, mise en page 3 colonnes
Game1.Sides.cs                         panneaux gauche/droite
Game1.Center.cs                        quatre onglets centraux
tests/SWGLAnimationExtractor.Tests/    projet xUnit (copie du csproj de test de CC)
  TestGla.cs                           constructeur de GLA synthétiques
  GlaFileTests.cs, GlaSideTests.cs, AnimSetsTests.cs, AnimComparerTests.cs,
  EulerXyzTests.cs, XsiWriterTests.cs, PlaybackTests.cs
```

---

### Task 1: Squelette du projet

**Files:**
- Create: `SWGLAnimationExtractor.csproj`, `SWGLAnimationExtractor.slnx`, `Program.cs`, `Settings.cs`,
  `Ui/ImGuiRenderer.cs`, `Game1.cs`, `app.manifest`, `Icon.ico`, `Icon.bmp`
- Create: `tests/SWGLAnimationExtractor.Tests/SWGLAnimationExtractor.Tests.csproj`,
  `tests/SWGLAnimationExtractor.Tests/SmokeTests.cs`

**Interfaces:**
- Produces: `Settings { string? LeftGla, RightGla, Glm, ExportDir; static Settings Load(); void Save(); }`
  (fichier `%APPDATA%\SWGLAnimationExtractor\settings.json`) ; `Game1 : Game` avec `_imgui`,
  `_settings` ; `ImGuiRenderer` inchangé (`BindTexture`, `UnbindTexture`, `BeginFrame`, `EndFrame`).

- [ ] **Step 1:** Copier csproj (retirer `StbImageSharp`/`StbImageWriteSharp`, `AssemblyName`
  `SWGLAnimationExtractor`, `<Compile Remove="tests/**" />` conservé), `app.manifest`, icônes,
  `ImGuiRenderer.cs` (namespace changé). Créer le projet de test sur le modèle de CC, référençant
  `../../SWGLAnimationExtractor.csproj`.
- [ ] **Step 2:** `Game1` minimal : fenêtre 1600×900 redimensionnable, titre `SWGL Animation Extractor`,
  une fenêtre ImGui plein écran sans décoration contenant une table 3 colonnes
  (`ImGuiTableFlags.Resizable | BordersInnerV`), colonnes gauche/droite à 300 px initiaux, centre en
  `WidthStretch`. Textes provisoires `Reference`, `Center`, `Compared`.
- [ ] **Step 3:** `SmokeTests.Settings_roundtrip_through_json` : sérialiser/désérialiser un `Settings`
  avec les quatre champs via `System.Text.Json` et vérifier l'égalité.
- [ ] **Step 4:** Run `dotnet build SWGLAnimationExtractor.slnx` puis `dotnet test` — Expected : build
  sans erreur, 1 test PASS. Lancer `dotnet run` : la fenêtre s'ouvre avec les trois colonnes.

---

### Task 2: Lecteur GLA avec pose de repos et espace modèle

**Files:**
- Create: `Gla/GlaFile.cs` (copie de CC), `Gla/AnimationCfg.cs` (`AnimDef`, `AnimationCfg` extraits)
- Test: `tests/SWGLAnimationExtractor.Tests/TestGla.cs`, `GlaFileTests.cs`

**Interfaces:**
- Produces:
  - `GlaBone { string Name; int Parent; bool IsFace; Matrix4x4 BasePose; }` — `BasePose` en convention
    ligne-vecteur (System.Numerics), comme les matrices de `Pose`.
  - `GlaFile.Parse(byte[]) : GlaFile`, `NumFrames`, `Bones`, `int FindBone(string name)` (−1 si absent,
    sensible à la casse), `IReadOnlyList<int> ParentFirstOrder` (exposer `_order`).
  - `Matrix4x4[] GlaFile.Pose(FrameSource src)` : surcharge où tous les os suivent `src`.
  - `Matrix4x4[] GlaFile.ModelSpace(FrameSource src)` : matrice monde de chaque os
    = `BasePose * Pose(src)[i]` (ligne-vecteur). Translation = position de l'os.
  - `Matrix4x4[] GlaFile.RestModelSpace()` : `BasePose` de chaque os.
  - `record AnimDef(string Name, int Start, int Length, int Loop, float Fps)` et
    `AnimationCfg.Parse(string) : List<AnimDef>` inchangés.
  - Tests : `TestGla.Build(string[] names, int[] parents, Matrix4x4[] basePoses, (Quaternion q, Vector3 t)[,] frames) : byte[]`
    (`frames[frame, bone]`, un élément de pool par couple, sans dédoublonnage).

Format à écrire dans `TestGla` (et à lire) : en-tête 100 octets — `"2LGA"`, version `6` @4, nom @8
(64), scale @72, `numFrames` @76, `ofsFrames` @80, `numBones` @84, `ofsCompBonePool` @88,
`ofsSkel` @92, `ofsEnd` @96. Table d'offsets d'os (int, relatifs à 100) à partir de 100. Entrée d'os :
nom (64), flags @64, parent @68, `BasePoseMat` 3×4 floats ligne par ligne @72 (convention
colonne-vecteur : translation en colonne 3), `BasePoseMatInv` @120, `numChildren` @168, puis les
index des enfants. Frames : 3 octets par (frame, os). Pool : 7 `ushort` `w,x,y,z,tx,ty,tz`, avec
`u = round((q + 2) * 16383)` et `u = round((t + 512) * 64)`.

- [ ] **Step 1: Tests qui échouent** (`GlaFileTests`)
  - `BasePose_is_read_transposed_to_row_vector` : os unique avec `BasePoseMat` translation (1,2,3) et
    rotation de 90° autour de Z → `Bones[0].BasePose.Translation == (1,2,3)` et
    `Vector3.Transform(UnitX, BasePose)` ≈ `UnitY` (1e-4).
  - `ModelSpace_composes_delta_with_base_pose` : racine (repos à l'origine) + enfant (repos en
    (10,0,0)) ; frame 0 : racine tournée de 90° autour de Z, delta enfant identité →
    `ModelSpace(new FrameSource(0,1,0))[1].Translation` ≈ (0,10,0) (tolérance 0,02 à cause de la
    quantification).
  - `FindBone_returns_index_or_minus_one`.
- [ ] **Step 2:** Run `dotnet test --filter GlaFileTests` — Expected : FAIL (membres inexistants).
- [ ] **Step 3:** Implémenter la lecture de `BasePose` (transposer la 3×4 en `Matrix4x4` ligne-vecteur),
  `FindBone`, `Pose(FrameSource)`, `ModelSpace`, `RestModelSpace`.
- [ ] **Step 4:** Run `dotnet test --filter GlaFileTests` — Expected : PASS.

---

### Task 3: Chargement d'un côté (GLA + cfg)

**Files:**
- Create: `Gla/GlaSide.cs`
- Test: `tests/SWGLAnimationExtractor.Tests/GlaSideTests.cs`

**Interfaces:**
- Consumes : `GlaFile.Parse`, `AnimationCfg.Parse`.
- Produces : `sealed class GlaSide { string GlaPath; GlaFile Gla; IReadOnlyList<AnimDef> Anims; AnimDef? Find(string name); static GlaSide Load(string glaPath); }`
  — lève `GlaLoadException(string message)` pour toute erreur affichable.
  `Anims` suit l'ordre du cfg, **sans doublons de nom** (première occurrence gardée).

- [ ] **Step 1: Tests qui échouent** (fichiers dans un dossier temporaire)
  - `Load_reads_cfg_next_to_gla` : `test.gla` + `animation.cfg` avec 2 lignes → `Anims.Count == 2`.
  - `Load_without_cfg_throws` : message exact `animation.cfg not found next to the GLA.`
  - `Load_invalid_gla_throws` : octets aléatoires → message `Not a GLA file.`
  - `Duplicate_names_keep_first` : cfg `BOTH_STAND1 0 10 -1 20` puis `BOTH_STAND1 50 5 -1 20` →
    une seule entrée, `Start == 0`.
  - `Cfg_fps_parsed_invariant_under_fr_culture` : `CultureInfo.CurrentCulture = fr-FR`, ligne avec
    `20.5` → `Fps == 20.5f`.
- [ ] **Step 2:** Run `dotnet test --filter GlaSideTests` — Expected : FAIL.
- [ ] **Step 3:** Implémenter `GlaSide.Load` (`InvalidDataException` du parseur → `GlaLoadException`).
- [ ] **Step 4:** Run `dotnet test --filter GlaSideTests` — Expected : PASS.

---

### Task 4: Ensembles et comparaison

**Files:**
- Create: `Compare/AnimSets.cs`, `Compare/Thresholds.cs`, `Compare/AnimComparer.cs`, `Compare/ComparisonStore.cs`
- Test: `AnimSetsTests.cs`, `AnimComparerTests.cs`

**Interfaces:**
- Consumes : `GlaSide`, `GlaFile.ModelSpace`, `GlaFile.FindBone`, `FrameSource`.
- Produces :
  - `static class Thresholds { const float AngleDeg = 0.5f, Offset = 0.05f, MajorMeanAngleDeg = 10f; }`
  - `enum Classification { Pending, Identical, Minor, Major, Exclusive }`
  - `static class AnimSets { IReadOnlyList<AnimDef> Common(GlaSide side, GlaSide? other); IReadOnlyList<AnimDef> Exclusive(GlaSide side, GlaSide? other); }`
    — avec `other == null`, tout est exclusif. Ordre du cfg de `side`.
  - `record BoneDiff(string Bone, float MaxAngleDeg, int AngleFrame, float MaxOffset, int OffsetFrame)`
    — frames dans l'échelle rééchantillonnée.
  - `sealed record AnimComparison(Classification Class, int FramesA, int FramesB, float FpsA, float FpsB, int Samples, float MeanAngleDeg, IReadOnlyList<BoneDiff> Bones, IReadOnlyList<string> OnlyInA, IReadOnlyList<string> OnlyInB)`
    — `Bones` : os dépassant l'une des tolérances, triés par `MaxAngleDeg` décroissant.
  - `static AnimComparison AnimComparer.Compare(GlaFile a, AnimDef da, GlaFile b, AnimDef db)`
  - `static FrameSource AnimComparer.Sample(AnimDef d, int i, int samples)` — temps normalisé
    `t = samples == 1 ? 0 : i / (samples - 1f)`, `Frame = t * (d.Length - 1)`, `Count = d.Length`,
    `First = d.Start`.
  - `sealed class ComparisonStore { void Start(GlaSide left, GlaSide right); void Clear(); Classification Get(string name); AnimComparison? Result(string name); }`
    — calcule en `Task.Run` toutes les communes, résultats dans un `ConcurrentDictionary`, compteur de
    génération pour ignorer un calcul périmé ; `Get` renvoie `Pending` tant que non calculé.

Algorithme : `samples = max(da.Length, db.Length)`. Pour chaque échantillon, matrices monde des deux
côtés ; pour chaque os commun (par nom) : angle = `2·acos(|dot(qA, qB)|)` en degrés (quaternions des
matrices monde, bornés à 1), offset = distance des translations. `MeanAngleDeg` = moyenne sur tous
les (os commun, échantillon). Classement : `Identical` si `FramesA == FramesB` et aucun dépassement
de tolérance ; sinon `Major` si `MeanAngleDeg >= 10`, sinon `Minor`.

- [ ] **Step 1: Tests qui échouent**
  - `AnimSetsTests.Common_and_exclusive_split_by_name` : gauche {A,B,C}, droite {B,C,D} → communes
    gauche {B,C}, exclusives gauche {A}, exclusives droite {D} ; `other == null` → tout exclusif.
  - `Identical_animations_are_identical` : même GLA des deux côtés → `Identical`, `Bones` vide.
  - `Same_motion_different_length_is_minor` : A = rotation racine 0→20° sur 3 frames, B = même
    rotation sur 5 frames → `Minor` (longueurs différentes, écart moyen < 10°).
  - `Small_rotation_change_is_minor` : un os décalé de 5° sur toutes les frames → `Minor`,
    `Bones[0].MaxAngleDeg` ≈ 5 (±0,2).
  - `Large_rotation_change_is_major` : racine tournée de 90° → `Major`.
  - `Sub_tolerance_noise_is_identical` : écart de 0,2° → `Identical`.
  - `Bones_only_on_one_side_are_listed` : B a un os `extra` → `OnlyInB == ["extra"]`, pas d'exception.
  - `Single_frame_animations_compare` : `length 1` des deux côtés, puis 1 contre 4 → pas d'exception,
    `Samples == 1` puis `4`.
  - `Peak_frame_is_reported` : écart seulement à l'échantillon 2 → `AngleFrame == 2`.
- [ ] **Step 2:** Run `dotnet test --filter "AnimSetsTests|AnimComparerTests"` — Expected : FAIL.
- [ ] **Step 3:** Implémenter `Thresholds`, `AnimSets`, `AnimComparer`, puis `ComparisonStore`.
- [ ] **Step 4:** Run `dotnet test --filter "AnimSetsTests|AnimComparerTests"` — Expected : PASS.

---

### Task 5: Écriture dotXSI 3.0

**Files:**
- Create: `Export/EulerXyz.cs`, `Export/XsiWriter.cs`
- Test: `EulerXyzTests.cs`, `XsiWriterTests.cs`

**Interfaces:**
- Consumes : `GlaFile.ModelSpace`, `RestModelSpace`, `ParentFirstOrder`, `Bones`, `AnimDef`.
- Produces :
  - `static Vector3 EulerXyz.FromMatrix(Matrix4x4 rotationRowVector, Vector3? previous)` — radians,
    convention Blender `XYZ` (R = Rz·Ry·Rx en colonne-vecteur), et si `previous` est donné, la
    solution la plus proche (équivalent de `to_euler('XYZ', compat)` : ajuster chaque axe de ±2π vers
    `previous`, comparer avec la solution alternative `(x+π, π−y, z+π)` et garder la plus proche).
  - `static string XsiWriter.Build(GlaFile gla, AnimDef anim, string sceneName)` — texte complet avec
    `\r\n`.
  - `static void XsiWriter.Write(string path, GlaFile gla, AnimDef anim)` — `sceneName` = nom du
    fichier sans extension, espaces → `_` ; encodage latin-1 (`Encoding.Latin1`).
  - Lève `XsiExportException("Animation frames exceed the GLA (N frames).")` si
    `anim.Start + anim.Length > gla.NumFrames`.

Espace XSI (inverse de l'importeur aux réglages par défaut). En colonne-vecteur, avec `G` la matrice
monde Ghoul2 d'un os :
`X = rigid(C⁻¹ · T(0,0,+24) · G · C)`, où `C = 0,64 · R` et `R` envoie (x,y,z) sur (x, −z, y).
Hypothèse : l'objet `skeleton_root` du plugin a une transformation identité. C'est vérifié à la main
dans Blender (tâche 8).

Contenu — reproduire la mise en forme de `xsi3_blender_exporter.py`
(`%APPDATA%\Blender Foundation\Blender\5.1\scripts\addons\io_scene_dotXsi3\`) :
- `SI_FileInfo` : nom de scène, `"SWGL Animation Extractor"`, date `ddd MMM dd HH:mm:ss yyyy`
  (culture invariante), `"SWGL Animation Extractor"`.
- `SI_Scene <scène> { "FRAMES", 1.000000, N, fps }` avec `fps = |anim.Fps|`, 20 si < 0,01.
- `SI_CoordinateSystem coord { 1, 0, 1, 0, 2, 5 }`, `SI_Angle { 0 }`, `SI_Ambience { 0, 0, 0 }`.
- Os en profondeur d'abord (racines puis enfants, dans l'ordre des index), un `SI_Model MDL-<nom>` par
  os, indenté d'une tabulation par niveau ; neuf `SI_FCurve <nom>-<PARAM>` (SCALING-X/Y/Z = 1,
  ROTATION en degrés, TRANSLATION, local = `parent_monde⁻¹ · monde`), en-tête
  `"<nom>", "<PARAM>", "LINEAR", 1, 1, N`, clés `"%f,%f,"` avec frames 1..N ; puis
  `SI_Transform SRT-<nom>` (local frame 1) et `SI_Transform BASEPOSE-<nom>` (monde au repos), 9 valeurs
  `1,1,1, rx,ry,rz (degrés), tx,ty,tz` ; puis `SI_Visibility { 1 }` et `SI_Null <nom> { }`.
- Nom d'os `face` écrit `face_always_`.

- [ ] **Step 1: Tests qui échouent**
  - `EulerXyzTests.Rotation_about_z_90` → (0,0,π/2) ±1e-4.
  - `EulerXyzTests.Continuity_keeps_angles_near_previous` : rotation Z de −170° avec `previous`
    (0,0,170°) → z ≈ 190°.
  - `XsiWriterTests.Header_and_scene` : commence par `xsi 0300txt 0032\r\n`, contient
    `SI_Scene ` et `"FRAMES"`, toutes les fins de ligne sont `\r\n`.
  - `One_model_and_nine_fcurves_per_bone` : 3 os → 3 `SI_Model MDL-`, 27 `SI_FCurve`, chaque
    FCurve annonce `N` = `anim.Length` clés.
  - `Face_bone_is_renamed` : os `face` → `MDL-face_always_`, aucun `MDL-face {`.
  - `Basepose_uses_xsi_space` : racine au repos en (6.4, 12.8, 8), rotation identité → bloc
    `BASEPOSE-root` se termine par les translations `10.000000`, `50.000000`, `-20.000000`.
  - `Numbers_invariant_under_fr_culture` : sous `fr-FR`, aucune virgule décimale (`0,`) dans les
    valeurs numériques ; `0.640000`-style uniquement.
  - `Out_of_range_animation_throws` : `start 8 length 5` sur un GLA de 10 frames → message exact.
  - `Single_frame_animation_exports` : `length 1` → FCurves à 1 clé, pas d'exception.
- [ ] **Step 2:** Run `dotnet test --filter "EulerXyzTests|XsiWriterTests"` — Expected : FAIL.
- [ ] **Step 3:** Implémenter `EulerXyz` puis `XsiWriter` (`StringBuilder`, `CultureInfo.InvariantCulture`
  partout, `F6`).
- [ ] **Step 4:** Run `dotnet test --filter "EulerXyzTests|XsiWriterTests"` — Expected : PASS.

---

### Task 6: Panneaux latéraux (chargement, onglets, couleurs, sélection, export)

**Files:**
- Create: `Ui/FileDialogs.cs`, `Game1.Sides.cs`
- Modify: `Game1.cs` (état des côtés, file d'actions UI, appel des panneaux)

**Interfaces:**
- Consumes : `GlaSide.Load`, `AnimSets`, `ComparisonStore`, `XsiWriter.Write`, `Settings`.
- Produces :
  - `FileDialogs.Open(string filter, string title, string? initialPath, Action<string?> done)` et
    `FileDialogs.Save(string filter, string title, string? initialDir, string defaultName, string defExt, Action<string?> done)`
    (`GetOpenFileNameW` / `GetSaveFileNameW`, thread STA, `OFN_OVERWRITEPROMPT` pour Save).
  - Dans `Game1` : `void RunOnUi(Action a)` (une `ConcurrentQueue<Action>` vidée dans `Update`) ;
    `SideState { GlaSide? Side; string Path; string? Error; string? Status; AnimDef? Selected; int Tab; }`
    pour `_left` et `_right` ; `event`-libre : la sélection appelle `Select(SideState s, AnimDef a)`.

Comportement (textes exacts) :
- Ligne 1 : `InputText` du chemin (Entrée = charger) + bouton `Open` (filtre `GLA files (*.gla)`).
  Erreur de chargement affichée en rouge sous la ligne.
- Après chaque chargement réussi : enregistrer `Settings`, puis `ComparisonStore.Start` si les deux
  côtés sont chargés, sinon `Clear`. Au démarrage, recharger `LeftGla`/`RightGla` s'ils existent.
- Onglets `All Animations`, `Common Animations`, `Exclusive Animations` ; liste
  `Selectable` « `NAME (N)` » où N = nombre de frames.
- Couleurs du texte : `Identical` (0.35,0.85,0.35), `Minor` (1.0,0.62,0.15), `Major` (0.95,0.3,0.3),
  `Exclusive` (0.6,0.6,0.6) — seulement si les deux côtés sont chargés ; `Pending` = couleur par défaut.
- Sélectionner une animation sélectionne la même (par nom) de l'autre côté, sinon désélectionne
  l'autre côté.
- Bouton `Export XSI...` en bas, désactivé (`BeginDisabled`) sans sélection ; `Save` dans
  `Settings.ExportDir`, nom `<NAME>.xsi`, filtre `dotXSI files (*.xsi)` ; succès :
  `Exported <file>` en vert ; erreur : message de l'exception en rouge ; `ExportDir` mis à jour.

- [ ] **Step 1:** Implémenter `FileDialogs` (à partir de `FilePicker.cs` de CC) et `RunOnUi`.
- [ ] **Step 2:** Implémenter `Game1.Sides.cs` selon le comportement ci-dessus.
- [ ] **Step 3:** Run `dotnet build` — Expected : 0 erreur. Run `dotnet test` — Expected : tout PASS.
- [ ] **Step 4:** Vérification manuelle (`dotnet run`) avec deux GLA réels de l'utilisateur : listes
  remplies, couleurs apparaissant après le calcul, sélection synchronisée, export d'un fichier qui
  s'ouvre dans un éditeur avec l'en-tête attendu.

---

### Task 7: Vues 3D et lecture (Reference / Compared, GLM)

**Files:**
- Create: `Ui/Playback.cs`, `Ui/SkeletonViewer.cs`, `Gla/GlmModel.cs` (copie de CC sans `SkinFile`),
  `Game1.Center.cs`
- Test: `PlaybackTests.cs`

**Interfaces:**
- Consumes : `GlaFile.ModelSpace`, `GlaFile.Pose(FrameSource)`, `AnimComparer.Sample`-équivalent
  pour le temps normalisé, `GlmModel.Parse`, `ImGuiRenderer`.
- Produces :
  - `sealed class Playback { float Time01; bool Playing = true; float Speed = 1; void Update(double dtSeconds, float durationSeconds); FrameSource Frame(AnimDef d); int FrameIndex(AnimDef d); }`
    — boucle sur [0,1) ; `durationSeconds ≤ 0` ne fait pas avancer ; `Frame(d)` = `Time01 * d.Length`
    borné à `Length - 1`.
  - `GlmModel.NumBones` (int lu à l'offset 140 de l'en-tête GLM).
  - `sealed class SkeletonViewer { SkeletonViewer(GraphicsDevice gd, ImGuiRenderer ui); Camera (orbite/pan/zoom repris de ModelViewer : Orbit, Pan, Zoom); IntPtr Render(int w, int h, GlaFile? gla, FrameSource? frame, Color boneColor, int highlightBone, GlmModel? glm); string? GlmWarning; }`
    — dessine des `LineList` parent→enfant entre les translations de `ModelSpace`, une petite croix
    par os, l'os surligné en jaune (1,0.9,0.2). Si `glm != null` et `glm.NumBones == gla.Bones.Count`,
    il dessine aussi le maillage gris non texturé, skinné avec `Pose(frame)` comme `ModelViewer.Skin` ;
    sinon il remplit `GlmWarning = "GLM bone count (X) does not match the GLA (Y)."`.
    Pas de `FrameSource` → pose de repos (`RestModelSpace`).
  - Couleurs : Reference (0.35,0.65,1.0), Compared (1.0,0.55,0.25).

Zone centrale : onglets `Reference Animation`, `Differences (View)`, `Differences (3D)`,
`Compared Animation`. Au-dessus des onglets : case `Show GLM` et bouton `Load GLM...` (enregistré
dans `Settings.Glm`, rechargé au démarrage). Sous la vue : `Play`/`Pause`, `SliderFloat` du temps,
`Frame i / N`, `SliderFloat` `Speed` 0,1–2. Durée = `Length / |Fps|` (20 fps si 0) de l'animation
affichée (Reference en priorité dans les onglets de différences). Onglets 1 et 4 : sans sélection,
texte `No animation selected` à la place de la vue. Glisser dans l'image = orbite, clic droit = pan,
molette = zoom.

- [ ] **Step 1: Tests qui échouent** (`PlaybackTests`)
  - `Update_advances_and_wraps` : durée 2 s, `Update(1.5)` deux fois → `Time01` ≈ 0,5.
  - `Paused_does_not_advance`.
  - `Zero_duration_does_not_advance`.
  - `Frame_is_clamped_to_last` : `Time01 = 0.9999`, `Length 10` → `FrameIndex == 9`.
- [ ] **Step 2:** Run `dotnet test --filter PlaybackTests` — Expected : FAIL.
- [ ] **Step 3:** Implémenter `Playback`. Run `dotnet test --filter PlaybackTests` — Expected : PASS.
- [ ] **Step 4:** Implémenter `SkeletonViewer`, `GlmModel.NumBones` et les onglets 1 et 4 dans
  `Game1.Center.cs`.
- [ ] **Step 5:** Run `dotnet build` — 0 erreur. Vérification manuelle : un squelette animé dans les
  onglets 1 et 4 ; `Load GLM...` avec un GLM compatible → maillage gris animé ; avec un GLM
  incompatible → avertissement, squelette seul.

---

### Task 8: Onglets de différences et vérification finale

**Files:**
- Modify: `Game1.Center.cs`, `Ui/SkeletonViewer.cs` (deuxième instance ou deuxième render target)

**Interfaces:**
- Consumes : `ComparisonStore.Result`, `AnimComparison`, `BoneDiff`, `SkeletonViewer`, `Playback`.

**Differences (View)** : deux rendus de même taille et **même caméra** (Reference avec sa couleur,
Compared avec la sienne), aux frames `AnimComparer.Sample`-équivalentes au même `Time01`. On les
compose avec `ImDrawList.AddImage` : la Reference avec les UV `[0, s]` à gauche de la barre, la
Compared avec les UV `[s, 1]` à droite. Barre verticale de 3 px déplaçable à la souris
(`s` ∈ [0,1], 0,5 au départ), avec les étiquettes `Reference` / `Compared` en haut de chaque côté.
Si l'une des sélections manque : `Select an animation present in both GLAs`.

**Differences (3D)** : en-tête `Reference: N frames @ F fps` / `Compared: N frames @ F fps`,
avertissement orange `Lengths differ: compared over S resampled frames.` si besoin, puis
`Bones only in Reference: ...` / `Bones only in Compared: ...` si non vides. Pendant le calcul :
`Comparing...`. Tableau `Bone | Max angle (°) | Frame | Max offset (units) | Frame` (2 décimales),
une ligne par `BoneDiff`, ou `No bone differs beyond tolerance.` Cliquer une ligne :
`highlightBone` = cet os dans toutes les vues, et `Time01 = AngleFrame / max(Samples − 1, 1)`, lecture
en pause.

- [ ] **Step 1:** Implémenter l'onglet `Differences (View)`.
- [ ] **Step 2:** Implémenter l'onglet `Differences (3D)`.
- [ ] **Step 3:** Run `dotnet build` puis `dotnet test` — Expected : 0 erreur, tous les tests PASS.
- [ ] **Step 4:** Vérification manuelle complète avec les GLA de l'utilisateur : barre A/B qui coupe
  correctement la vue, tableau trié, clic → os surligné et lecture sur la bonne frame.
- [ ] **Step 5:** Aller-retour Blender (fait par l'utilisateur) : importer un `.xsi` exporté avec le
  plugin « JA dotXSI NLA Import/Export » aux réglages par défaut sur le squelette correspondant, et
  vérifier que l'animation se superpose à celle du GLA (position, orientation, échelle, nombre de
  frames). En cas d'écart, revoir l'hypothèse `skeleton_root` = identité de la tâche 5.
