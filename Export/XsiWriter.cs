using System.Globalization;
using System.Numerics;
using System.Text;

namespace SWGLAnimationExtractor;

public sealed class XsiExportException : Exception
{
    public XsiExportException(string message) : base(message) { }
}

/// <summary>
/// Writes one animation as a dotXSI 3.0 text file (nine linear FCurves per bone, SRT and BASEPOSE transforms),
/// in the layout of the Blender dotXsi3 exporter, with bone transforms in the XSI space the importer reads.
/// </summary>
public static class XsiWriter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // C = 0.64 * R, R sends (x, y, z) to (x, -z, y). Row-vector (transposed) forms.
    private static readonly Matrix4x4 CRow = Matrix4x4.Transpose(new Matrix4x4(
        0.64f, 0, 0, 0,
        0, 0, -0.64f, 0,
        0, 0.64f, 0, 0,
        0, 0, 0, 1));
    private static readonly Matrix4x4 CInvRow = Invert(CRow);
    private static readonly Matrix4x4 LiftRow = Matrix4x4.CreateTranslation(0, 0, 24);

    private static Matrix4x4 Invert(Matrix4x4 m) =>
        Matrix4x4.Invert(m, out var r) ? r : throw new InvalidOperationException("Singular matrix.");

    /// <summary>
    /// Ghoul2 model-space matrix to XSI space: rigid(C^-1 * T(0,0,24) * G * C) in column-vector form for animated
    /// frames; the base pose omits the T(0,0,24) origin lift, as the plugin's exporter does.
    /// </summary>
    private static Matrix4x4 ToXsi(Matrix4x4 g, bool lift = true)
    {
        // Row-vector order = reverse of the column-vector product. The base pose stays at 0 (no lift).
        var m = lift ? CRow * g * LiftRow * CInvRow : CRow * g * CInvRow;
        var q = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(OrthoRotation(m)));
        var r = Matrix4x4.CreateFromQuaternion(q);
        r.Translation = m.Translation;
        return r;
    }

    private static Matrix4x4 OrthoRotation(Matrix4x4 m)
    {
        var x = Vector3.Normalize(new Vector3(m.M11, m.M12, m.M13));
        var y = Vector3.Normalize(new Vector3(m.M21, m.M22, m.M23));
        var z = Vector3.Normalize(new Vector3(m.M31, m.M32, m.M33));
        return new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1);
    }

    private static string F(float v) => v.ToString("F6", Inv);
    private static float Deg(float rad) => rad * (180f / MathF.PI);

    private static string ExportName(string name) => name == "face" ? "face_always_" : name;

    public static void Write(string path, GlaFile gla, AnimDef anim)
    {
        string scene = Path.GetFileNameWithoutExtension(path).Replace(' ', '_');
        if (scene.Length == 0) scene = "scene";
        File.WriteAllText(path, Build(gla, anim, scene), Encoding.Latin1);
    }

    public static string Build(GlaFile gla, AnimDef anim, string sceneName)
    {
        if (anim.Length < 1)
            throw new XsiExportException($"Animation '{anim.Name}' has no frames (length {anim.Length}).");
        if (anim.Start < 0 || anim.Start + anim.Length > gla.NumFrames)
            throw new XsiExportException($"Animation frames exceed the GLA ({gla.NumFrames} frames).");

        int n = anim.Length;
        int nb = gla.Bones.Count;
        float fps = MathF.Abs(anim.Fps);
        if (fps < 0.01f) fps = 20f;

        // World matrices in XSI space per frame and at rest.
        var rest = gla.RestModelSpace().Select(g => ToXsi(g, lift: false)).ToArray();
        var frames = new Matrix4x4[n][];
        for (int f = 0; f < n; f++)
            frames[f] = gla.ModelSpace(new FrameSource(f, anim.Length, anim.Start)).Select(g => ToXsi(g)).ToArray();

        Matrix4x4 Local(Matrix4x4[] w, int b)
        {
            int p = gla.Bones[b].Parent;
            return p < 0 || p >= nb ? w[b] : w[b] * Invert(w[p]); // row-vector: local = world * parentWorld^-1
        }

        // Local keys per bone, with euler angles kept continuous from frame to frame.
        var keys = new (Vector3 rot, Vector3 loc)[nb][];
        for (int b = 0; b < nb; b++)
        {
            keys[b] = new (Vector3, Vector3)[n];
            Vector3? prev = null;
            for (int f = 0; f < n; f++)
            {
                var l = Local(frames[f], b);
                var e = EulerXyz.FromMatrix(l, prev);
                prev = e;
                keys[b][f] = (e, l.Translation);
            }
        }

        var children = new List<int>[nb];
        for (int i = 0; i < nb; i++) children[i] = new();
        var roots = new List<int>();
        for (int i = 0; i < nb; i++)
        {
            int p = gla.Bones[i].Parent;
            if (p >= 0 && p < nb) children[p].Add(i); else roots.Add(i);
        }

        var sb = new StringBuilder();
        sb.Append("xsi 0300txt 0032\n\n");
        sb.Append("SI_FileInfo  { \n\t\"").Append(sceneName).Append("\", \n\t\"SWGL Animation Extractor\", \n\t\"")
          .Append(DateTime.Now.ToString("ddd MMM dd HH:mm:ss yyyy", Inv)).Append("\", \n\t\"SWGL Animation Extractor\", \n}\n\n");
        sb.Append("SI_Scene ").Append(sceneName).Append(" { \n\t\"FRAMES\", \n\t").Append(F(1f)).Append(", \n\t")
          .Append(F(n)).Append(", \n\t").Append(F(fps)).Append(", \n}\n\n");
        sb.Append("SI_CoordinateSystem coord { \n\t1, \n\t0, \n\t1, \n\t0, \n\t2, \n\t5, \n}\n\n");
        sb.Append("SI_Angle  { \n\t0, \n}\n\n");
        sb.Append("SI_Ambience  { \n\t0.000000, \n\t0.000000, \n\t0.000000, \n}\n\n");

        void WriteTransform(string t1, string t2, string label, string xname, Matrix4x4 m)
        {
            var e = EulerXyz.FromMatrix(m, null);
            var tr = m.Translation;
            float[] v = { 1, 1, 1, Deg(e.X), Deg(e.Y), Deg(e.Z), tr.X, tr.Y, tr.Z };
            sb.Append(t1).Append("SI_Transform ").Append(label).Append('-').Append(xname).Append(" { \n");
            foreach (var x in v) sb.Append(t2).Append(F(x)).Append(", \n");
            sb.Append(t1).Append("}\n\n");
        }

        void WriteBone(int b, int depth)
        {
            string t = new('\t', depth), t1 = t + "\t", t2 = t + "\t\t";
            string xname = ExportName(gla.Bones[b].Name);
            sb.Append(t).Append("SI_Model MDL-").Append(xname).Append(" { \n");

            for (int kind = 0; kind < 3; kind++)
            {
                string kname = kind == 0 ? "SCALING" : kind == 1 ? "ROTATION" : "TRANSLATION";
                for (int axis = 0; axis < 3; axis++)
                {
                    string param = $"{kname}-{"XYZ"[axis]}";
                    sb.Append(t1).Append("SI_FCurve ").Append(xname).Append('-').Append(param).Append(" { \n");
                    sb.Append(t2).Append('"').Append(xname).Append("\", \n").Append(t2).Append('"').Append(param).Append("\", \n")
                      .Append(t2).Append("\"LINEAR\", \n").Append(t2).Append("1, \n").Append(t2).Append("1, \n")
                      .Append(t2).Append(n.ToString(Inv)).Append(", \n");
                    for (int f = 0; f < n; f++)
                    {
                        var (rot, loc) = keys[b][f];
                        float v = kind == 0 ? 1f : kind == 1 ? Deg(rot[axis]) : loc[axis];
                        sb.Append(t2).Append(F(f + 1)).Append(',').Append(F(v)).Append(",\n");
                    }
                    sb.Append(t1).Append("}\n\n");
                }
            }

            WriteTransform(t1, t2, "SRT", xname, Local(frames[0], b));
            WriteTransform(t1, t2, "BASEPOSE", xname, rest[b]);
            sb.Append(t1).Append("SI_Visibility  { \n").Append(t2).Append("1, \n").Append(t1).Append("}\n\n");
            sb.Append(t1).Append("SI_Null ").Append(xname).Append(" { \n").Append(t1).Append("}\n\n");
            foreach (int c in children[b]) WriteBone(c, depth + 1);
            sb.Append(t).Append("}\n\n");
        }

        foreach (int r in roots) WriteBone(r, 0);
        return sb.ToString().Replace("\n", "\r\n");
    }
}
