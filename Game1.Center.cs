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

    private void DrawDiffView()
    {
        if (!RequireBothSelected()) return;
        var a = _left.Selected!;
        var b = _right.Selected!;

        if (_refViewer.GlmWarning != null) DrawWrapped(Red, "Reference: " + _refViewer.GlmWarning);
        if (_cmpViewer.GlmWarning != null) DrawWrapped(Red, "Compared: " + _cmpViewer.GlmWarning);
        var avail = ImGui.GetContentRegionAvail();
        int warnings = (_refViewer.GlmWarning != null ? 1 : 0) + (_cmpViewer.GlmWarning != null ? 1 : 0);
        int w = Math.Max((int)avail.X, 16), h = Math.Max((int)(avail.Y - warnings * ImGui.GetTextLineHeightWithSpacing()), 16);
        var glm = _showGlm ? _glm : null;
        FrameSource? fa = a.Length > 0 ? _playback.NormalizedFrame(a) : null;
        FrameSource? fb = b.Length > 0 ? _playback.NormalizedFrame(b) : null;
        var idA = _refViewer.Render(_camera, w, h, _left.Side!.Gla, fa, SkeletonViewer.ReferenceColor, HighlightIndex(_left.Side.Gla), glm);
        var idB = _cmpViewer.Render(_camera, w, h, _right.Side!.Gla, fb, SkeletonViewer.ComparedColor, HighlightIndex(_right.Side.Gla), glm);

        // One button over the whole image. A press within grab px of the bar drags the bar, any other press drives the camera.
        const float grab = 5f;
        var pos = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("##viewport", new Vec2(w, h), ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight);
        float mouseX = ImGui.GetIO().MousePos.X;
        bool nearBar = Math.Abs(mouseX - (pos.X + Math.Clamp(_wipe, 0f, 1f) * w)) <= grab;
        if (ImGui.IsItemActivated()) _draggingBar = ImGui.IsMouseDown(ImGuiMouseButton.Left) && nearBar;
        if (!ImGui.IsItemActive()) _draggingBar = false;

        if (_draggingBar)
        {
            _wipe = Math.Clamp((mouseX - pos.X) / w, 0f, 1f);
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEW);
        }
        else
        {
            if (ImGui.IsItemHovered() && !ImGui.IsItemActive() && nearBar) ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEW);
            CameraInput.Handle(_camera);
        }

        var dl = ImGui.GetWindowDrawList();
        float split = Math.Clamp(_wipe, 0f, 1f);
        float x = pos.X + split * w;
        if (split > 0f) dl.AddImage(idA, pos, new Vec2(x, pos.Y + h), new Vec2(0, 0), new Vec2(split, 1));
        if (split < 1f) dl.AddImage(idB, new Vec2(x, pos.Y), new Vec2(pos.X + w, pos.Y + h), new Vec2(split, 0), new Vec2(1, 1));
        dl.AddRectFilled(new Vec2(x - 1.5f, pos.Y), new Vec2(x + 1.5f, pos.Y + h), 0xFFFFFFFF);

        var refPos = new Vec2(pos.X + 8, pos.Y + 6);
        var cmpPos = new Vec2(pos.X + w - ImGui.CalcTextSize("Compared").X - 8, pos.Y + 6);
        DrawLabel(dl, refPos, "Reference");
        DrawLabel(dl, cmpPos, "Compared");
    }

    /// <summary>Reference and Compared next to each other, shared camera, synchronized in normalized time.</summary>
    private void DrawDiffSideBySide()
    {
        if (!RequireBothSelected()) return;
        var a = _left.Selected!;
        var b = _right.Selected!;

        if (_refViewer.GlmWarning != null) DrawWrapped(Red, "Reference: " + _refViewer.GlmWarning);
        if (_cmpViewer.GlmWarning != null) DrawWrapped(Red, "Compared: " + _cmpViewer.GlmWarning);
        var avail = ImGui.GetContentRegionAvail();
        int warnings = (_refViewer.GlmWarning != null ? 1 : 0) + (_cmpViewer.GlmWarning != null ? 1 : 0);
        const float gap = 4f;
        int w = Math.Max((int)((avail.X - gap) / 2), 16);
        int h = Math.Max((int)(avail.Y - warnings * ImGui.GetTextLineHeightWithSpacing()), 16);
        var glm = _showGlm ? _glm : null;
        FrameSource? fa = a.Length > 0 ? _playback.NormalizedFrame(a) : null;
        FrameSource? fb = b.Length > 0 ? _playback.NormalizedFrame(b) : null;
        var idA = _refViewer.Render(_camera, w, h, _left.Side!.Gla, fa, SkeletonViewer.ReferenceColor, HighlightIndex(_left.Side.Gla), glm);
        var idB = _cmpViewer.Render(_camera, w, h, _right.Side!.Gla, fb, SkeletonViewer.ComparedColor, HighlightIndex(_right.Side.Gla), glm);

        var posA = ImGui.GetCursorScreenPos();
        ImGui.PushID("ref");
        CameraInput.Image(idA, new Vec2(w, h), _camera);
        ImGui.PopID();
        ImGui.SameLine(0, gap);
        var posB = ImGui.GetCursorScreenPos();
        ImGui.PushID("cmp");
        CameraInput.Image(idB, new Vec2(w, h), _camera);
        ImGui.PopID();

        var dl = ImGui.GetWindowDrawList();
        DrawLabel(dl, posA + new Vec2(8, 6), "Reference");
        DrawLabel(dl, posB + new Vec2(8, 6), "Compared");
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
