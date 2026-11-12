using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using Xunit;

namespace SWGLAnimationExtractor.Tests;

/// <summary>Exports a GLA to XSI, parses the text back the way dotxsi.py does and recomposes the Ghoul2 model space.</summary>
public class XsiRoundTripTests
{
    private sealed class Block
    {
        public string Type = "", Name = "";
        public List<object> Values = new(); // double or string
        public List<Block> Children = new();
    }

    /// <summary>Mirrors dotxsi.parse: commas dropped, "type [name] {" headers, quoted strings, numbers, nesting.</summary>
    private static Block Parse(string text)
    {
        text = text.Substring(text.IndexOf('\n') + 1).Replace(',', ' ');
        var root = new Block();
        var stack = new Stack<Block>();
        stack.Push(root);
        (string type, string name)? pending = null;
        foreach (Match m in Regex.Matches(text, "\"[^\"]*\"|[{}]|[^\\s{}\"]+"))
        {
            string tok = m.Value;
            if (tok == "{")
            {
                Assert.NotNull(pending);
                var b = new Block { Type = pending!.Value.type, Name = pending.Value.name };
                stack.Peek().Children.Add(b);
                stack.Push(b);
                pending = null;
            }
            else if (tok == "}")
            {
                Assert.Null(pending);
                Assert.True(stack.Count > 1);
                stack.Pop();
            }
            else if (tok[0] == '"') stack.Peek().Values.Add(tok[1..^1]);
            else if (double.TryParse(tok, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) stack.Peek().Values.Add(d);
            else if (pending == null) pending = (tok, "");
            else if (pending.Value.name == "") pending = (pending.Value.type, tok);
            else throw new InvalidDataException("unexpected word " + tok);
        }
        Assert.Single(stack);
        return root;
    }

    private sealed class Model
    {
        public string Name = "";
        public Model? Parent;
        public List<Model> Children = new();
        public double[][] Curves = new double[6][]; // rotation x,y,z then translation x,y,z, indexed by frame
        public double[] BasePose = Array.Empty<double>();
    }

    private static Model ReadModel(Block b, Model? parent)
    {
        var m = new Model { Name = b.Name["MDL-".Length..], Parent = parent };
        foreach (var c in b.Children)
        {
            if (c.Type == "SI_FCurve")
            {
                string param = (string)c.Values[1];
                int n = (int)(double)c.Values[5];
                var keys = c.Values.Skip(6).Cast<double>().ToArray();
                Assert.Equal(2 * n, keys.Length);
                var vals = new double[n];
                for (int i = 0; i < n; i++)
                {
                    Assert.Equal(i + 1, keys[2 * i]);
                    vals[i] = keys[2 * i + 1];
                }
                int axis = "XYZ".IndexOf(param[^1]);
                if (param.StartsWith("ROTATION")) m.Curves[axis] = vals;
                else if (param.StartsWith("TRANSLATION")) m.Curves[3 + axis] = vals;
            }
            else if (c.Type == "SI_Transform" && c.Name.StartsWith("BASEPOSE-"))
                m.BasePose = c.Values.Cast<double>().ToArray();
            else if (c.Type == "SI_Model")
                m.Children.Add(ReadModel(c, m));
        }
        return m;
    }

    private static IEnumerable<Model> Walk(Model m)
    {
        yield return m;
        foreach (var c in m.Children)
            foreach (var x in Walk(c)) yield return x;
    }

    private const float DegToRad = MathF.PI / 180f;

    /// <summary>Column-vector T*Rz*Ry*Rx, stored row-vector (Rx*Ry*Rz then translation).</summary>
    private static Matrix4x4 FromEuler(double rx, double ry, double rz, double tx, double ty, double tz)
    {
        var m = Matrix4x4.CreateRotationX((float)rx * DegToRad) * Matrix4x4.CreateRotationY((float)ry * DegToRad)
              * Matrix4x4.CreateRotationZ((float)rz * DegToRad);
        m.Translation = new Vector3((float)tx, (float)ty, (float)tz);
        return m;
    }

    // C = 0.64 * R with R (x,y,z) -> (x,-z,y), column-vector rows; transposed for the row-vector convention.
    private static readonly Matrix4x4 CRow = Matrix4x4.Transpose(new Matrix4x4(
        0.64f, 0, 0, 0,
        0, 0, -0.64f, 0,
        0, 0.64f, 0, 0,
        0, 0, 0, 1));

    private static Matrix4x4 Inv(Matrix4x4 m)
    {
        Assert.True(Matrix4x4.Invert(m, out var r));
        return r;
    }

    /// <summary>Column-vector G = T(0,0,-24) * C * X * C^-1 (or without the lift), in row-vector form.</summary>
    private static Matrix4x4 UndoXsi(Matrix4x4 xRow, bool lift)
    {
        var g = Inv(CRow) * xRow * CRow;
        return lift ? g * Matrix4x4.CreateTranslation(0, 0, -24) : g;
    }

    private static Matrix4x4 Rot(float yaw, float pitch, float roll, Vector3 t)
    {
        var m = Matrix4x4.CreateFromYawPitchRoll(yaw, pitch, roll);
        m.Translation = t;
        return m;
    }

    private static void AssertSame(Matrix4x4 expected, Matrix4x4 actual, string what)
    {
        float dt = Vector3.Distance(expected.Translation, actual.Translation);
        Assert.True(dt < 1e-3f, $"{what}: translation {expected.Translation} vs {actual.Translation}");
        var qa = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(Strip(expected)));
        var qb = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(Strip(actual)));
        float angle = 2f * MathF.Acos(Math.Min(1f, Math.Abs(Quaternion.Dot(qa, qb)))) / DegToRad;
        Assert.True(angle < 0.05f, $"{what}: rotation differs by {angle} deg");
    }

    private static Matrix4x4 Strip(Matrix4x4 m) { m.Translation = Vector3.Zero; return m; }

    [Fact]
    public void Export_roundtrips_to_gla_model_space()
    {
        // hand(0) has parent arm(2), a higher index; chain root -> arm -> hand -> fingers.
        var names = new[] { "hand", "root", "arm", "fingers" };
        var parents = new[] { 2, -1, 1, 0 };
        var rest = new[]
        {
            Rot(0.5f, -0.3f, 0.7f, new Vector3(12, -4, 3)),
            Rot(0f, 0f, 0f, new Vector3(3, -2, 5)) * Matrix4x4.CreateRotationX(15 * DegToRad),
            Rot(-0.4f, 0.2f, 0.9f, new Vector3(-6, 8, 2)),
            Rot(0.8f, 0.1f, -0.6f, new Vector3(2, 5, -9)),
        };
        rest[1].Translation = new Vector3(3, -2, 5);

        const int total = 7, start = 1, len = 6;
        var f = new (Quaternion, Vector3)[total, 4];
        for (int i = 0; i < total; i++)
        {
            // root: pure X rotation from 150 to 222 deg (plus the 15 deg rest): crosses +-180 between two frames.
            f[i, 1] = (Quaternion.CreateFromAxisAngle(Vector3.UnitX, (150 + 12 * i) * DegToRad), new Vector3(0.5f, -0.3f * i, 0.2f * i));
            f[i, 2] = (Quaternion.CreateFromYawPitchRoll(0.05f * i + 0.2f, -0.04f * i, 0.07f * i), new Vector3(1.5f * i, -0.5f, 0.25f * i));
            f[i, 0] = (Quaternion.CreateFromYawPitchRoll(-0.06f * i, 0.05f * i + 0.1f, 0.1f), new Vector3(-0.4f * i, 0.8f, 2f));
            f[i, 3] = (Quaternion.CreateFromYawPitchRoll(0.03f * i, 0.2f, -0.08f * i), new Vector3(0.3f, 0.2f * i, -0.1f * i));
        }
        var gla = GlaFile.Parse(TestGla.Build(names, parents, rest, f));
        var anim = new AnimDef("A", start, len, -1, 20f);

        var xsi = Parse(XsiWriter.Build(gla, anim, "scene"));
        var roots = xsi.Children.Where(c => c.Type == "SI_Model").Select(c => ReadModel(c, null)).ToList();
        Assert.Single(roots);
        var models = Walk(roots[0]).ToDictionary(m => m.Name);
        Assert.Equal(4, models.Count);

        // Nesting reproduces the bone hierarchy.
        for (int b = 0; b < 4; b++)
            Assert.Equal(gla.Bones[b].Parent < 0 ? null : names[gla.Bones[b].Parent], models[names[b]].Parent?.Name);

        // Euler keys stay continuous (no 360 jumps) even though the root passes +-180.
        foreach (var m in models.Values)
            for (int k = 0; k < 3; k++)
                for (int i = 1; i < len; i++)
                    Assert.True(Math.Abs(m.Curves[k][i] - m.Curves[k][i - 1]) < 90, $"{m.Name} rot {k} jumps at key {i}");
        Assert.True(models["root"].Curves[0].Max() > 180 || models["root"].Curves[0].Min() < -180 ||
                    Math.Abs(models["root"].Curves[0][^1] - models["root"].Curves[0][0]) > 60);

        for (int fr = 0; fr < len; fr++)
        {
            var expected = gla.ModelSpace(new FrameSource(fr, len, start));
            var worldXsi = new Dictionary<string, Matrix4x4>();
            Matrix4x4 World(Model m)
            {
                if (worldXsi.TryGetValue(m.Name, out var w)) return w;
                var local = FromEuler(m.Curves[0][fr], m.Curves[1][fr], m.Curves[2][fr], m.Curves[3][fr], m.Curves[4][fr], m.Curves[5][fr]);
                w = m.Parent == null ? local : local * World(m.Parent);
                return worldXsi[m.Name] = w;
            }
            for (int b = 0; b < 4; b++)
                AssertSame(expected[b], UndoXsi(World(models[names[b]]), lift: true), $"{names[b]} frame {fr}");
        }

        var restModel = gla.RestModelSpace();
        for (int b = 0; b < 4; b++)
        {
            var v = models[names[b]].BasePose;
            Assert.Equal(9, v.Length);
            AssertSame(restModel[b], UndoXsi(FromEuler(v[3], v[4], v[5], v[6], v[7], v[8]), lift: false), $"BASEPOSE {names[b]}");
        }
    }
}
