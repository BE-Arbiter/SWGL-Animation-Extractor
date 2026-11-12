using System.Numerics;
using Xunit;

namespace SWGLAnimationExtractor.Tests;

public class AnimComparerTests
{
    private static Quaternion Rot(float deg) => Quaternion.CreateFromAxisAngle(Vector3.UnitY, deg * MathF.PI / 180f);
    private static (Quaternion, Vector3) F(float deg) => (Rot(deg), Vector3.Zero);

    private static GlaFile Gla(params float[] rootDegs)
    {
        var frames = new (Quaternion, Vector3)[rootDegs.Length, 1];
        for (int i = 0; i < rootDegs.Length; i++) frames[i, 0] = F(rootDegs[i]);
        return GlaFile.Parse(TestGla.Build(new[] { "root" }, new[] { -1 }, new[] { Matrix4x4.Identity }, frames));
    }

    private static AnimDef Def(int len) => new("A", 0, len, -1, 20f);

    private static AnimComparison Cmp(GlaFile a, GlaFile b) =>
        AnimComparer.Compare(a, Def(a.NumFrames), b, Def(b.NumFrames));

    [Fact]
    public void Identical_animations_are_identical()
    {
        var r = Cmp(Gla(0, 10, 20), Gla(0, 10, 20));
        Assert.Equal(Classification.Identical, r.Class);
        Assert.Empty(r.Bones);
    }

    [Fact]
    public void Same_motion_different_length_is_minor()
    {
        var r = Cmp(Gla(0, 10, 20), Gla(0, 5, 10, 15, 20));
        Assert.Equal(Classification.Minor, r.Class);
        Assert.True(r.MeanAngleDeg < 10f);
        Assert.Equal(5, r.Samples);
    }

    [Fact]
    public void Small_rotation_change_is_minor()
    {
        var r = Cmp(Gla(0, 0, 0), Gla(5, 5, 5));
        Assert.Equal(Classification.Minor, r.Class);
        Assert.InRange(r.Bones[0].MaxAngleDeg, 4.8f, 5.2f);
    }

    [Fact]
    public void Large_rotation_change_is_major() =>
        Assert.Equal(Classification.Major, Cmp(Gla(0, 0, 0), Gla(90, 90, 90)).Class);

    [Fact]
    public void Sub_tolerance_noise_is_identical() =>
        Assert.Equal(Classification.Identical, Cmp(Gla(0, 0, 0), Gla(0.2f, 0.2f, 0.2f)).Class);

    [Fact]
    public void Bones_only_on_one_side_are_listed()
    {
        var id = (Quaternion.Identity, Vector3.Zero);
        var a = Gla(0, 0);
        var b = GlaFile.Parse(TestGla.Build(new[] { "root", "extra" }, new[] { -1, 0 },
            new[] { Matrix4x4.Identity, Matrix4x4.Identity }, new[,] { { id, id }, { id, id } }));
        var r = Cmp(a, b);
        Assert.Equal(new[] { "extra" }, r.OnlyInB);
        Assert.Empty(r.OnlyInA);
    }

    [Fact]
    public void Single_frame_animations_compare()
    {
        var r1 = Cmp(Gla(0), Gla(0));
        Assert.Equal(1, r1.Samples);
        var r2 = Cmp(Gla(0), Gla(0, 0, 0, 0));
        Assert.Equal(4, r2.Samples);
    }

    [Fact]
    public void Peak_frame_is_reported()
    {
        var r = Cmp(Gla(0, 0, 0, 0), Gla(0, 0, 30, 0));
        Assert.Equal(2, r.Bones[0].AngleFrame);
    }

    private static GlaFile Gla2(float[] rootDegs, Vector3[] childT, float[] childDegs)
    {
        var frames = new (Quaternion, Vector3)[rootDegs.Length, 2];
        for (int i = 0; i < rootDegs.Length; i++)
        {
            frames[i, 0] = F(rootDegs[i]);
            frames[i, 1] = (Rot(childDegs[i]), childT[i]);
        }
        return GlaFile.Parse(TestGla.Build(new[] { "root", "child" }, new[] { -1, 0 },
            new[] { Matrix4x4.Identity, Matrix4x4.Identity }, frames));
    }

    [Fact]
    public void Mean_angle_is_exact()
    {
        var r = Cmp(Gla(0, 0, 0, 0), Gla(0, 0, 30, 0));
        Assert.InRange(r.MeanAngleDeg, 7.45f, 7.55f);
    }

    [Fact]
    public void Large_peak_with_low_mean_is_minor()
    {
        var r = Cmp(Gla(0, 0, 0, 0, 0, 0, 0, 0, 0, 0), Gla(0, 0, 0, 0, 0, 0, 0, 0, 0, 90));
        Assert.Equal(Classification.Minor, r.Class);
        Assert.InRange(r.Bones[0].MaxAngleDeg, 89f, 91f);
    }

