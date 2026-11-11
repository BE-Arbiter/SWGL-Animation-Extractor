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

    private static float[] Block(string s, string header)
    {
        int i = s.IndexOf(header, StringComparison.Ordinal);
        Assert.True(i >= 0, header);
        var block = s.Substring(i, s.IndexOf('}', i) - i);
        return Regex.Matches(block, @"-?\d+\.\d+").Select(m => float.Parse(m.Value, CultureInfo.InvariantCulture)).ToArray();
    }

    private static float[] Keys(string s, string fcurve)
    {
        int i = s.IndexOf("SI_FCurve " + fcurve + " {", StringComparison.Ordinal);
        Assert.True(i >= 0, fcurve);
        var block = s.Substring(i, s.IndexOf('}', i) - i);
        return Regex.Matches(block, @"^	*\d+\.\d{6},(-?\d+\.\d{6}),?$", RegexOptions.Multiline)
            .Select(m => float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToArray();
    }

    private static GlaFile Static(string[] names, int[] parents, Matrix4x4[] rest, int frames = 3)
    {
        var f = new (Quaternion, Vector3)[frames, names.Length];
        for (int i = 0; i < frames; i++)
            for (int b = 0; b < names.Length; b++) f[i, b] = (Quaternion.Identity, Vector3.Zero);
        return GlaFile.Parse(TestGla.Build(names, parents, rest, f));
    }

    private static readonly float D = 180f / MathF.PI;

    [Fact]
    public void Basepose_uses_xsi_space_without_lift()
    {
        var rest = new[] { Matrix4x4.CreateTranslation(6.4f, 12.8f, 8f) };
        var s = Build(Static(new[] { "root" }, new[] { -1 }, rest), Def(0, 2));
        var vals = Regex.Matches(s.Substring(s.IndexOf("SI_Transform BASEPOSE-root", StringComparison.Ordinal)), @"-?\d+\.\d+")
            .Select(m => m.Value).Take(9).ToArray();
        Assert.Equal(new[] { "10.000000", "12.500000", "-20.000000" }, vals[^3..]);
    }

    [Fact]
    public void Animated_frame_gets_origin_lift()
    {
        var rest = new[] { Matrix4x4.CreateTranslation(6.4f, 12.8f, 8f) };
        var s = Build(Static(new[] { "root" }, new[] { -1 }, rest), Def(0, 2));
        Assert.Equal(50f, Keys(s, "root-TRANSLATION-Y")[0], 1e-3f);
        var srt = Block(s, "SI_Transform SRT-root");
        Assert.Equal(new[] { 10f, 50f, -20f }, srt[^3..].Select(v => MathF.Round(v, 3)).ToArray());
    }

    [Fact]
    public void Rest_rotation_goes_through_axis_conversion()
    {
        // Ghoul2 Z rotation becomes a rotation about XSI Y (R sends z to y); Ghoul2 X stays X.
        var z = Block(Build(Static(new[] { "r" }, new[] { -1 }, new[] { Matrix4x4.CreateRotationZ(30 / D) }), Def(0, 1)), "SI_Transform BASEPOSE-r");
        Assert.Equal(new[] { 0f, 30f, 0f }, z[3..6].Select(v => MathF.Round(v, 2) + 0f).ToArray());
        var x = Block(Build(Static(new[] { "r" }, new[] { -1 }, new[] { Matrix4x4.CreateRotationX(30 / D) }), Def(0, 1)), "SI_Transform BASEPOSE-r");
        Assert.Equal(new[] { 30f, 0f, 0f }, x[3..6].Select(v => MathF.Round(v, 2) + 0f).ToArray());
    }

    [Fact]
    public void Local_transform_under_non_identity_parent()
    {
        // Parent turned 90 deg about Ghoul2 Z (XSI Y); child 6.4 along Ghoul2 Y, same orientation, identity frames.
        var rz = Matrix4x4.CreateRotationZ(MathF.PI / 2);
        var child = rz; child.Translation = new Vector3(0, 6.4f, 0);
        var s = Build(Static(new[] { "p", "c" }, new[] { -1, 0 }, new[] { rz, child }), Def(0, 1));
        var srt = Block(s, "SI_Transform SRT-c");
        Assert.Equal(new[] { 0f, 0f, 0f, 10f, 0f, 0f }, srt[3..].Select(v => MathF.Round(v, 2) + 0f).ToArray());
    }

    [Fact]
    public void Animation_start_offsets_gla_frames()
    {
        // Root turns i * 0.1 rad about Ghoul2 Z at GLA frame i, i.e. about XSI Y.
        var s = Build(Gla(new[] { "root" }, new[] { -1 }), Def(4, 2));
        var keys = Keys(s, "root-ROTATION-Y");
        Assert.Equal(2, keys.Length);
        Assert.Equal(0.4f * D, keys[0], 0.05f);
        Assert.Equal(0.5f * D, keys[1], 0.05f);
    }

    [Theory]
    [InlineData(-30f, 30f)]
    [InlineData(0f, 20f)]
    [InlineData(24f, 24f)]
    public void Fps_fallback(float fps, float expected)
    {
        var s = Build(Three(), new AnimDef("A", 0, 3, -1, fps));
        var scene = Block(s, "SI_Scene scene");
        Assert.Equal(new[] { 1f, 3f, expected }, scene);
    }

    [Fact]
    public void Empty_animation_throws()
    {
        var ex = Assert.Throws<XsiExportException>(() => Build(Three(), Def(0, 0)));
        Assert.Equal("Animation 'A' has no frames (length 0).", ex.Message);
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
