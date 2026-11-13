using System.Numerics;
using Xunit;

namespace SWGLAnimationExtractor.Tests;

public class GlaBuilderTests
{
    private const float D = MathF.PI / 180f;

    private static Matrix4x4 Rot(float yaw, float pitch, float roll, Vector3 t)
    {
        var m = Matrix4x4.CreateFromYawPitchRoll(yaw, pitch, roll);
        m.Translation = t;
        return m;
    }

    /// <summary>hand(0) has parent arm(2), a higher index; root -> arm -> hand -> fingers, plus face_always_ under root.</summary>
    private static GlaFile Source()
    {
        var names = new[] { "hand", "root", "arm", "fingers", "face_always_" };
        var parents = new[] { 2, -1, 1, 0, 1 };
        var rest = new[]
        {
            Rot(0.5f, -0.3f, 0.7f, new Vector3(12, -4, 3)),
            Rot(0f, 0.2f, 0f, new Vector3(3, -2, 5)),
            Rot(-0.4f, 0.2f, 0.9f, new Vector3(-6, 8, 2)),
            Rot(0.8f, 0.1f, -0.6f, new Vector3(2, 5, -9)),
            Rot(0.1f, 0.1f, 0.1f, new Vector3(0, 1, 20)),
        };
        const int total = 9;
        var f = new (Quaternion, Vector3)[total, 5];
        for (int i = 0; i < total; i++)
        {
            f[i, 1] = (Quaternion.CreateFromAxisAngle(Vector3.UnitX, (150 + 12 * i) * D), new Vector3(0.5f, -0.3f * i, 0.2f * i));
            f[i, 2] = (Quaternion.CreateFromYawPitchRoll(0.05f * i + 0.2f, -0.04f * i, 0.07f * i), new Vector3(1.5f * i, -0.5f, 0.25f * i));
            f[i, 0] = (Quaternion.CreateFromYawPitchRoll(-0.06f * i, 0.05f * i + 0.1f, 0.1f), new Vector3(-0.4f * i, 0.8f, 2f));
            f[i, 3] = (Quaternion.CreateFromYawPitchRoll(0.03f * i, 0.2f, -0.08f * i), new Vector3(0.3f, 0.2f * i, -0.1f * i));
            f[i, 4] = (Quaternion.CreateFromYawPitchRoll(0.02f * i, 0f, 0.01f * i), new Vector3(0, 0, 0.1f * i));
        }
        return GlaFile.Parse(TestGla.Build(names, parents, rest, f));
    }

    private static readonly AnimDef A = new("walk", 1, 5, 0, 20f);
    private static readonly AnimDef B = new("idle", 6, 3, -1, 10f);

    private static XsiAnimation Export(GlaFile gla, AnimDef d) => XsiReader.Parse(XsiWriter.Build(gla, d, d.Name));