    private static GlaFile GlaT(params Vector3[] t)
    {
        var frames = new (Quaternion, Vector3)[t.Length, 1];
        for (int i = 0; i < t.Length; i++) frames[i, 0] = (Quaternion.Identity, t[i]);
        return GlaFile.Parse(TestGla.Build(new[] { "root" }, new[] { -1 }, new[] { Matrix4x4.Identity }, frames));
    }

    [Fact]
    public void Offset_above_tolerance_is_reported()
    {
        var r = Cmp(GlaT(Vector3.Zero, Vector3.Zero, Vector3.Zero), GlaT(Vector3.Zero, new Vector3(0, 0, 1), Vector3.Zero));
        Assert.NotEqual(Classification.Identical, r.Class);
        Assert.InRange(r.Bones[0].MaxOffset, 0.95f, 1.05f);
        Assert.Equal(1, r.Bones[0].OffsetFrame);
    }

    [Fact]
    public void Offset_below_tolerance_is_identical() =>
        Assert.Equal(Classification.Identical, Cmp(GlaT(Vector3.Zero, Vector3.Zero), GlaT(new Vector3(0.02f, 0, 0), new Vector3(0.02f, 0, 0))).Class);

    [Fact]
    public void Bones_are_sorted_by_angle_descending_and_filtered()
    {
        // root: 0 vs 3 deg; child (identity base, model = skin product): child delta 10 deg extra.
        var a = Gla2(new float[] { 0, 0 }, new[] { Vector3.Zero, Vector3.Zero }, new float[] { 0, 0 });
        var b = Gla2(new float[] { 3, 3 }, new[] { Vector3.Zero, Vector3.Zero }, new float[] { 10, 10 });
        var r = Cmp(a, b);
        Assert.Equal(2, r.Bones.Count);
        Assert.True(r.Bones[0].MaxAngleDeg >= r.Bones[1].MaxAngleDeg);
        Assert.Equal("child", r.Bones[0].Bone);

        var c = Gla2(new float[] { 0, 0 }, new[] { Vector3.Zero, Vector3.Zero }, new float[] { 0, 0 });
        var d = Gla2(new float[] { 0, 0 }, new[] { Vector3.Zero, Vector3.Zero }, new float[] { 5, 5 });
        var r2 = Cmp(c, d);
        Assert.Equal(new[] { "child" }, r2.Bones.Select(x => x.Bone));
    }

    [Fact]
    public void Zero_length_animation_throws()
    {
        var g = Gla(0, 0);
        Assert.Throws<ArgumentException>(() => AnimComparer.Compare(g, new AnimDef("A", 0, 0, -1, 20f), g, Def(2)));
    }

    private static GlaFile Named(string name, int frames = 3)
    {
        var f = new (Quaternion, Vector3)[frames, 1];
        for (int i = 0; i < frames; i++) f[i, 0] = (Quaternion.Identity, Vector3.Zero);
        return GlaFile.Parse(TestGla.Build(new[] { name }, new[] { -1 }, new[] { Matrix4x4.Identity }, f));
    }

    [Fact]
    public void No_common_bone_throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => Cmp(Named("a"), Named("b")));
        Assert.Equal("No bone in common between the two GLAs.", ex.Message);
    }

    [Theory]
    [InlineData(2, 2, 3, "Animation frames exceed the GLA (3 frames).")]  // overrun on the reference side
    [InlineData(-1, 2, 3, "Animation frames exceed the GLA (3 frames).")] // negative start
    public void Out_of_range_reference_animation_throws(int start, int len, int frames, string msg)
    {
        var g = Gla(0, 0, 0);
        Assert.Equal(frames, g.NumFrames);
        var ex = Assert.Throws<ArgumentException>(() => AnimComparer.Compare(g, new AnimDef("A", start, len, -1, 20f), g, Def(2)));
        Assert.Equal(msg, ex.Message);
    }

    [Fact]
    public void Out_of_range_compared_animation_throws()
    {
        var a = Gla(0, 0, 0, 0, 0);
        var b = Gla(0, 0);
        var ex = Assert.Throws<ArgumentException>(() => AnimComparer.Compare(a, Def(3), b, new AnimDef("A", 1, 2, -1, 20f)));
        Assert.Equal("Animation frames exceed the GLA (2 frames).", ex.Message);
        Assert.Throws<ArgumentException>(() => AnimComparer.Compare(a, Def(3), b, new AnimDef("A", -1, 1, -1, 20f)));
    }
}
