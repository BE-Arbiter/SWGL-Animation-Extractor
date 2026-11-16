using ImGuiNET;
using Microsoft.Xna.Framework;
using Vec2 = System.Numerics.Vector2;
using Vec4 = System.Numerics.Vector4;

namespace SWGLAnimationExtractor;

public partial class Game1
{
    private const int TabReference = 0, TabDiffView = 1, TabDiffSideBySide = 2, TabDiff3D = 3, TabCompared = 4;
    private static readonly string[] CenterTabNames =
        { "Reference Animation", "Differences (View)", "Differences (Side by Side)", "Differences (3D)", "Compared Animation" };

    private SkeletonViewer _refViewer = null!;
    private SkeletonViewer _cmpViewer = null!;
    private readonly OrbitCamera _camera = new(); // shared by every viewer
    private readonly Playback _playback = new();
    private GlmModel? _glm;
    private bool _showGlm = true;
    private string? _glmError;
    private int _centerTab;
    private GlaFile? _fittedFor;
    private string? _highlightBone; // by name: bone indexes differ between the two GLAs
    private float _wipe = 0.5f;
    private bool _draggingBar;

    private void InitCenter()
    {
        _refViewer = new SkeletonViewer(GraphicsDevice, _imgui) { Camera = _camera };
        _cmpViewer = new SkeletonViewer(GraphicsDevice, _imgui) { Camera = _camera };
        if (!string.IsNullOrWhiteSpace(_settings.Glm))
        {
            if (File.Exists(_settings.Glm)) TryLoadGlm(_settings.Glm);
            else _glmError = $"File not found: {_settings.Glm}";
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refViewer?.Dispose();
            _cmpViewer?.Dispose();
        }
        base.Dispose(disposing);
    }

    private bool TryLoadGlm(string path)
    {
        _glmError = null;
        try
        {
            _glm = GlmModel.Parse(File.ReadAllBytes(path));
            return true;
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException
                                  or ArgumentException or IndexOutOfRangeException or OverflowException)
        {
            _glmError = e.Message;
            return false;
        }
    }

    private static float DurationSeconds(AnimDef d)
    {
        float fps = Math.Abs(d.Fps) < 0.01f ? 20f : Math.Abs(d.Fps);
        return d.Length / fps;
    }

    /// <summary>Animation the playback controls refer to (Reference first in the difference tabs).</summary>
    private AnimDef? DisplayedAnim() => _centerTab switch
    {
        TabCompared => _right.Selected,
        TabReference => _left.Selected,
        _ => _left.Selected ?? _right.Selected,
    };

    private void FitCameraIfNeeded()
    {
        var gla = _left.Side?.Gla ?? _right.Side?.Gla;
        if (gla == null || ReferenceEquals(gla, _fittedFor)) return;
        _fittedFor = gla;
        var (min, max) = SkeletonViewer.RestBounds(gla);
        _camera.Fit(min, max);
    }

