using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace SWGLAnimationExtractor;

public sealed class XsiReadException : Exception
{
    public XsiReadException(string message) : base(message) { }
}

/// <summary>One SI_Model of a dotXSI file: its parent, rest pose and animation curves (all in XSI space).</summary>
public sealed class XsiModel
{
    public string Name = "";
    public int Parent = -1;

    /// <summary>BASEPOSE transform (world, XSI space, row-vector), or null when the file has none.</summary>
    public Matrix4x4? BasePose;

    /// <summary>SRT values (sx sy sz rx ry rz tx ty tz) used for the channels without an FCurve.</summary>
    public float[] Srt = { 1, 1, 1, 0, 0, 0, 0, 0, 0 };

    /// <summary>Rotation x, y, z then translation x, y, z; null when the channel is not animated.</summary>
    public readonly XsiCurve?[] Curves = new XsiCurve?[6];
}

/// <summary>Keys of one animated channel, evaluated linearly between keys and held outside them.</summary>
public sealed class XsiCurve
{
    public float[] Times = Array.Empty<float>();
    public float[] Values = Array.Empty<float>();

    public float Evaluate(float t)
    {
        if (Times.Length == 0) return 0f;
        if (t <= Times[0]) return Values[0];
        if (t >= Times[^1]) return Values[^1];
        int i = Array.BinarySearch(Times, t);
        if (i >= 0) return Values[i];
        i = ~i; // first key after t
        float k = (t - Times[i - 1]) / (Times[i] - Times[i - 1]);
        return Values[i - 1] + (Values[i] - Values[i - 1]) * k;
    }
}

/// <summary>Animation read back from a dotXSI 3.0 text file (as written by <see cref="XsiWriter"/> or the Blender exporter).</summary>
public sealed class XsiAnimation
{
    public List<XsiModel> Models { get; } = new();
    public float StartFrame { get; init; } = 1f;
    public int FrameCount { get; init; } = 1;
    public float Fps { get; init; } = 20f;
    public bool Radians { get; init; }

    public int FindModel(string name)
    {
        for (int i = 0; i < Models.Count; i++)
            if (Models[i].Name == name) return i;
        return -1;
    }

    /// <summary>Local transform of a model (relative to its parent, XSI space, row-vector) at frame index <paramref name="f"/>.</summary>
    public Matrix4x4 Local(int model, int f)
    {
        var m = Models[model];
        float t = StartFrame + f;
        float V(int ch) => m.Curves[ch]?.Evaluate(t) ?? m.Srt[3 + ch];
        return XsiReader.FromEuler(V(0), V(1), V(2), V(3), V(4), V(5), Radians);
    }

    /// <summary>World transform (XSI space, row-vector) of every model at frame index <paramref name="f"/>.</summary>
    public Matrix4x4[] World(int f)
    {
        var w = new Matrix4x4[Models.Count];
        var done = new bool[Models.Count];
        Matrix4x4 Get(int i)
        {
            if (done[i]) return w[i];
            var l = Local(i, f);
            int p = Models[i].Parent;
            w[i] = p < 0 ? l : l * Get(p);
            done[i] = true;
            return w[i];
        }
        for (int i = 0; i < w.Length; i++) Get(i);
        return w;
    }
}

