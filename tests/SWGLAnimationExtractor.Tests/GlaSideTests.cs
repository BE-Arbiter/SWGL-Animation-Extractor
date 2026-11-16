using System.Globalization;
using System.Numerics;
using Xunit;

namespace SWGLAnimationExtractor.Tests;

public class GlaSideTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "glaside_" + Guid.NewGuid().ToString("N"));

    public GlaSideTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private string WriteGla(string? cfg)
    {
        var id = (Quaternion.Identity, Vector3.Zero);
        var bytes = TestGla.Build(new[] { "root" }, new[] { -1 }, new[] { Matrix4x4.Identity }, new[,] { { id } });
        var path = Path.Combine(_dir, "test.gla");
        File.WriteAllBytes(path, bytes);
        if (cfg != null) File.WriteAllText(Path.Combine(_dir, "animation.cfg"), cfg);
        return path;
    }

    [Fact]
    public void Load_reads_cfg_next_to_gla()
    {
        var side = GlaSide.Load(WriteGla("BOTH_STAND1 0 10 -1 20\nBOTH_RUN1 10 5 0 20\n"));
        Assert.Equal(2, side.Anims.Count);
        Assert.NotNull(side.Find("BOTH_RUN1"));
        Assert.Null(side.Find("NOPE"));
    }

    [Fact]
    public void Load_without_cfg_throws()
    {
        var e = Assert.Throws<GlaLoadException>(() => GlaSide.Load(WriteGla(null)));
        Assert.Equal("Neither animation.cfg nor test.cfg found next to the GLA.", e.Message);
    }

    [Fact]
    public void Load_falls_back_to_cfg_named_after_the_gla()
    {
        var path = WriteGla(null);
        File.WriteAllText(Path.Combine(_dir, "test.cfg"), "BOTH_WALK1 0 4 -1 20\n");
        var side = GlaSide.Load(path);
        Assert.Equal(new[] { "BOTH_WALK1" }, side.Anims.Select(a => a.Name));
    }

    [Fact]
    public void Load_prefers_animation_cfg_over_named_cfg()
    {
        var path = WriteGla("BOTH_STAND1 0 1 -1 20\n");
        File.WriteAllText(Path.Combine(_dir, "test.cfg"), "BOTH_WALK1 0 1 -1 20\n");
        Assert.Equal(new[] { "BOTH_STAND1" }, GlaSide.Load(path).Anims.Select(a => a.Name));
    }

    [Fact]
    public void Load_invalid_gla_throws()
    {
        var path = Path.Combine(_dir, "bad.gla");
        File.WriteAllBytes(path, new byte[200]);
        File.WriteAllText(Path.Combine(_dir, "animation.cfg"), "A 0 1 0 20\n");
        var e = Assert.Throws<GlaLoadException>(() => GlaSide.Load(path));
        Assert.Equal("Not a GLA file.", e.Message);
    }

    [Fact]
    public void Load_missing_gla_throws_GlaLoadException()
    {
        File.WriteAllText(Path.Combine(_dir, "animation.cfg"), "A 0 1 0 20\n");
        Assert.Throws<GlaLoadException>(() => GlaSide.Load(Path.Combine(_dir, "missing.gla")));
    }

    [Fact]
    public void Duplicate_names_keep_first()
    {
        var side = GlaSide.Load(WriteGla("BOTH_STAND1 0 10 -1 20\nBOTH_STAND1 50 5 -1 20\n"));
        Assert.Single(side.Anims);
        Assert.Equal(0, side.Anims[0].Start);
    }

    [Fact]
    public void Cfg_fps_parsed_invariant_under_fr_culture()
    {
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
            var side = GlaSide.Load(WriteGla("BOTH_STAND1 0 10 -1 20.5\n"));
            Assert.Equal(20.5f, side.Anims[0].Fps);
        }
        finally { CultureInfo.CurrentCulture = old; }
    }

    [Fact]
    public void Find_is_case_sensitive()
    {
        var side = GlaSide.Load(WriteGla("BOTH_STAND1 0 10 -1 20\n"));
        Assert.Null(side.Find("both_stand1"));
        Assert.NotNull(side.Find("BOTH_STAND1"));
    }

    [Fact]
    public void Names_differing_only_by_case_are_both_kept()
    {
        var side = GlaSide.Load(WriteGla("BOTH_STAND1 0 10 -1 20\nboth_stand1 50 5 -1 20\n"));
        Assert.Equal(2, side.Anims.Count);
    }
}