    private static void AssertClose(Matrix4x4 e, Matrix4x4 a, string what)
    {
        Assert.True(Vector3.Distance(e.Translation, a.Translation) < 0.1f, $"{what}: translation {e.Translation} vs {a.Translation}");
        static Quaternion Q(Matrix4x4 m) { m.Translation = Vector3.Zero; return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(m)); }
        float angle = 2f * MathF.Acos(Math.Min(1f, Math.Abs(Quaternion.Dot(Q(e), Q(a))))) / D;
        Assert.True(angle < 0.2f, $"{what}: rotation differs by {angle} deg");
    }

    private static void AssertSameAnimation(GlaFile src, AnimDef ds, GlaFile dst, AnimDef dd)
    {
        Assert.Equal(ds.Length, dd.Length);
        for (int f = 0; f < ds.Length; f++)
        {
            var e = src.ModelSpace(new FrameSource(f, ds.Length, ds.Start));
            var a = dst.ModelSpace(new FrameSource(f, dd.Length, dd.Start));
            for (int b = 0; b < src.Bones.Count; b++)
            {
                int j = dst.FindBone(src.Bones[b].Name);
                Assert.True(j >= 0, src.Bones[b].Name);
                AssertClose(e[b], a[j], $"{ds.Name} {src.Bones[b].Name} frame {f}");
            }
        }
    }

    [Fact]
    public void Rebuild_with_template_skeleton_roundtrips()
    {
        var src = Source();
        var entries = new[]
        {
            new GlaBuildEntry("idle", Export(src, B), -1, 10f),
            new GlaBuildEntry("walk", Export(src, A), 0, 20f),
        };
        var r = GlaBuilder.Build(GlaSkeleton.FromGla(src), entries);
        Assert.Empty(r.Warnings);
        Assert.Equal(8, r.NumFrames);

        var gla = GlaFile.Parse(r.Gla);
        Assert.Equal(src.Bones.Select(b => (b.Name, b.Parent)), gla.Bones.Select(b => (b.Name, b.Parent)));
        var cfg = AnimationCfg.Parse(r.Cfg);
        Assert.Equal(new[] { new AnimDef("idle", 0, 3, -1, 10f), new AnimDef("walk", 3, 5, 0, 20f) }, cfg);
        AssertSameAnimation(src, B, gla, cfg[0]);
        AssertSameAnimation(src, A, gla, cfg[1]);
    }

    [Fact]
    public void Rebuild_with_skeleton_from_xsi_roundtrips()
    {
        var src = Source();
        var xsi = Export(src, A);
        var skel = GlaSkeleton.FromXsi(xsi, "test", scale: 1f);
        var r = GlaBuilder.Build(skel, new[] { new GlaBuildEntry("walk", xsi, 0, 20f) });
        var gla = GlaFile.Parse(r.Gla);
        Assert.Equal("test", gla.Name);
        Assert.Equal(1u, gla.Bones[gla.FindBone("face_always_")].Flags);
        for (int b = 0; b < src.Bones.Count; b++)
            AssertClose(src.Bones[b].BasePose, gla.Bones[gla.FindBone(src.Bones[b].Name)].BasePose, "base " + src.Bones[b].Name);
        AssertSameAnimation(src, A, gla, AnimationCfg.Parse(r.Cfg)[0]);
    }

    [Fact]
    public void Scaled_bind_pose_roundtrips()
    {
        // Game GLAs store the bind pose scaled by 0.64 (fScale); the skinning deltas stay rigid.
        var rest = new[] { Rot(0.3f, 0.1f, 0f, new Vector3(1, 2, 3)), Rot(-0.2f, 0.4f, 0.1f, new Vector3(4, -5, 6)) };
        for (int i = 0; i < 2; i++) { var t = rest[i].Translation; rest[i] = Matrix4x4.CreateScale(0.64f) * rest[i]; rest[i].Translation = t; }
        var f = new (Quaternion, Vector3)[3, 2];
        for (int i = 0; i < 3; i++)
        {
            f[i, 0] = (Quaternion.CreateFromYawPitchRoll(0.1f * i, 0.2f, 0f), new Vector3(1f * i, 0, 2));
            f[i, 1] = (Quaternion.CreateFromYawPitchRoll(0f, -0.1f * i, 0.3f), new Vector3(0, 0.5f * i, 0));
        }
        var src = GlaFile.Parse(TestGla.Build(new[] { "root", "child" }, new[] { -1, 0 }, rest, f));
        var anim = new AnimDef("a", 0, 3, 0, 20f);
        var r = GlaBuilder.Build(GlaSkeleton.FromGla(src), new[] { new GlaBuildEntry("a", Export(src, anim), 0, 20f) });
        var gla = GlaFile.Parse(r.Gla);
        AssertSameAnimation(src, anim, gla, AnimationCfg.Parse(r.Cfg)[0]);

        var fromXsi = GlaFile.Parse(GlaBuilder.Build(GlaSkeleton.FromXsi(Export(src, anim), "x"),
            new[] { new GlaBuildEntry("a", Export(src, anim), 0, 20f) }).Gla);
        for (int b = 0; b < 2; b++)
            Assert.True(Vector3.Distance(rest[b].Translation, fromXsi.Bones[b].BasePose.Translation) < 1e-3f
                        && MathF.Abs(rest[b].GetDeterminant() - fromXsi.Bones[b].BasePose.GetDeterminant()) < 1e-3f);
    }

    [Fact]
    public void Identical_transforms_share_pool_entries()
    {
        var id = (Quaternion.Identity, Vector3.Zero);
        var src = GlaFile.Parse(TestGla.Build(new[] { "root", "child" }, new[] { -1, 0 },
            new[] { Matrix4x4.Identity, Matrix4x4.CreateTranslation(0, 0, 5) }, new[,] { { id, id }, { id, id }, { id, id } }));
        var anim = new AnimDef("still", 0, 3, 0, 20f);
        var r = GlaBuilder.Build(GlaSkeleton.FromGla(src), new[] { new GlaBuildEntry("still", Export(src, anim), 0, 20f) });
        Assert.Equal(3, GlaFile.Parse(r.Gla).NumFrames);
        int poolEntries = (r.Gla.Length - BitConverter.ToInt32(r.Gla, 88)) / 14;
        Assert.Equal(1, poolEntries);
    }

    [Fact]
    public void Missing_bone_is_reported_and_follows_its_parent()
    {
        var src = Source();
        var xsiText = XsiWriter.Build(src, A, "walk");
        // Drop the "fingers" model (a leaf) from the file.
        int s = xsiText.IndexOf("SI_Model MDL-fingers {", StringComparison.Ordinal);
        int depth = 0, e = s;
        for (; e < xsiText.Length; e++)
        {
            if (xsiText[e] == '{') depth++;
            else if (xsiText[e] == '}' && --depth == 0) { e++; break; }
        }
        var xsi = XsiReader.Parse(xsiText.Remove(s, e - s));
        var r = GlaBuilder.Build(GlaSkeleton.FromGla(src), new[] { new GlaBuildEntry("walk", xsi, 0, 20f) });
        Assert.Contains(r.Warnings, w => w.Contains("fingers"));
        var gla = GlaFile.Parse(r.Gla);
        var skin = gla.Pose(new FrameSource(2, A.Length, 0));
        int fi = gla.FindBone("fingers"), hi = gla.FindBone("hand");
        AssertClose(skin[hi], skin[fi], "fingers follow hand");
    }

    [Fact]
    public void Duplicate_names_are_rejected()
    {
        var src = Source();
        var x = Export(src, A);
        Assert.Throws<GlaBuildException>(() => GlaBuilder.Build(GlaSkeleton.FromGla(src),
            new[] { new GlaBuildEntry("walk", x, 0, 20f), new GlaBuildEntry("walk", x, 0, 20f) }));
    }

    [Fact]
    public void Reader_handles_multi_component_hermite_curves_in_radians()
    {
        const string text = "xsi 0300txt 0032\n\nSI_Scene s { \"FRAMES\", 0, 2, 30, }\nSI_Angle { 1, }\n" +
            "SI_Model MDL-root { SI_Transform SRT-root { 1,1,1, 0,0,0, 1,2,3, }\n" +
            "SI_FCurve root-ROTATION { \"root\", \"ROTATION\", \"HERMITE\", 3, 1, 2,\n" +
            "0, 0,0,0, 0,0,0, 0,0,0,\n2, 1,0,0, 0,0,0, 0,0,0, }\n}\n";
        var x = XsiReader.Parse(text);
        Assert.Equal(3, x.FrameCount);
        Assert.Equal(30f, x.Fps);
        var m = x.Local(0, 1);
        Assert.Equal(new Vector3(1, 2, 3), m.Translation);
        var expected = Matrix4x4.CreateRotationX(0.5f);
        Assert.Equal(expected.M22, m.M22, 4);
        Assert.Equal(expected.M23, m.M23, 4);
    }
}
