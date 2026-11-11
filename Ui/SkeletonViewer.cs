using System.Numerics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Color = Microsoft.Xna.Framework.Color;
using XVec3 = Microsoft.Xna.Framework.Vector3;
using Vector3 = System.Numerics.Vector3;

namespace SWGLAnimationExtractor;

/// <summary>Renders a GLA skeleton (and optionally a skinned GLM mesh) into its own render target shown through ImGui.</summary>
public sealed class SkeletonViewer : IDisposable
{
    public static readonly Color ReferenceColor = new(0.35f, 0.65f, 1.0f);
    public static readonly Color ComparedColor = new(1.0f, 0.55f, 0.25f);
    private static readonly Color Highlight = new(1.0f, 0.9f, 0.2f);

    private readonly GraphicsDevice _gd;
    private readonly ImGuiRenderer _ui;
    private readonly BasicEffect _lineEffect;
    private readonly BasicEffect _meshEffect;
    private RenderTarget2D? _rt;
    private IntPtr _rtId;
    private VertexPositionColor[] _lines = Array.Empty<VertexPositionColor>();
    private GlmModel? _workModel;
    private VertexPositionNormalTexture[][] _work = Array.Empty<VertexPositionNormalTexture[]>();

    /// <summary>Camera used by the overload without a camera argument; assign one camera to several viewers to share the view.</summary>
    public OrbitCamera Camera { get; set; } = new();

    /// <summary>Set by the last render when the GLM could not be drawn.</summary>
    public string? GlmWarning { get; private set; }

    public SkeletonViewer(GraphicsDevice gd, ImGuiRenderer ui)
    {
        _gd = gd; _ui = ui;
        _lineEffect = new BasicEffect(gd) { VertexColorEnabled = true, LightingEnabled = false, TextureEnabled = false };
        _meshEffect = new BasicEffect(gd) { VertexColorEnabled = false, LightingEnabled = true, TextureEnabled = false };
    }

    /// <summary>Bounding box of the rest-pose bone positions.</summary>
    public static (XVec3 min, XVec3 max) RestBounds(GlaFile gla)
    {
        var min = new XVec3(float.MaxValue);
        var max = new XVec3(float.MinValue);
        foreach (var m in gla.RestModelSpace())
        {
            var p = ToXna(m.Translation);
            min = XVec3.Min(min, p); max = XVec3.Max(max, p);
        }
        if (min.X > max.X) { min = -XVec3.One; max = XVec3.One; }
        return (min, max);
    }

    private static XVec3 ToXna(Vector3 v) => new(v.X, v.Y, v.Z);

    public IntPtr Render(int width, int height, GlaFile? gla, FrameSource? frame, Color boneColor, int highlightBone, GlmModel? glm) =>
        Render(Camera, width, height, gla, frame, boneColor, highlightBone, glm);

