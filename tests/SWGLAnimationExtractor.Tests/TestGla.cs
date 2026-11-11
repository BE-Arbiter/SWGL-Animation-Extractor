using System.Numerics;
using System.Text;

namespace SWGLAnimationExtractor.Tests;

/// <summary>Builds minimal version-6 .gla files in memory.</summary>
public static class TestGla
{
    public static byte[] Build(string[] names, int[] parents, Matrix4x4[] basePoses, (Quaternion q, Vector3 t)[,] frames)
    {
        int nb = names.Length, nf = frames.GetLength(0);
        var children = new List<int>[nb];
        for (int i = 0; i < nb; i++) children[i] = new();
        for (int i = 0; i < nb; i++) if (parents[i] >= 0) children[parents[i]].Add(i);

        // Bone entries (offsets relative to the table at 100).
        var entries = new List<byte[]>();
        int ofs = 4 * nb;
        var offsets = new int[nb];
        for (int i = 0; i < nb; i++)
        {
            var e = new byte[172 + 4 * children[i].Count];
            Encoding.ASCII.GetBytes(names[i]).CopyTo(e, 0);
            BitConverter.GetBytes(0).CopyTo(e, 64);
            BitConverter.GetBytes(parents[i]).CopyTo(e, 68);
            // 3x4 column-vector convention, row by row (translation in column 3).
            var m = basePoses[i];
            float[] rows =
            {
                m.M11, m.M21, m.M31, m.M41,
                m.M12, m.M22, m.M32, m.M42,
                m.M13, m.M23, m.M33, m.M43,
            };
            for (int k = 0; k < 12; k++) BitConverter.GetBytes(rows[k]).CopyTo(e, 72 + 4 * k);
            BitConverter.GetBytes(children[i].Count).CopyTo(e, 168);
            for (int c = 0; c < children[i].Count; c++) BitConverter.GetBytes(children[i][c]).CopyTo(e, 172 + 4 * c);
            offsets[i] = ofs;
            ofs += e.Length;
            entries.Add(e);
        }
        int ofsFrames = 100 + ofs;
        int ofsPool = ofsFrames + nf * nb * 3;
        int ofsEnd = ofsPool + nf * nb * 14;
        var data = new byte[ofsEnd];
        Encoding.ASCII.GetBytes("2LGA").CopyTo(data, 0);
        BitConverter.GetBytes(6).CopyTo(data, 4);
        BitConverter.GetBytes(1f).CopyTo(data, 72);
        BitConverter.GetBytes(nf).CopyTo(data, 76);
        BitConverter.GetBytes(ofsFrames).CopyTo(data, 80);
        BitConverter.GetBytes(nb).CopyTo(data, 84);
        BitConverter.GetBytes(ofsPool).CopyTo(data, 88);
        BitConverter.GetBytes(100 + 4 * nb).CopyTo(data, 92);
        BitConverter.GetBytes(ofsEnd).CopyTo(data, 96);
        for (int i = 0; i < nb; i++) BitConverter.GetBytes(offsets[i]).CopyTo(data, 100 + 4 * i);
        for (int i = 0; i < nb; i++) entries[i].CopyTo(data, 100 + offsets[i]);

        static ushort Q(float v) => (ushort)Math.Round((v + 2f) * 16383f);
        static ushort T(float v) => (ushort)Math.Round((v + 512f) * 64f);
        for (int f = 0; f < nf; f++)
            for (int b = 0; b < nb; b++)
            {
                int idx = f * nb + b;
                int ip = ofsFrames + idx * 3;
                data[ip] = (byte)idx; data[ip + 1] = (byte)(idx >> 8); data[ip + 2] = (byte)(idx >> 16);
                var (q, t) = frames[f, b];
                ushort[] u = { Q(q.W), Q(q.X), Q(q.Y), Q(q.Z), T(t.X), T(t.Y), T(t.Z) };
                for (int k = 0; k < 7; k++) BitConverter.GetBytes(u[k]).CopyTo(data, ofsPool + idx * 14 + 2 * k);
            }
        return data;
    }
}