public static class XsiReader
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private sealed class Block
    {
        public string Type = "", Name = "";
        public readonly List<object> Values = new(); // float or string
        public readonly List<Block> Children = new();
    }

    public static XsiAnimation Load(string path) => Parse(File.ReadAllText(path, Encoding.Latin1));

    /// <summary>Column-vector T * Rz * Ry * Rx (XSI/Blender XYZ order), stored row-vector.</summary>
    public static Matrix4x4 FromEuler(float rx, float ry, float rz, float tx, float ty, float tz, bool radians)
    {
        float k = radians ? 1f : MathF.PI / 180f;
        var m = Matrix4x4.CreateRotationX(rx * k) * Matrix4x4.CreateRotationY(ry * k) * Matrix4x4.CreateRotationZ(rz * k);
        m.Translation = new Vector3(tx, ty, tz);
        return m;
    }

    private static Block Tokenize(string text)
    {
        if (!text.StartsWith("xsi ", StringComparison.Ordinal)) throw new XsiReadException("Not a dotXSI text file.");
        text = text.Substring(text.IndexOf('\n') + 1).Replace(',', ' ');
        var root = new Block();
        var stack = new Stack<Block>();
        stack.Push(root);
        string? type = null, name = null;
        foreach (Match m in Regex.Matches(text, "\"[^\"]*\"|[{}]|[^\\s{}\"]+"))
        {
            string tok = m.Value;
            if (tok == "{")
            {
                var b = new Block { Type = type ?? "", Name = name ?? "" };
                stack.Peek().Children.Add(b);
                stack.Push(b);
                type = name = null;
            }
            else if (tok == "}")
            {
                if (stack.Count == 1) throw new XsiReadException("Unbalanced braces.");
                stack.Pop();
                type = name = null;
            }
            else if (tok[0] == '"') stack.Peek().Values.Add(tok[1..^1]);
            else if (float.TryParse(tok, NumberStyles.Float, Inv, out var d)) stack.Peek().Values.Add(d);
            else if (type == null) type = tok;
            else name ??= tok;
        }
        if (stack.Count != 1) throw new XsiReadException("The file is truncated (unclosed block).");
        return root;
    }

    private static float Num(Block b, int i) =>
        i < b.Values.Count && b.Values[i] is float f ? f : throw new XsiReadException($"{b.Type} {b.Name}: number expected.");

    private static string StripPrefix(string name) => name.StartsWith("MDL-", StringComparison.Ordinal) ? name[4..] : name;

    public static XsiAnimation Parse(string text)
    {
        var root = Tokenize(text);
        var all = new List<Block>();
        void Collect(Block b) { all.Add(b); foreach (var c in b.Children) Collect(c); }
        Collect(root);

        var scene = all.FirstOrDefault(b => b.Type == "SI_Scene");
        float start = 1f, end = 1f, fps = 20f;
        if (scene != null && scene.Values.Count >= 4)
        {
            start = Num(scene, 1);
            end = Num(scene, 2);
            fps = Num(scene, 3);
        }
        var angle = all.FirstOrDefault(b => b.Type == "SI_Angle");
        bool radians = angle != null && angle.Values.Count > 0 && angle.Values[0] is float a && a == 1f;

        var anim = new XsiAnimation
        {
            StartFrame = start,
            FrameCount = Math.Max(1, (int)MathF.Round(end - start) + 1),
            Fps = fps,
            Radians = radians,
        };

        void ReadModel(Block b, int parent)
        {
            int index = anim.Models.Count;
            var model = new XsiModel { Name = StripPrefix(b.Name), Parent = parent };
            anim.Models.Add(model);
            foreach (var c in b.Children)
            {
                if (c.Type == "SI_Transform" && c.Values.Count >= 9)
                {
                    var v = Enumerable.Range(0, 9).Select(i => Num(c, i)).ToArray();
                    if (c.Name.StartsWith("BASEPOSE", StringComparison.Ordinal))
                        model.BasePose = FromEuler(v[3], v[4], v[5], v[6], v[7], v[8], radians);
                    else if (c.Name.StartsWith("SRT", StringComparison.Ordinal))
                        model.Srt = v;
                }
                else if (c.Type == "SI_Model") ReadModel(c, index);
            }
        }
        foreach (var b in root.Children.Where(b => b.Type == "SI_Model")) ReadModel(b, -1);
        if (anim.Models.Count == 0) throw new XsiReadException("The file has no SI_Model.");

        // FCurves may sit inside their model or anywhere else: they name their model.
        foreach (var c in all.Where(b => b.Type == "SI_FCurve")) ReadCurve(anim, c);
        return anim;
    }

    private static void ReadCurve(XsiAnimation anim, Block c)
    {
        if (c.Values.Count < 6 || c.Values[0] is not string model || c.Values[1] is not string param) return;
        int mi = anim.FindModel(StripPrefix(model));
        if (mi < 0) return;
        int first;
        if (param.StartsWith("ROTATION", StringComparison.Ordinal)) first = 0;
        else if (param.StartsWith("TRANSLATION", StringComparison.Ordinal)) first = 3;
        else return; // scaling is not supported by the GLA format

        int numCurves = (int)Num(c, 3), numKeys = (int)Num(c, 5);
        int axis = param.Length >= 2 && param[^2] == '-' ? "XYZ".IndexOf(param[^1]) : -1;
        if (numKeys <= 0) return;
        int rest = c.Values.Count - 6;
        if (rest % numKeys != 0) throw new XsiReadException($"FCurve {c.Name}: unexpected key layout.");
        int stride = rest / numKeys;
        if (numCurves < 1 || stride < 1 + numCurves) throw new XsiReadException($"FCurve {c.Name}: unexpected key layout.");
        int width = (stride - 1) / numCurves; // value (+ tangents for HERMITE/CUBIC)

        for (int k = 0; k < numCurves; k++)
        {
            int ax = numCurves == 1 ? axis : k;
            if (ax < 0 || ax > 2) continue;
            var curve = new XsiCurve { Times = new float[numKeys], Values = new float[numKeys] };
            for (int i = 0; i < numKeys; i++)
            {
                curve.Times[i] = Num(c, 6 + i * stride);
                curve.Values[i] = Num(c, 6 + i * stride + 1 + k * width);
            }
            Array.Sort(curve.Times, curve.Values);
            anim.Models[mi].Curves[first + ax] = curve;
        }
    }
}