    /// <summary>Renders with an explicit (possibly shared) camera into the (re)sized target and returns its ImGui texture id.</summary>
    public IntPtr Render(OrbitCamera camera, int width, int height, GlaFile? gla, FrameSource? frame, Color boneColor, int highlightBone, GlmModel? glm)
    {
        width = Math.Max(width, 16);
        height = Math.Max(height, 16);
        if (_rt == null || _rt.Width != width || _rt.Height != height)
        {
            if (_rt != null) { _ui.UnbindTexture(_rtId); _rt.Dispose(); }
            _rt = new RenderTarget2D(_gd, width, height, false, SurfaceFormat.Color, DepthFormat.Depth24);
            _rtId = _ui.BindTexture(_rt);
        }

        GlmWarning = null;
        _gd.SetRenderTarget(_rt);
        _gd.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, new Color(40, 40, 48), 1f, 0);
        if (gla != null)
        {
            var view = camera.View();
            var proj = camera.Projection(width / (float)height);
            Matrix4x4[] model = frame is { } f ? gla.ModelSpace(f) : gla.RestModelSpace();

            if (glm != null)
            {
                if (glm.NumBones == gla.Bones.Count)
                {
                    var skin = frame is { } f2 ? gla.Pose(f2) : null;
                    DrawMesh(glm, skin, view, proj, camera.Direction);
                }
                else GlmWarning = $"GLM bone count ({glm.NumBones}) does not match the GLA ({gla.Bones.Count}).";
            }
            DrawSkeleton(gla, model, boneColor, highlightBone, view, proj, camera.Radius);
        }
        _gd.SetRenderTarget(null);
        return _rtId;
    }

    private void DrawSkeleton(GlaFile gla, Matrix4x4[] model, Color color, int highlight, Matrix view, Matrix proj, float radius)
    {
        int n = gla.Bones.Count;
        if (n == 0) return;
        if (_lines.Length < n * 8) _lines = new VertexPositionColor[n * 8];
        var verts = _lines;
        int c = 0;
        float s = Math.Max(radius * 0.012f, 0.2f);
        for (int i = 0; i < n; i++)
        {
            var p = ToXna(model[i].Translation);
            int parent = gla.Bones[i].Parent;
            var col = i == highlight ? Highlight : color;
            if (parent >= 0 && parent < n)
            {
                var lc = i == highlight ? Highlight : color;
                verts[c++] = new VertexPositionColor(ToXna(model[parent].Translation), lc);
                verts[c++] = new VertexPositionColor(p, lc);
            }
            verts[c++] = new VertexPositionColor(p - XVec3.UnitX * s, col); verts[c++] = new VertexPositionColor(p + XVec3.UnitX * s, col);
            verts[c++] = new VertexPositionColor(p - XVec3.UnitY * s, col); verts[c++] = new VertexPositionColor(p + XVec3.UnitY * s, col);
            verts[c++] = new VertexPositionColor(p - XVec3.UnitZ * s, col); verts[c++] = new VertexPositionColor(p + XVec3.UnitZ * s, col);
        }

        _lineEffect.World = Matrix.Identity;
        _lineEffect.View = view;
        _lineEffect.Projection = proj;
        _gd.BlendState = BlendState.Opaque;
        _gd.DepthStencilState = DepthStencilState.None; // skeleton stays visible through the mesh
        _gd.RasterizerState = RasterizerState.CullNone;
        foreach (var pass in _lineEffect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _gd.DrawUserPrimitives(PrimitiveType.LineList, verts, 0, c / 2);
        }
    }

    private void DrawMesh(GlmModel glm, Matrix4x4[]? skin, Matrix view, Matrix proj, XVec3 dir)
    {
        if (!ReferenceEquals(_workModel, glm))
        {
            _workModel = glm;
            _work = glm.Surfaces.Select(s => (VertexPositionNormalTexture[])s.Verts.Clone()).ToArray();
        }

        _meshEffect.World = Matrix.Identity;
        _meshEffect.View = view;
        _meshEffect.Projection = proj;
        _meshEffect.DiffuseColor = new XVec3(0.62f);
        _meshEffect.AmbientLightColor = new XVec3(0.45f);
        _meshEffect.DirectionalLight0.Enabled = true;
        _meshEffect.DirectionalLight0.Direction = -dir; // headlight
        _meshEffect.DirectionalLight0.DiffuseColor = new XVec3(0.75f);
        _meshEffect.DirectionalLight0.SpecularColor = XVec3.Zero;
        _meshEffect.DirectionalLight1.Enabled = false;
        _meshEffect.DirectionalLight2.Enabled = false;
        _gd.BlendState = BlendState.Opaque;
        _gd.DepthStencilState = DepthStencilState.Default;
        _gd.RasterizerState = RasterizerState.CullNone;

        for (int si = 0; si < glm.Surfaces.Count; si++)
        {
            var s = glm.Surfaces[si];
            if (s.Hidden || s.Indices.Length == 0) continue;
            var work = _work[si];
            Skin(s, work, skin);
            foreach (var pass in _meshEffect.CurrentTechnique.Passes)
            {
                pass.Apply();
                _gd.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, work, 0, work.Length, s.Indices, 0, s.Indices.Length / 3);
            }
        }
    }

    /// <summary>Skins the bind-pose vertices with the pose matrices (null = bind pose).</summary>
    private static void Skin(GlmSurface s, VertexPositionNormalTexture[] work, Matrix4x4[]? mats)
    {
        if (mats == null) { Array.Copy(s.Verts, work, work.Length); return; }
        for (int v = 0; v < work.Length; v++)
        {
            var src = s.Verts[v];
            var p = new Vector3(src.Position.X, src.Position.Y, src.Position.Z);
            var nrm = new Vector3(src.Normal.X, src.Normal.Y, src.Normal.Z);
            Vector3 pos = default, nor = default;
            for (int k = 0; k < 4; k++)
            {
                float w = s.Weight[v * 4 + k];
                if (w <= 0) continue;
                int b = s.Bone[v * 4 + k];
                if (b < 0 || b >= mats.Length) continue;
                pos += w * Vector3.Transform(p, mats[b]);
                nor += w * Vector3.TransformNormal(nrm, mats[b]);
            }
            work[v].Position = new XVec3(pos.X, pos.Y, pos.Z);
            if (nor.LengthSquared() > 1e-8f) nor = Vector3.Normalize(nor);
            work[v].Normal = new XVec3(nor.X, nor.Y, nor.Z);
        }
    }

    public void Dispose()
    {
        if (_rt != null) { _ui.UnbindTexture(_rtId); _rt.Dispose(); _rt = null; }
        _lineEffect.Dispose();
        _meshEffect.Dispose();
    }
}
