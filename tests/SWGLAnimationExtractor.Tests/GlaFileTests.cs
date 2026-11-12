using System.Numerics;
using Xunit;

namespace SWGLAnimationExtractor.Tests;

public class GlaFileTests
{
    private static readonly (Quaternion q, Vector3 t) Id = (Quaternion.Identity, Vector3.Zero);
    private static readonly Quaternion Rz90 = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2);

    private static void Near(Vector3 e, Vector3 a, float tol)
    {
        Assert.True(Vector3.Distance(e, a) <= tol, $"expected {e} got {a}");
    }

    [Fact]
    public void BasePose_is_read_transposed_to_row_vector()
    {
        // Column-vector: rotation 90 deg about Z, translation (1,2,3) in column 3.
        var rowVec = Matrix4x4.CreateFromQuaternion(Rz90);
        rowVec.Translation = new Vector3(1, 2, 3);
        var gla = GlaFile.Parse(TestGla.Build(new[] { "root" }, new[] { -1 }, new[] { rowVec }, new[,] { { Id } }));

        var bp = gla.Bones[0].BasePose;
        Near(new Vector3(1, 2, 3), bp.Translation, 1e-4f);
        Near(Vector3.UnitY, Vector3.Transform(Vector3.UnitX, bp) - bp.Translation, 1e-4f);
    }

    [Fact]
    public void ModelSpace_composes_delta_with_base_pose()
    {
        var child = Matrix4x4.CreateTranslation(10, 0, 0);
        var gla = GlaFile.Parse(TestGla.Build(
            new[] { "root", "child" }, new[] { -1, 0 },
            new[] { Matrix4x4.Identity, child },
            new[,] { { (Rz90, Vector3.Zero), Id } }));

        var ms = gla.ModelSpace(new FrameSource(0, 1, 0));
        Near(new Vector3(0, 10, 0), ms[1].Translation, 0.02f);

        var rest = gla.RestModelSpace();
        Near(new Vector3(10, 0, 0), rest[1].Translation, 1e-4f);
    }

    [Fact]
    public void FindBone_returns_index_or_minus_one()
    {
        var gla = GlaFile.Parse(TestGla.Build(
            new[] { "root", "Child" }, new[] { -1, 0 },
            new[] { Matrix4x4.Identity, Matrix4x4.Identity },
            new[,] { { Id, Id } }));
        Assert.Equal(1, gla.FindBone("Child"));
        Assert.Equal(-1, gla.FindBone("child"));
        Assert.Equal(-1, gla.FindBone("nope"));
        Assert.Equal(new[] { 0, 1 }, gla.ParentFirstOrder);
    }

    private static byte[] Valid(int[]? parents = null)
    {
        parents ??= new[] { -1, 0 };
        return TestGla.Build(new[] { "a", "b" }, parents, new[] { Matrix4x4.Identity, Matrix4x4.Identity },
            new[,] { { Id, Id }, { Id, Id } });
    }

    [Fact]
    public void Truncated_pool_is_rejected()
    {
        var d = Valid();
        Assert.Throws<InvalidDataException>(() => GlaFile.Parse(d[..^10]));
    }

    [Fact]
    public void Truncated_frame_index_table_is_rejected()
    {
        var d = Valid();
        int ofsFrames = BitConverter.ToInt32(d, 80);
        Assert.Throws<InvalidDataException>(() => GlaFile.Parse(d[..(ofsFrames + 5)]));
    }

    [Fact]
    public void Zero_frames_is_rejected()
    {
        var d = Valid();
        BitConverter.GetBytes(0).CopyTo(d, 76);
        Assert.Throws<InvalidDataException>(() => GlaFile.Parse(d));
    }

    [Fact]
    public void Parent_cycle_is_rejected() =>
        Assert.Throws<InvalidDataException>(() => GlaFile.Parse(Valid(new[] { 1, 0 })));

    [Fact]
    public void Parent_out_of_range_is_rejected()
    {
        var d = Valid();
        // Bone 1 entry: parent index at +68 of the entry whose offset is stored after the header.
        int entry = 100 + BitConverter.ToInt32(d, 104);
        BitConverter.GetBytes(7).CopyTo(d, entry + 68);
        Assert.Throws<InvalidDataException>(() => GlaFile.Parse(d));
    }
}
