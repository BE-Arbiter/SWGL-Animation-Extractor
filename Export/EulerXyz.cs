using System.Numerics;

namespace SWGLAnimationExtractor;

/// <summary>Euler angles in Blender's XYZ convention (R = Rz * Ry * Rx, column-vector), like mathutils' to_euler.</summary>
public static class EulerXyz
{
    private const float TwoPi = MathF.PI * 2f;

    /// <summary>Radians. With <paramref name="previous"/>, returns the equivalent solution closest to it.</summary>
    public static Vector3 FromMatrix(Matrix4x4 rotationRowVector, Vector3? previous)
    {
        // Column-vector entries c[row, col] = row-vector matrix entry [col, row].
        var m = rotationRowVector;
        float c00 = m.M11, c01 = m.M21, c02 = m.M31;
        float c10 = m.M12, c11 = m.M22, c12 = m.M32;
        float c20 = m.M13, c21 = m.M23, c22 = m.M33;
        // Normalize against residual scale.
        float s = MathF.Sqrt(c00 * c00 + c10 * c10 + c20 * c20);
        if (s > 1e-8f)
        {
            c00 /= s; c01 /= s; c02 /= s; c10 /= s; c11 /= s; c12 /= s; c20 /= s; c21 /= s; c22 /= s;
        }

        float cy = MathF.Sqrt(c00 * c00 + c10 * c10);
        Vector3 e1, e2;
        if (cy > 16f * 1.1920929e-7f)
        {
            e1 = new Vector3(MathF.Atan2(c21, c22), MathF.Atan2(-c20, cy), MathF.Atan2(c10, c00));
            e2 = new Vector3(MathF.Atan2(-c21, -c22), MathF.Atan2(-c20, -cy), MathF.Atan2(-c10, -c00));
        }
        else
        {
            e1 = e2 = new Vector3(MathF.Atan2(-c12, c11), MathF.Atan2(-c20, cy), 0f);
        }

        if (previous is not Vector3 p)
            return Math.Abs(e1.X) + Math.Abs(e1.Y) + Math.Abs(e1.Z) <= Math.Abs(e2.X) + Math.Abs(e2.Y) + Math.Abs(e2.Z) ? e1 : e2;

        e1 = Compat(e1, p);
        e2 = Compat(e2, p);
        return Dist(e1, p) <= Dist(e2, p) ? e1 : e2;
    }

    private static float Fit(float v, float target)
    {
        while (v - target > MathF.PI) v -= TwoPi;
        while (v - target < -MathF.PI) v += TwoPi;
        return v;
    }

    private static Vector3 Compat(Vector3 e, Vector3 p) => new(Fit(e.X, p.X), Fit(e.Y, p.Y), Fit(e.Z, p.Z));

    private static float Dist(Vector3 a, Vector3 b) => MathF.Abs(a.X - b.X) + MathF.Abs(a.Y - b.Y) + MathF.Abs(a.Z - b.Z);
}
