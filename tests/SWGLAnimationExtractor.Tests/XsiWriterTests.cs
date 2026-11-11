using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using Xunit;

namespace SWGLAnimationExtractor.Tests;

public class XsiWriterTests
{
    private static GlaFile Gla(string[] names, int[] parents, Matrix4x4[]? rest = null, int frames = 10)
    {
        var f = new (Quaternion, Vector3)[frames, names.Length];
        for (int i = 0; i < frames; i++)
            for (int b = 0; b < names.Length; b++)
                f[i, b] = (Quaternion.CreateFromAxisAngle(Vector3.UnitZ, i * 0.1f), Vector3.Zero);
        rest ??= names.Select(_ => Matrix4x4.Identity).ToArray();
        return GlaFile.Parse(TestGla.Build(names, parents, rest, f));
    }

    private static string Build(GlaFile g, AnimDef a) => XsiWriter.Build(g, a, "scene");
    private static AnimDef Def(int start, int len) => new("A", start, len, -1, 20f);
    private static GlaFile Three() => Gla(new[] { "root", "a", "b" }, new[] { -1, 0, 1 });

    [Fact]
    public void Header_and_scene()
    {
        var s = Build(Three(), Def(0, 5));
        Assert.StartsWith("xsi 0300txt 0032\r\n", s);
        Assert.Contains("SI_Scene ", s);
        Assert.Contains("\"FRAMES\"", s);
        Assert.DoesNotMatch(new Regex("(?<!\r)\n"), s);
    }

    [Fact]
    public void One_model_and_nine_fcurves_per_bone()
    {
        var s = Build(Three(), Def(0, 5));
        Assert.Equal(3, Regex.Matches(s, "SI_Model MDL-").Count);
        Assert.Equal(27, Regex.Matches(s, "SI_FCurve ").Count);
        Assert.Equal(27, Regex.Matches(s, "\"LINEAR\", \r\n\t*1, \r\n\t*1, \r\n\t*5, \r\n").Count);
    }

    [Fact]
    public void Face_bone_is_renamed()
    {
        var s = Build(Gla(new[] { "root", "face" }, new[] { -1, 0 }), Def(0, 3));
        Assert.Contains("MDL-face_always_", s);
        Assert.DoesNotContain("MDL-face {", s);
    }

    [Fact]
    public void Basepose_uses_xsi_space()
    {
        var rest = new[] { Matrix4x4.CreateTranslation(6.4f, 12.8f, 8f) };
        var s = Build(Gla(new[] { "root" }, new[] { -1 }, rest), Def(0, 2));
        int i = s.IndexOf("SI_Transform BASEPOSE-root", StringComparison.Ordinal);
        Assert.True(i >= 0);
        var block = s.Substring(i, s.IndexOf('}', i) - i);
        var vals = Regex.Matches(block, @"-?\d+\.\d+").Select(m => m.Value).ToArray();
        Assert.Equal(new[] { "10.000000", "50.000000", "-20.000000" }, vals[^3..]);
    }

    [Fact]
    public void Numbers_invariant_under_fr_culture()
    {
        var old = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
        try
        {
            var s = Build(Three(), Def(0, 5));
            Assert.DoesNotMatch(new Regex(@"\d,\d{6}\b"), s);
            Assert.Contains("1.000000", s);
        }
        finally { CultureInfo.CurrentCulture = old; }
    }

    [Fact]
    public void Out_of_range_animation_throws()
    {
        var ex = Assert.Throws<XsiExportException>(() => Build(Three(), Def(8, 5)));
        Assert.Equal("Animation frames exceed the GLA (10 frames).", ex.Message);
    }

    [Fact]
    public void Single_frame_animation_exports()
    {
        var s = Build(Three(), Def(0, 1));
        Assert.Equal(27, Regex.Matches(s, "SI_FCurve ").Count);
        Assert.Equal(27, Regex.Matches(s, "\"LINEAR\", \r\n\t*1, \r\n\t*1, \r\n\t*1, \r\n").Count);
    }

    [Fact]
    public void Write_creates_latin1_file_named_after_scene()
    {
        var path = Path.Combine(Path.GetTempPath(), "my anim " + Guid.NewGuid().ToString("N") + ".xsi");
        try
        {
            XsiWriter.Write(path, Three(), Def(0, 2));
            var text = File.ReadAllText(path, System.Text.Encoding.Latin1);
            Assert.StartsWith("xsi 0300txt 0032\r\n", text);
            Assert.Contains("SI_Scene my_anim_", text);
        }
        finally { File.Delete(path); }
    }
}
