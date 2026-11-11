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
}
