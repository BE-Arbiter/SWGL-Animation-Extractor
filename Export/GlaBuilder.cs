using System.Globalization;
using System.Numerics;
using System.Text;

namespace SWGLAnimationExtractor;

public sealed class GlaBuildException : Exception
{
    public GlaBuildException(string message) : base(message) { }
}

public sealed record GlaSkeletonBone(string Name, int Parent, uint Flags, Matrix4x4 BasePose);

/// <summary>Bones (order, hierarchy, bind pose) of the GLA to build.</summary>
public sealed class GlaSkeleton
{
    public string Name { get; init; } = "";
    public float Scale { get; init; } = 1f;
    public IReadOnlyList<GlaSkeletonBone> Bones { get; init; } = Array.Empty<GlaSkeletonBone>();

    /// <summary>Same bones, order and bind pose as an existing GLA (keeps it compatible with the GLMs made for it).</summary>
    public static GlaSkeleton FromGla(GlaFile gla) => new()
    {
        Name = gla.Name,
        Scale = gla.Scale == 0f ? 1f : gla.Scale,
        Bones = gla.Bones.Select(b => new GlaSkeletonBone(b.Name, b.Parent, b.Flags, b.BasePose)).ToList(),
    };

    /// <summary>
    /// Bones of a dotXSI file in file order, bind pose from the BASEPOSE transforms, scaled by <paramref name="scale"/>
    /// like the bind pose of the game's GLAs (0.64).
    /// </summary>
    public static GlaSkeleton FromXsi(XsiAnimation xsi, string name, float scale = 0.64f)
    {
        var bones = new List<GlaSkeletonBone>();
        foreach (var m in xsi.Models)
        {
            if (m.BasePose is not Matrix4x4 bp)
                throw new GlaBuildException($"Bone '{m.Name}' has no BASEPOSE in the first XSI: choose a skeleton GLA.");
            uint flags = m.Name.Contains("_always_", StringComparison.OrdinalIgnoreCase) ? 1u : 0u;
            var b = GlaBuilder.FromXsiSpace(bp, lift: false);
            var t = b.Translation;
            b = Matrix4x4.CreateScale(scale) * b;
            b.Translation = t;
            bones.Add(new GlaSkeletonBone(m.Name, m.Parent, flags, b));
        }
        return new GlaSkeleton { Name = name, Scale = scale, Bones = bones };
    }
}

/// <summary>One animation of the GLA to build, in cfg order.</summary>
public sealed record GlaBuildEntry(string Name, XsiAnimation Anim, int Loop, float Fps);

public sealed record GlaBuildResult(byte[] Gla, string Cfg, int NumFrames, IReadOnlyList<string> Warnings);

