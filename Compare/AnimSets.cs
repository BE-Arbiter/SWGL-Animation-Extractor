namespace SWGLAnimationExtractor;

public static class AnimSets
{
    /// <summary>Animations of <paramref name="side"/> whose name also exists on <paramref name="other"/>, in cfg order.</summary>
    public static IReadOnlyList<AnimDef> Common(GlaSide side, GlaSide? other) =>
        other == null ? Array.Empty<AnimDef>() : side.Anims.Where(a => other.Find(a.Name) != null).ToList();

    /// <summary>Animations of <paramref name="side"/> missing from <paramref name="other"/>, in cfg order.</summary>
    public static IReadOnlyList<AnimDef> Exclusive(GlaSide side, GlaSide? other) =>
        other == null ? side.Anims.ToList() : side.Anims.Where(a => other.Find(a.Name) == null).ToList();
}
