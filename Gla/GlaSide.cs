namespace SWGLAnimationExtractor;

public sealed class GlaLoadException : Exception
{
    public GlaLoadException(string message) : base(message) { }
}

/// <summary>One side of the comparison: a GLA skeleton plus the animation.cfg next to it.</summary>
public sealed class GlaSide
{
    private readonly Dictionary<string, AnimDef> _byName;

    public string GlaPath { get; }
    public GlaFile Gla { get; }
    public IReadOnlyList<AnimDef> Anims { get; }

    private GlaSide(string glaPath, GlaFile gla, List<AnimDef> anims)
    {
        GlaPath = glaPath;
        Gla = gla;
        Anims = anims;
        _byName = anims.ToDictionary(a => a.Name, a => a, StringComparer.OrdinalIgnoreCase);
    }

    public AnimDef? Find(string name) => _byName.TryGetValue(name, out var a) ? a : null;

    public static GlaSide Load(string glaPath)
    {
        try
        {
            var cfgPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(glaPath)) ?? "", "animation.cfg");
            if (!File.Exists(cfgPath)) throw new GlaLoadException("animation.cfg not found next to the GLA.");
            var gla = GlaFile.Parse(File.ReadAllBytes(glaPath));
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var anims = AnimationCfg.Parse(File.ReadAllText(cfgPath)).Where(a => seen.Add(a.Name)).ToList();
            return new GlaSide(glaPath, gla, anims);
        }
        catch (InvalidDataException e) { throw new GlaLoadException(e.Message); }
        catch (IOException e) { throw new GlaLoadException(e.Message); }
        catch (UnauthorizedAccessException e) { throw new GlaLoadException(e.Message); }
    }
}
