namespace SWGLAnimationExtractor;

public static class AnimSets
{
    /// <summary>Animations of <paramref name="side"/> whose name also exists on <paramref name="other"/>, in cfg order.</summary>
    public static IReadOnlyList<AnimDef> Common(GlaSide side, GlaSide? other) =>
        other == null ? Array.Empty<AnimDef>() : side.Anims.Where(a => other.Find(a.Name) != null).ToList();

    /// <summary>Animations of <paramref name="side"/> missing from <paramref name="other"/>, in cfg order.</summary>
    public static IReadOnlyList<AnimDef> Exclusive(GlaSide side, GlaSide? other) =>
        other == null ? side.Anims.ToList() : side.Anims.Where(a => other.Find(a.Name) == null).ToList();

    /// <summary>
    /// Animations of <paramref name="side"/> that differ from <paramref name="other"/>, in cfg order: missing from the
    /// other side, or present on both with a Major (red) comparison.
    /// </summary>
    public static IReadOnlyList<AnimDef> Different(GlaSide side, GlaSide? other, Func<string, Classification> classify) =>
        other == null ? side.Anims.ToList() : side.Anims.Where(a => other.Find(a.Name) == null ||
            classify(a.Name) == Classification.Major).ToList();

    /// <summary>
    /// Animations of <paramref name="list"/> whose name contains every whitespace-separated term of
    /// <paramref name="filter"/> (case-insensitive), in order. A blank filter keeps everything.
    /// </summary>
    public static IReadOnlyList<AnimDef> Filter(IReadOnlyList<AnimDef> list, string? filter)
    {
        var terms = (filter ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length == 0) return list;
        return list.Where(a => terms.All(t => a.Name.Contains(t, StringComparison.OrdinalIgnoreCase))).ToList();
    }
}
