using System.Numerics;
using Xunit;

namespace SWGLAnimationExtractor.Tests;

public class EulerXyzTests
{
    private static Matrix4x4 RotZ(float deg) => Matrix4x4.CreateRotationZ(deg * MathF.PI / 180f);

    [Fact]
    public void Rotation_about_z_90()
    {
        var e = EulerXyz.FromMatrix(RotZ(90), null);
        Assert.Equal(0f, e.X, 1e-4f);
        Assert.Equal(0f, e.Y, 1e-4f);
        Assert.Equal(MathF.PI / 2, e.Z, 1e-4f);
    }

    [Fact]
    public void Continuity_keeps_angles_near_previous()
    {
        var e = EulerXyz.FromMatrix(RotZ(-170), new Vector3(0, 0, 170 * MathF.PI / 180f));
        Assert.Equal(190 * MathF.PI / 180f, e.Z, 1e-4f);
        Assert.Equal(0f, e.X, 1e-4f);
        Assert.Equal(0f, e.Y, 1e-4f);
    }

    [Fact]
    public void Matches_composition_order_rz_ry_rx()
    {
        // Column-vector R = Rz*Ry*Rx is row-vector Rx*Ry*Rz.
        var m = Matrix4x4.CreateRotationX(0.3f) * Matrix4x4.CreateRotationY(-0.5f) * Matrix4x4.CreateRotationZ(1.1f);
        var e = EulerXyz.FromMatrix(m, null);
        Assert.Equal(0.3f, e.X, 1e-4f);
        Assert.Equal(-0.5f, e.Y, 1e-4f);
        Assert.Equal(1.1f, e.Z, 1e-4f);
    }
}