    private void DrawCenter()
    {
        FitCameraIfNeeded();

        ImGui.Checkbox("Show GLM", ref _showGlm);
        ImGui.SameLine();
        if (ImGui.Button("Load GLM..."))
        {
            FileDialogs.Open("GLM files (*.glm)", "Load GLM", _settings.Glm, p =>
            {
                if (p == null) return;
                RunOnUi(() =>
                {
                    if (!TryLoadGlm(p)) return;
                    _settings.Glm = p;
                    _settings.Save();
                });
            });
        }
        ImGui.SameLine();
        ImGui.TextDisabled(_glm != null && !string.IsNullOrEmpty(_settings.Glm) ? System.IO.Path.GetFileName(_settings.Glm) : "(no GLM)");
        ImGui.SameLine();
        if (ImGui.Button("Build GLA from XSI...")) OpenBuilder();
        if (_glmError != null) DrawWrapped(Red, _glmError);

        float footer = ImGui.GetFrameHeightWithSpacing() + ImGui.GetStyle().ItemSpacing.Y;
        if (ImGui.BeginTabBar("centerTabs"))
        {
            for (int i = 0; i < CenterTabNames.Length; i++)
            {
                if (!ImGui.BeginTabItem(CenterTabNames[i])) continue;
                _centerTab = i;
                if (ImGui.BeginChild("view", new Vec2(0, -footer)))
                {
                    switch (i)
                    {
                        case TabReference: DrawAnimView(_refViewer, _left, SkeletonViewer.ReferenceColor); break;
                        case TabCompared: DrawAnimView(_cmpViewer, _right, SkeletonViewer.ComparedColor); break;
                        case TabDiffView: DrawDiffView(); break;
                        case TabDiffSideBySide: DrawDiffSideBySide(); break;
                        case TabDiff3D: DrawDiff3D(); break;
                    }
                }
                ImGui.EndChild();
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }

        DrawPlaybackControls();
    }

    private void DrawAnimView(SkeletonViewer viewer, SideState s, Microsoft.Xna.Framework.Color color)
    {
        var anim = s.Selected;
        if (anim == null || s.Side == null) { ImGui.TextUnformatted("No animation selected"); return; }

        var avail = ImGui.GetContentRegionAvail();
        if (viewer.GlmWarning != null) DrawWrapped(Red, viewer.GlmWarning);
        int w = Math.Max((int)avail.X, 16), h = Math.Max((int)(avail.Y - (viewer.GlmWarning != null ? ImGui.GetTextLineHeightWithSpacing() : 0)), 16);
        FrameSource? frame = anim.Length > 0 ? _playback.Frame(anim) : null;
        var id = viewer.Render(w, h, s.Side.Gla, frame, color, HighlightIndex(s.Side.Gla), _showGlm ? _glm : null);
        CameraInput.Image(id, new Vec2(w, h), viewer.Camera);
    }

    /// <summary>Index of the highlighted bone in this GLA (bone indexes differ between GLAs), or -1 when absent.</summary>
    private int HighlightIndex(GlaFile gla) => _highlightBone == null ? -1 : gla.FindBone(_highlightBone);

    /// <summary>Shows the standard hint and returns false unless both sides have a selected animation.</summary>
    private bool RequireBothSelected()
    {
        if (_left.Selected != null && _right.Selected != null && _left.Side != null && _right.Side != null) return true;
        ImGui.TextUnformatted("Select an animation present in both GLAs");
        return false;
    }

    /// <summary>True when that side has a GLA and a selected animation.</summary>
    private static bool HasAnim(SideState s) => s.Side != null && s.Selected != null;

    /// <summary>Renders one side for the difference tabs, or returns IntPtr.Zero when it has no animation.</summary>
    private IntPtr RenderDiffSide(SkeletonViewer viewer, SideState s, int w, int h, Microsoft.Xna.Framework.Color color)
    {
        if (!HasAnim(s)) return IntPtr.Zero;
        var d = s.Selected!;
        FrameSource? f = d.Length > 0 ? _playback.NormalizedFrame(d) : null;
        return viewer.Render(_camera, w, h, s.Side!.Gla, f, color, HighlightIndex(s.Side.Gla), _showGlm ? _glm : null);
    }

    /// <summary>GLM warnings of the sides that are drawn; returns the space left for the images.</summary>
    private Vec2 DrawDiffWarnings(bool a, bool b)
    {
        if (a && _refViewer.GlmWarning != null) DrawWrapped(Red, "Reference: " + _refViewer.GlmWarning);
        if (b && _cmpViewer.GlmWarning != null) DrawWrapped(Red, "Compared: " + _cmpViewer.GlmWarning);
        return ImGui.GetContentRegionAvail();
    }

    private static string MissingText(SideState s) => s.Side == null ? "(no GLA)" : "(no animation)";

    private void DrawDiffView()
    {
        bool hasA = HasAnim(_left), hasB = HasAnim(_right);
        if (!hasA && !hasB) { ImGui.TextUnformatted("No animation selected"); return; }

        var avail = DrawDiffWarnings(hasA, hasB);
        int w = Math.Max((int)avail.X, 16), h = Math.Max((int)avail.Y, 16);
        var idA = RenderDiffSide(_refViewer, _left, w, h, SkeletonViewer.ReferenceColor);
        var idB = RenderDiffSide(_cmpViewer, _right, w, h, SkeletonViewer.ComparedColor);
        // With only one side, the bar is pinned to the edge so that side fills the view and cannot be dragged.
        bool pinned = !(hasA && hasB);
        float split = pinned ? (hasA ? 1f : 0f) : Math.Clamp(_wipe, 0f, 1f);

        // One button over the whole image. A press within grab px of the bar drags the bar, any other press drives the camera.
        const float grab = 5f;
        var pos = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("##viewport", new Vec2(w, h), ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight);
        float mouseX = ImGui.GetIO().MousePos.X;
        bool nearBar = !pinned && Math.Abs(mouseX - (pos.X + split * w)) <= grab;
        if (ImGui.IsItemActivated()) _draggingBar = ImGui.IsMouseDown(ImGuiMouseButton.Left) && nearBar;
        if (!ImGui.IsItemActive() || pinned) _draggingBar = false;

        if (_draggingBar)
        {
            _wipe = Math.Clamp((mouseX - pos.X) / w, 0f, 1f);
            split = _wipe;
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEW);
        }
        else
        {
            if (ImGui.IsItemHovered() && !ImGui.IsItemActive() && nearBar) ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEW);
            CameraInput.Handle(_camera);
        }

