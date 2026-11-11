using ImGuiNET;
using Microsoft.Xna.Framework;
using Vec2 = System.Numerics.Vector2;

namespace SWGLAnimationExtractor;

public partial class Game1
{
    private const int TabReference = 0, TabDiffView = 1, TabDiff3D = 2, TabCompared = 3;
    private static readonly string[] CenterTabNames =
        { "Reference Animation", "Differences (View)", "Differences (3D)", "Compared Animation" };

    private SkeletonViewer _refViewer = null!;
    private SkeletonViewer _cmpViewer = null!;
    private readonly OrbitCamera _camera = new(); // shared by every viewer
    private readonly Playback _playback = new();
    private GlmModel? _glm;
    private bool _showGlm = true;
    private string? _glmError;
    private int _centerTab;
    private GlaFile? _fittedFor;

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
                        default: ImGui.TextDisabled("Not implemented yet."); break; // Task 8
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
        var id = viewer.Render(w, h, s.Side.Gla, frame, color, -1, _showGlm ? _glm : null);
        CameraInput.Image(id, new Vec2(w, h), viewer.Camera);
    }

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
        ImGui.TextUnformatted(anim != null && anim.Length > 0 ? $"Frame {_playback.FrameIndex(anim) + 1} / {anim.Length}" : "Frame - / -");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(120f);
        float speed = _playback.Speed;
        if (ImGui.SliderFloat("Speed", ref speed, 0.1f, 2f)) _playback.Speed = speed;
    }
}
