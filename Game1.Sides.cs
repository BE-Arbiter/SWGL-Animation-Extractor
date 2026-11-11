using ImGuiNET;
using Vec2 = System.Numerics.Vector2;
using Vec4 = System.Numerics.Vector4;

namespace SWGLAnimationExtractor;

public sealed class SideState
{
    public GlaSide? Side;
    public string Path = "";
    public string? Error;
    public string? Status;
    public bool StatusIsError;
    public AnimDef? Selected;
    public int Tab;
}

public partial class Game1
{
    private readonly SideState _left = new();
    private readonly SideState _right = new();
    private readonly ComparisonStore _comparison = new();

    private static readonly Vec4 Red = new(0.95f, 0.3f, 0.3f, 1f);
    private static readonly Vec4 Green = new(0.35f, 0.85f, 0.35f, 1f);
    private static readonly string[] TabNames = { "All Animations", "Common Animations", "Exclusive Animations" };

    private void InitSides()
    {
        LoadInitial(_left, _settings.LeftGla);
        LoadInitial(_right, _settings.RightGla);
        UpdateComparison();
    }

    private void LoadInitial(SideState s, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        s.Path = path;
        TryLoad(s);
    }

    private bool TryLoad(SideState s)
    {
        s.Error = null;
        s.Status = null;
        try
        {
            s.Side = GlaSide.Load(s.Path.Trim());
            s.Selected = null;
            return true;
        }
        catch (GlaLoadException e)
        {
            s.Error = e.Message;
            return false;
        }
    }

    private void LoadSide(SideState s)
    {
        if (!TryLoad(s)) { UpdateComparison(); return; }
        if (ReferenceEquals(s, _left)) _settings.LeftGla = s.Path.Trim(); else _settings.RightGla = s.Path.Trim();
        _settings.Save();
        UpdateComparison();
    }

    private void UpdateComparison()
    {
        if (_left.Side != null && _right.Side != null) _comparison.Start(_left.Side, _right.Side);
        else _comparison.Clear();
        // A reload can remove the animation that the other side had selected.
        foreach (var s in new[] { _left, _right })
            if (s.Selected != null && s.Side?.Find(s.Selected.Name) == null) s.Selected = null;
    }

    private void Select(SideState s, AnimDef a)
    {
        var other = ReferenceEquals(s, _left) ? _right : _left;
        s.Selected = a;
        other.Selected = other.Side?.Find(a.Name);
    }

    private static Vec4? ColorFor(Classification c) => c switch
    {
        Classification.Identical => new Vec4(0.35f, 0.85f, 0.35f, 1f),
        Classification.Minor => new Vec4(1.0f, 0.62f, 0.15f, 1f),
        Classification.Major => new Vec4(0.95f, 0.3f, 0.3f, 1f),
        Classification.Exclusive => new Vec4(0.6f, 0.6f, 0.6f, 1f),
        Classification.Failed => new Vec4(0.85f, 0.4f, 0.85f, 1f),
        _ => null,
    };

    private void DrawSidePanel(SideState s, SideState other, string title)
    {
        ImGui.PushID(title);
        ImGui.TextUnformatted(title);

        float btn = 60f;
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - btn - ImGui.GetStyle().ItemSpacing.X);
        string path = s.Path;
        if (ImGui.InputText("##path", ref path, 1024, ImGuiInputTextFlags.EnterReturnsTrue))
        {
            s.Path = path;
            LoadSide(s);
        }
        else s.Path = path;
        ImGui.SameLine();
        if (ImGui.Button("Open", new Vec2(btn, 0)))
        {
            FileDialogs.Open("GLA files (*.gla)", $"Open {title} GLA", s.Path, p =>
            {
                if (p == null) return;
                RunOnUi(() => { s.Path = p; LoadSide(s); });
            });
        }
        if (s.Error != null) ImGui.TextColored(Red, s.Error);

        if (ImGui.BeginTabBar("tabs"))
        {
            for (int i = 0; i < TabNames.Length; i++)
            {
                if (!ImGui.BeginTabItem(TabNames[i])) continue;
                s.Tab = i;
                float footer = ImGui.GetFrameHeightWithSpacing() * 2 + ImGui.GetStyle().ItemSpacing.Y;
                if (ImGui.BeginChild("list", new Vec2(0, -footer)))
                    DrawAnimList(s, other, i);
                ImGui.EndChild();
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }

        DrawExport(s);
        ImGui.PopID();
    }

    private void DrawAnimList(SideState s, SideState other, int tab)
    {
        if (s.Side == null) return;
        IReadOnlyList<AnimDef> list = tab switch
        {
            1 => AnimSets.Common(s.Side, other.Side),
            2 => AnimSets.Exclusive(s.Side, other.Side),
            _ => s.Side.Anims,
        };
        bool both = other.Side != null;
        foreach (var a in list)
        {
            var c = Classification.Pending;
            if (both)
                c = other.Side!.Find(a.Name) == null ? Classification.Exclusive : _comparison.Get(a.Name);
            var color = ColorFor(c);
            if (color != null) ImGui.PushStyleColor(ImGuiCol.Text, color.Value);
            bool sel = s.Selected != null && s.Selected.Name == a.Name;
            if (ImGui.Selectable($"{a.Name} ({a.Length})", sel)) Select(s, a);
            if (color != null) ImGui.PopStyleColor();
            if (c == Classification.Failed && ImGui.IsItemHovered())
                ImGui.SetTooltip(_comparison.Error(a.Name) ?? "Comparison failed");
        }
    }

    private void DrawExport(SideState s)
    {
        ImGui.BeginDisabled(s.Selected == null || s.Side == null);
        if (ImGui.Button("Export XSI..."))
        {
            var gla = s.Side!.Gla;
            var anim = s.Selected!;
            FileDialogs.Save("dotXSI files (*.xsi)", "Export XSI", _settings.ExportDir, anim.Name + ".xsi", "xsi", p =>
            {
                if (p == null) return;
                RunOnUi(() =>
                {
                    try
                    {
                        XsiWriter.Write(p, gla, anim);
                        _settings.ExportDir = System.IO.Path.GetDirectoryName(p);
                        _settings.Save();
                        s.Status = $"Exported {System.IO.Path.GetFileName(p)}";
                        s.StatusIsError = false;
                    }
                    catch (Exception e) when (e is XsiExportException or IOException or UnauthorizedAccessException)
                    {
                        s.Status = e.Message;
                        s.StatusIsError = true;
                    }
                });
            });
        }
        ImGui.EndDisabled();
        if (s.Status != null) ImGui.TextColored(s.StatusIsError ? Red : Green, s.Status);
    }
}