        var dl = ImGui.GetWindowDrawList();
        float x = pos.X + split * w;
        if (split > 0f && idA != IntPtr.Zero) dl.AddImage(idA, pos, new Vec2(x, pos.Y + h), new Vec2(0, 0), new Vec2(split, 1));
        if (split < 1f && idB != IntPtr.Zero) dl.AddImage(idB, new Vec2(x, pos.Y), new Vec2(pos.X + w, pos.Y + h), new Vec2(split, 0), new Vec2(1, 1));
        dl.AddRectFilled(new Vec2(x - 1.5f, pos.Y), new Vec2(x + 1.5f, pos.Y + h), 0xFFFFFFFF);

        string refText = hasA ? "Reference" : "Reference " + MissingText(_left);
        string cmpText = hasB ? "Compared" : "Compared " + MissingText(_right);
        DrawLabel(dl, new Vec2(pos.X + 8, pos.Y + 6), refText);
        DrawLabel(dl, new Vec2(pos.X + w - ImGui.CalcTextSize(cmpText).X - 8, pos.Y + 6), cmpText);
    }

    /// <summary>Reference and Compared next to each other, shared camera, synchronized in normalized time.
    /// A side without an animation is left empty.</summary>
    private void DrawDiffSideBySide()
    {
        bool hasA = HasAnim(_left), hasB = HasAnim(_right);
        if (!hasA && !hasB) { ImGui.TextUnformatted("No animation selected"); return; }

        var avail = DrawDiffWarnings(hasA, hasB);
        const float gap = 4f;
        int w = Math.Max((int)((avail.X - gap) / 2), 16);
        int h = Math.Max((int)avail.Y, 16);
        var idA = RenderDiffSide(_refViewer, _left, w, h, SkeletonViewer.ReferenceColor);
        var idB = RenderDiffSide(_cmpViewer, _right, w, h, SkeletonViewer.ComparedColor);

        var posA = ImGui.GetCursorScreenPos();
        DiffHalf("ref", idA, w, h);
        ImGui.SameLine(0, gap);
        var posB = ImGui.GetCursorScreenPos();
        DiffHalf("cmp", idB, w, h);

        var dl = ImGui.GetWindowDrawList();
        DrawLabel(dl, posA + new Vec2(8, 6), hasA ? "Reference" : "Reference " + MissingText(_left));
        DrawLabel(dl, posB + new Vec2(8, 6), hasB ? "Compared" : "Compared " + MissingText(_right));
    }

    /// <summary>One half of the side-by-side view: the image, or an empty frame that still drives the camera.</summary>
    private void DiffHalf(string id, IntPtr texture, int w, int h)
    {
        ImGui.PushID(id);
        if (texture != IntPtr.Zero) CameraInput.Image(texture, new Vec2(w, h), _camera);
        else
        {
            var pos = ImGui.GetCursorScreenPos();
            ImGui.InvisibleButton("##viewport", new Vec2(w, h), ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight);
            CameraInput.Handle(_camera);
            ImGui.GetWindowDrawList().AddRect(pos, pos + new Vec2(w, h), 0x40FFFFFF);
        }
        ImGui.PopID();
    }

    private static void DrawLabel(ImDrawListPtr dl, Vec2 pos, string text)
    {
        dl.AddText(pos + new Vec2(1, 1), 0xFF000000, text);
        dl.AddText(pos, 0xFFFFFFFF, text);
    }

    private static readonly Vec4 Orange = new(1.0f, 0.62f, 0.15f, 1f);

    private void DrawDiff3D()
    {
        if (!RequireBothSelected()) return;
        var a = _left.Selected!;

        var state = _comparison.Get(a.Name);
        if (state == Classification.Failed)
        {
            DrawWrapped(Red, $"Comparison failed: {_comparison.Error(a.Name)}");
            return;
        }
        var r = _comparison.Result(a.Name);
        if (r == null) { ImGui.TextUnformatted("Comparing..."); return; }

        ImGui.TextUnformatted($"Reference: {r.FramesA} frames @ {r.FpsA:0.##} fps");
        ImGui.TextUnformatted($"Compared: {r.FramesB} frames @ {r.FpsB:0.##} fps");
        if (r.FramesA != r.FramesB) DrawWrapped(Orange, $"Lengths differ: compared over {r.Samples} resampled frames.");
        if (r.OnlyInA.Count > 0) DrawWrapped(Orange, $"Bones only in Reference: {string.Join(", ", r.OnlyInA)}");
        if (r.OnlyInB.Count > 0) DrawWrapped(Orange, $"Bones only in Compared: {string.Join(", ", r.OnlyInB)}");

        if (r.Bones.Count == 0) { ImGui.TextUnformatted("No bone differs beyond tolerance."); return; }

        var flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable;
        if (!ImGui.BeginTable("diffBones", 5, flags, new Vec2(0, ImGui.GetContentRegionAvail().Y))) return;
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Bone");
        ImGui.TableSetupColumn("Max angle (°)");
        ImGui.TableSetupColumn("Frame");
        ImGui.TableSetupColumn("Max offset (units)");
        ImGui.TableSetupColumn("Frame");
        ImGui.TableHeadersRow();
        for (int i = 0; i < r.Bones.Count; i++)
        {
            var d = r.Bones[i];
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.PushID(i);
            if (ImGui.Selectable(d.Bone, d.Bone == _highlightBone, ImGuiSelectableFlags.SpanAllColumns))
            {
                _highlightBone = d.Bone;
                _playback.Time01 = d.AngleFrame / (float)Math.Max(r.Samples - 1, 1);
                _playback.Playing = false;
            }
            ImGui.PopID();
            ImGui.TableNextColumn(); ImGui.TextUnformatted(d.MaxAngleDeg.ToString("0.00"));
            ImGui.TableNextColumn(); ImGui.TextUnformatted((d.AngleFrame + 1).ToString());
            ImGui.TableNextColumn(); ImGui.TextUnformatted(d.MaxOffset.ToString("0.00"));
            ImGui.TableNextColumn(); ImGui.TextUnformatted((d.OffsetFrame + 1).ToString());
        }
        ImGui.EndTable();
    }

    /// <summary>Frame index shown in the footer: matches what the active tab renders.</summary>
    private int FooterFrame(AnimDef anim) => _centerTab is TabDiffView or TabDiffSideBySide or TabDiff3D
        ? Math.Clamp((int)MathF.Round(_playback.NormalizedFrame(anim).Frame), 0, anim.Length - 1)
        : _playback.FrameIndex(anim);

    private void DrawPlaybackControls()
    {
        var anim = DisplayedAnim();
        if (anim != null) _playback.Update(ImGui.GetIO().DeltaTime, DurationSeconds(anim));

        if (ImGui.Button(_playback.Playing ? "Pause" : "Play")) _playback.Playing = !_playback.Playing;
        ImGui.SameLine();
        ImGui.SetNextItemWidth(Math.Max(ImGui.GetContentRegionAvail().X - 330f, 80f));
        float t = _playback.Time01;
        if (ImGui.SliderFloat("##time", ref t, 0f, 1f, "")) _playback.Time01 = Math.Min(t, 0.99999f);
        ImGui.SameLine();
        ImGui.TextUnformatted(anim != null && anim.Length > 0 ? $"Frame {FooterFrame(anim) + 1} / {anim.Length}" : "Frame - / -");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(120f);
        float speed = _playback.Speed;
        if (ImGui.SliderFloat("Speed", ref speed, 0.1f, 2f)) _playback.Speed = speed;
    }
}
