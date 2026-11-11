namespace SWGLAnimationExtractor;

public sealed record AnimDef(string Name, int Start, int Length, int Loop, float Fps);

/// <summary>animation.cfg: "name start length loop fps" lines; other lines (sex, soundpath...) are ignored.</summary>
public static class AnimationCfg
{
    public static List<AnimDef> Parse(string text)
    {
        var list = new List<AnimDef>();
        foreach (var raw in text.Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("//")) continue;
            var t = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (t.Length < 5) continue;
            if (!int.TryParse(t[1], out var start) || !int.TryParse(t[2], out var len) ||
                !int.TryParse(t[3], out var loop) ||
                !float.TryParse(t[4], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var fps))
                continue;
            list.Add(new AnimDef(t[0], start, Math.Max(len, 1), loop, fps));
        }
        return list;
    }
}