/// <summary>
/// Builds a version-6 .gla (and its animation.cfg) from dotXSI animations: the inverse of <see cref="XsiWriter"/>.
/// Animations are stored one after the other in the given order.
/// </summary>
public static class GlaBuilder
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // Same conversion as XsiWriter: C = 0.64 * R, R sends (x, y, z) to (x, -z, y), row-vector forms.
    private static readonly Matrix4x4 CRow = Matrix4x4.Transpose(new Matrix4x4(
        0.64f, 0, 0, 0,
        0, 0, -0.64f, 0,
        0, 0.64f, 0, 0,
        0, 0, 0, 1));
    private static readonly Matrix4x4 CInvRow = Invert(CRow);
    private static readonly Matrix4x4 UnliftRow = Matrix4x4.CreateTranslation(0, 0, -24);

    private static Matrix4x4 Invert(Matrix4x4 m) =>
        Matrix4x4.Invert(m, out var r) ? r : throw new GlaBuildException("Singular bone matrix.");

    /// <summary>XSI world matrix back to Ghoul2 model space: C * X * C^-1, then the origin lift undone for animated frames.</summary>
    public static Matrix4x4 FromXsiSpace(Matrix4x4 x, bool lift = true)
    {
        var g = CInvRow * x * CRow;
        return lift ? g * UnliftRow : g;
    }

    /// <summary>Bone names match exactly or ignoring case; "face" and "face_always_" are the same bone.</summary>
    private static string Key(string name)
    {
        var n = name.ToLowerInvariant();
        return n == "face_always_" ? "face" : n;
    }

    private static Matrix4x4 Rigid(Matrix4x4 m)
    {
        var r = Matrix4x4.CreateFromQuaternion(RotationOf(m));
        r.Translation = m.Translation;
        return r;
    }

    private static Quaternion RotationOf(Matrix4x4 m)
    {
        var x = Vector3.Normalize(new Vector3(m.M11, m.M12, m.M13));
        var y = Vector3.Normalize(new Vector3(m.M21, m.M22, m.M23));
        var z = Vector3.Normalize(new Vector3(m.M31, m.M32, m.M33));
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(
            new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1)));
    }

    private static ushort Pack(float v, float offset, float scale, ref bool clamped)
    {
        double r = Math.Round((v + offset) * scale);
        if (r < 0 || r > ushort.MaxValue) { clamped = true; r = Math.Clamp(r, 0, ushort.MaxValue); }
        return (ushort)r;
    }

    public static GlaBuildResult Build(GlaSkeleton skel, IReadOnlyList<GlaBuildEntry> entries)
    {
        int nb = skel.Bones.Count;
        if (nb == 0) throw new GlaBuildException("The skeleton has no bones.");
        if (entries.Count == 0) throw new GlaBuildException("No animation to build.");
        var dup = entries.GroupBy(e => e.Name, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (dup != null) throw new GlaBuildException($"Animation name '{dup.Key}' is used twice.");
        var bad = entries.FirstOrDefault(e => string.IsNullOrWhiteSpace(e.Name) || e.Name.Any(char.IsWhiteSpace));
        if (bad != null) throw new GlaBuildException($"Invalid animation name '{bad.Name}' (empty or with spaces).");

        var order = ParentFirst(skel);
        // Real GLAs scale their bind pose (fScale, 0.64); XSI keeps only rigid transforms, so the deltas are taken
        // against the rigid part of the bind pose.
        var baseInv = skel.Bones.Select(b => Invert(Rigid(b.BasePose))).ToArray();
        var warnings = new List<string>();

        // Pool of unique compressed bones (14 bytes each) and, per frame and bone, its index.
        var pool = new List<byte[]>();
        var poolIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        var indexes = new List<int>();
        var cfg = new StringBuilder();
        cfg.Append("// Built by SWGL Animation Extractor\r\n");
        cfg.Append("// name\tstart\tlength\tloop\tfps\r\n");
        int frameStart = 0;

        foreach (var e in entries)
        {
            var xsi = e.Anim;
            var map = new int[nb];
            var missing = new List<string>();
            for (int b = 0; b < nb; b++)
            {
                string k = Key(skel.Bones[b].Name);
                map[b] = xsi.Models.FindIndex(m => Key(m.Name) == k);
                if (map[b] < 0) missing.Add(skel.Bones[b].Name);
            }
            if (missing.Count == nb) throw new GlaBuildException($"{e.Name}: no bone of the skeleton found in the XSI.");
            if (missing.Count > 0)
                warnings.Add($"{e.Name}: {missing.Count} bone(s) not in the XSI, kept at rest ({string.Join(", ", missing.Take(5))}{(missing.Count > 5 ? ", ..." : "")}).");
            var extra = xsi.Models.Where(m => !skel.Bones.Any(b => Key(b.Name) == Key(m.Name))).Select(m => m.Name).ToList();
            if (extra.Count > 0)
                warnings.Add($"{e.Name}: {extra.Count} XSI bone(s) not in the skeleton, ignored ({string.Join(", ", extra.Take(5))}{(extra.Count > 5 ? ", ..." : "")}).");

            bool clamped = false;
            var skin = new Matrix4x4[nb];
            for (int f = 0; f < xsi.FrameCount; f++)
            {
                var world = xsi.World(f);
                foreach (int b in order)
                {
                    int parent = skel.Bones[b].Parent;
                    var parentSkin = parent < 0 ? Matrix4x4.Identity : skin[parent];
                    // Missing bone: no delta of its own, it follows its parent.
                    skin[b] = map[b] < 0 ? parentSkin : baseInv[b] * FromXsiSpace(world[map[b]]);
                    var local = parent < 0 ? skin[b] : skin[b] * Invert(parentSkin);
                    var q = RotationOf(local);
                    var t = local.Translation;
                    var bytes = new byte[14];
                    ushort[] u =
                    {
                        Pack(q.W, 2f, 16383f, ref clamped), Pack(q.X, 2f, 16383f, ref clamped),
                        Pack(q.Y, 2f, 16383f, ref clamped), Pack(q.Z, 2f, 16383f, ref clamped),
                        Pack(t.X, 512f, 64f, ref clamped), Pack(t.Y, 512f, 64f, ref clamped), Pack(t.Z, 512f, 64f, ref clamped),
                    };
                    for (int i = 0; i < 7; i++) BitConverter.TryWriteBytes(bytes.AsSpan(2 * i), u[i]);
                    string key = Convert.ToBase64String(bytes);
                    if (!poolIndex.TryGetValue(key, out int idx))
                    {
                        idx = pool.Count;
                        pool.Add(bytes);
                        poolIndex[key] = idx;
                    }
                    indexes.Add(idx);
                }
                // indexes were appended in parent-first order: reorder this frame by bone index.
                ReorderLastFrame(indexes, order);
            }
            if (clamped) warnings.Add($"{e.Name}: translations beyond +-512 were clamped.");

            float fps = Math.Abs(e.Fps) < 0.01f ? 20f : e.Fps;
            cfg.Append(e.Name).Append('\t').Append(frameStart.ToString(Inv)).Append('\t')
               .Append(xsi.FrameCount.ToString(Inv)).Append('\t').Append(e.Loop.ToString(Inv)).Append('\t')
               .Append(fps.ToString("0.###", Inv)).Append("\r\n");
            frameStart += xsi.FrameCount;
        }
        if (pool.Count > 0xFFFFFF) throw new GlaBuildException("Too many distinct bone transforms for a GLA.");

        return new GlaBuildResult(Write(skel, frameStart, indexes, pool), cfg.ToString(), frameStart, warnings);
    }

    private static void ReorderLastFrame(List<int> indexes, int[] order)
    {
        int nb = order.Length, start = indexes.Count - nb;
        var byBone = new int[nb];
        for (int i = 0; i < nb; i++) byBone[order[i]] = indexes[start + i];
        for (int b = 0; b < nb; b++) indexes[start + b] = byBone[b];
    }

    private static int[] ParentFirst(GlaSkeleton skel)
    {
        int nb = skel.Bones.Count;
        var order = new List<int>(nb);
        var state = new int[nb]; // 0 new, 1 visiting, 2 done
        void Visit(int i)
        {
            if (state[i] == 2) return;
            if (state[i] == 1) throw new GlaBuildException($"The bone hierarchy has a cycle (bone '{skel.Bones[i].Name}').");
            state[i] = 1;
            int p = skel.Bones[i].Parent;
            if (p >= nb || p < -1) throw new GlaBuildException($"Bone '{skel.Bones[i].Name}' has an invalid parent.");
            if (p >= 0) Visit(p);
            state[i] = 2;
            order.Add(i);
        }
        for (int i = 0; i < nb; i++) Visit(i);
        return order.ToArray();
    }

    private static void WriteMatrix34(BinaryWriter w, Matrix4x4 m)
    {
        // 3x4, column-vector convention, row by row (translation in column 3).
        float[] rows = { m.M11, m.M21, m.M31, m.M41, m.M12, m.M22, m.M32, m.M42, m.M13, m.M23, m.M33, m.M43 };
        foreach (var v in rows) w.Write(v);
    }

    private static byte[] Write(GlaSkeleton skel, int numFrames, List<int> indexes, List<byte[]> pool)
    {
        int nb = skel.Bones.Count;
        var children = new List<int>[nb];
        for (int i = 0; i < nb; i++) children[i] = new();
        for (int i = 0; i < nb; i++) if (skel.Bones[i].Parent >= 0) children[skel.Bones[i].Parent].Add(i);

        const int header = 100;
        var offsets = new int[nb];
        int ofs = 4 * nb;
        for (int i = 0; i < nb; i++) { offsets[i] = ofs; ofs += 172 + 4 * children[i].Count; }
        int ofsFrames = header + ofs;
        int ofsPool = (ofsFrames + numFrames * nb * 3 + 3) & ~3;
        int ofsEnd = ofsPool + pool.Count * 14;

        using var ms = new MemoryStream(ofsEnd);
        using var w = new BinaryWriter(ms);
        w.Write(Encoding.ASCII.GetBytes("2LGA"));
        w.Write(6);
        w.Write(Fixed(skel.Name, 64));
        w.Write(skel.Scale);
        w.Write(numFrames);
        w.Write(ofsFrames);
        w.Write(nb);
        w.Write(ofsPool);
        w.Write(header);
        w.Write(ofsEnd);

        foreach (var o in offsets) w.Write(o);
        for (int i = 0; i < nb; i++)
        {
            var b = skel.Bones[i];
            w.Write(Fixed(b.Name, 64));
            w.Write(b.Flags);
            w.Write(b.Parent);
            WriteMatrix34(w, b.BasePose);
            WriteMatrix34(w, Invert(b.BasePose));
            w.Write(children[i].Count);
            foreach (var c in children[i]) w.Write(c);
        }

        foreach (var idx in indexes) { w.Write((byte)idx); w.Write((byte)(idx >> 8)); w.Write((byte)(idx >> 16)); }
        while (ms.Position < ofsPool) w.Write((byte)0);
        foreach (var p in pool) w.Write(p);
        w.Flush();
        return ms.ToArray();
    }

    private static byte[] Fixed(string s, int len)
    {
        var b = new byte[len];
        var src = Encoding.ASCII.GetBytes(s);
        Array.Copy(src, b, Math.Min(src.Length, len - 1));
        return b;
    }
}
