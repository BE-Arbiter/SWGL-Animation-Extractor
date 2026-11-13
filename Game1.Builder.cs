using ImGuiNET;
using Vec2 = System.Numerics.Vector2;

namespace SWGLAnimationExtractor;

/// <summary>"Build GLA" window: an ordered list of .xsi files turned into a .gla and its animation.cfg.</summary>
public partial class Game1
{
    private sealed class BuildItem
    {
        public string Path = "";
        public string Name = "";
        public int Loop = -1;
        public float Fps = 20f;
        public XsiAnimation? Anim;
        public string? Error;
        public bool Loading = true;
    }

    private readonly List<BuildItem> _buildItems = new();
    private bool _builderOpen;
    private int _buildSelected = -1;
    private string _buildOutput = "";
    private bool _buildUseTemplate = true;
    private string _buildTemplatePath = "";
    private GlaSide? _buildTemplate;
    private string? _buildTemplateError;
    private string _buildGlaName = "";
    private bool _building;
    private string? _buildStatus;
    private bool _buildStatusError;
    private IReadOnlyList<string> _buildWarnings = Array.Empty<string>();
    private string? _buildDone; // path of the last GLA built

    private void OpenBuilder()
    {
        if (!_builderOpen && _buildTemplate == null)
        {
            _buildOutput = _settings.BuildOutput ?? "";
            var t = _settings.BuildSkeleton ?? _left.Side?.GlaPath;
            if (!string.IsNullOrWhiteSpace(t)) LoadBuildTemplate(t);
        }
        _builderOpen = true;
    }

    private void LoadBuildTemplate(string path)
    {
        _buildTemplatePath = path;
        _buildTemplateError = null;
        _buildTemplate = null;
        try
        {
            _buildTemplate = GlaSide.Load(path.Trim());
            _buildGlaName = _buildTemplate.Gla.Name;
            foreach (var it in _buildItems.Where(i => !i.Loading)) ApplyTemplateCfg(it);
            _settings.BuildSkeleton = path.Trim();
            _settings.Save();
        }
        catch (GlaLoadException e) { _buildTemplateError = e.Message; }
    }

    /// <summary>Loop and fps from the template's animation.cfg when it has an animation of that name.</summary>
    private void ApplyTemplateCfg(BuildItem it)
    {
        var d = _buildTemplate?.Find(it.Name);
        if (d != null) { it.Loop = d.Loop; it.Fps = d.Fps; }
        else if (it.Anim != null) it.Fps = it.Anim.Fps;
    }

    private void AddBuildFiles(IEnumerable<string> paths)
    {
        foreach (var p in paths)
        {
            if (_buildItems.Any(i => string.Equals(i.Path, p, StringComparison.OrdinalIgnoreCase))) continue;
            var it = new BuildItem { Path = p, Name = System.IO.Path.GetFileNameWithoutExtension(p).Replace(' ', '_') };
            _buildItems.Add(it);
            Task.Run(() =>
            {
                XsiAnimation? anim = null;
                string? err = null;
                try { anim = XsiReader.Load(p); }
                catch (Exception e) when (e is XsiReadException or IOException or UnauthorizedAccessException) { err = e.Message; }
                RunOnUi(() =>
                {
                    it.Loading = false;
                    it.Anim = anim;
                    it.Error = err;
                    ApplyTemplateCfg(it);
                });
            });
        }
    }

    private void DrawBuilder()
    {
        if (!_builderOpen) return;
        ImGui.SetNextWindowSize(new Vec2(900, 620), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Build GLA from XSI", ref _builderOpen)) { ImGui.End(); return; }

        DrawBuilderSkeleton();
        ImGui.Separator();
        DrawBuilderList();
        ImGui.Separator();
        DrawBuilderOutput();
        ImGui.End();
    }

    private void DrawBuilderSkeleton()
    {
        ImGui.TextUnformatted("Skeleton");
        if (ImGui.RadioButton("Template GLA (same bones, order and bind pose)", _buildUseTemplate)) _buildUseTemplate = true;
        ImGui.SameLine();
        if (ImGui.RadioButton("From the first XSI", !_buildUseTemplate)) _buildUseTemplate = false;

        if (_buildUseTemplate)
        {
            float btn = 70f;
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - btn - ImGui.GetStyle().ItemSpacing.X);
            string p = _buildTemplatePath;
            if (ImGui.InputText("##tpl", ref p, 1024, ImGuiInputTextFlags.EnterReturnsTrue)) LoadBuildTemplate(p);
            else _buildTemplatePath = p;
            ImGui.SameLine();
            if (ImGui.Button("Browse##tpl", new Vec2(btn, 0)))
                FileDialogs.Open("GLA files (*.gla)", "Template GLA", _buildTemplatePath, r =>
                {
                    if (r != null) RunOnUi(() => LoadBuildTemplate(r));
                });
            if (_buildTemplateError != null) DrawWrapped(Red, _buildTemplateError);
            else if (_buildTemplate != null)
                ImGui.TextDisabled($"{_buildTemplate.Gla.Bones.Count} bones, {_buildTemplate.Anims.Count} animations in its cfg (loop/fps reused for the same names)");
        }
        ImGui.SetNextItemWidth(400);
        ImGui.InputTextWithHint("Internal GLA name", "e.g. models/players/_humanoid/_humanoid", ref _buildGlaName, 63);
    }

    private void DrawBuilderList()
    {
        if (ImGui.Button("Add XSI..."))
            FileDialogs.OpenMany("dotXSI files (*.xsi)", "Add XSI animations", _settings.BuildXsiDir ?? _settings.ExportDir, paths =>
            {
                if (paths.Length == 0) return;
                RunOnUi(() =>
                {
                    _settings.BuildXsiDir = System.IO.Path.GetDirectoryName(paths[0]);
                    _settings.Save();
                    AddBuildFiles(paths.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
                });
            });
        ImGui.SameLine();
        bool sel = _buildSelected >= 0 && _buildSelected < _buildItems.Count;
        ImGui.BeginDisabled(!sel || _buildSelected == 0);
        if (ImGui.ArrowButton("##up", ImGuiDir.Up)) Move(_buildSelected, _buildSelected - 1);
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(!sel || _buildSelected == _buildItems.Count - 1);
        if (ImGui.ArrowButton("##down", ImGuiDir.Down)) Move(_buildSelected, _buildSelected + 1);
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(!sel);
        if (ImGui.Button("Remove"))
        {
            _buildItems.RemoveAt(_buildSelected);
            _buildSelected = Math.Min(_buildSelected, _buildItems.Count - 1);
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(_buildItems.Count < 2);
        if (ImGui.Button("Sort by name"))
        {
            var cur = sel ? _buildItems[_buildSelected] : null;
            _buildItems.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            _buildSelected = cur == null ? -1 : _buildItems.IndexOf(cur);
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(_buildItems.Count == 0);
        if (ImGui.Button("Clear")) { _buildItems.Clear(); _buildSelected = -1; }
        ImGui.EndDisabled();
        ImGui.SameLine();
        int total = _buildItems.Sum(i => i.Anim?.FrameCount ?? 0);
        ImGui.TextDisabled($"{_buildItems.Count} animation(s), {total} frames. Drag a row to reorder.");

        float footer = ImGui.GetFrameHeightWithSpacing() * 3 + ImGui.GetTextLineHeightWithSpacing() * (2 + Math.Min(_buildWarnings.Count, 4));
        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable;
        if (!ImGui.BeginTable("buildList", 6, flags, new Vec2(0, -footer))) return;
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed, 30);
        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch, 2);
        ImGui.TableSetupColumn("Start", ImGuiTableColumnFlags.WidthFixed, 50);
        ImGui.TableSetupColumn("Frames", ImGuiTableColumnFlags.WidthFixed, 50);
        ImGui.TableSetupColumn("Loop", ImGuiTableColumnFlags.WidthFixed, 90);
        ImGui.TableSetupColumn("FPS", ImGuiTableColumnFlags.WidthFixed, 90);
        ImGui.TableHeadersRow();

        int start = 0;
        (int from, int to)? move = null;
        var names = _buildItems.GroupBy(i => i.Name, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        for (int i = 0; i < _buildItems.Count; i++)
        {
            var it = _buildItems[i];
            ImGui.PushID(i);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            if (ImGui.Selectable($"{i + 1}", _buildSelected == i,
                    ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowOverlap))
                _buildSelected = i;
            if (ImGui.IsItemActive() && !ImGui.IsItemHovered())
            {
                float dy = ImGui.GetMouseDragDelta(ImGuiMouseButton.Left).Y;
                int to = i + (dy < 0 ? -1 : 1);
                if (to >= 0 && to < _buildItems.Count && Math.Abs(dy) > ImGui.GetTextLineHeight() * 0.5f)
                {
                    move = (i, to);
                    ImGui.ResetMouseDragDelta();
                }
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(it.Path);

            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(-1);
            bool bad = names.Contains(it.Name) || it.Name.Length == 0 || it.Name.Any(char.IsWhiteSpace);
            if (bad) ImGui.PushStyleColor(ImGuiCol.Text, Red);
            string name = it.Name;
            if (ImGui.InputText("##name", ref name, 64)) { it.Name = name; ApplyTemplateCfg(it); }
            if (bad) ImGui.PopStyleColor();

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(it.Anim != null ? start.ToString() : "");
            ImGui.TableNextColumn();
            if (it.Loading) ImGui.TextDisabled("...");
            else if (it.Error != null)
            {
                ImGui.TextColored(Red, "error");
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(it.Error);
            }
            else ImGui.TextUnformatted(it.Anim!.FrameCount.ToString());
            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(-1);
            ImGui.InputInt("##loop", ref it.Loop, 0);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("-1: no loop, 0: loop the whole animation, n: loop the last n frames");
            ImGui.TableNextColumn();
            ImGui.SetNextItemWidth(-1);
            ImGui.InputFloat("##fps", ref it.Fps, 0, 0, "%.3g");
            start += it.Anim?.FrameCount ?? 0;
            ImGui.PopID();
        }
        ImGui.EndTable();
        if (move is var (f, t)) Move(f, t);
    }

    private void Move(int from, int to)
    {
        if (from < 0 || to < 0 || from >= _buildItems.Count || to >= _buildItems.Count) return;
        (_buildItems[from], _buildItems[to]) = (_buildItems[to], _buildItems[from]);
        _buildSelected = to;
    }

    private void DrawBuilderOutput()
    {
        float btn = 70f;
        ImGui.TextUnformatted("Output GLA");
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - btn - ImGui.GetStyle().ItemSpacing.X);
        ImGui.InputText("##out", ref _buildOutput, 1024);
        ImGui.SameLine();
        if (ImGui.Button("Browse##out", new Vec2(btn, 0)))
        {
            string? dir = string.IsNullOrWhiteSpace(_buildOutput) ? null : System.IO.Path.GetDirectoryName(_buildOutput);
            string file = string.IsNullOrWhiteSpace(_buildOutput) ? "animations.gla" : System.IO.Path.GetFileName(_buildOutput);
            FileDialogs.Save("GLA files (*.gla)", "Output GLA", dir, file, "gla", r =>
            {
                if (r != null) RunOnUi(() => _buildOutput = r);
            });
        }
        string cfg = string.IsNullOrWhiteSpace(_buildOutput) ? "" :
            System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(_buildOutput.Trim())) ?? "", "animation.cfg");
        if (cfg.Length > 0)
            ImGui.TextDisabled(File.Exists(cfg)
                ? "animation.cfg is written next to the GLA (the existing one is saved as animation.cfg.bak)."
                : "animation.cfg is written next to the GLA.");

        string? why = _building ? "Building..."
            : _buildItems.Count == 0 ? "Add XSI files."
            : _buildItems.Any(i => i.Loading) ? "Reading XSI files..."
            : _buildItems.Any(i => i.Error != null) ? "Remove the XSI files that failed to load."
            : string.IsNullOrWhiteSpace(_buildOutput) ? "Choose the output GLA."
            : _buildUseTemplate && _buildTemplate == null ? "Choose a template GLA."
            : null;
        ImGui.BeginDisabled(why != null);
        if (ImGui.Button("Build GLA + animation.cfg")) StartBuild(cfg);
        ImGui.EndDisabled();
        if (why != null) { ImGui.SameLine(); ImGui.TextDisabled(why); }
        if (_buildDone != null && !_building)
        {
            ImGui.SameLine();
            if (ImGui.Button("Load as Reference")) { _left.Path = _buildDone; LoadSide(_left); }
            ImGui.SameLine();
            if (ImGui.Button("Load as Compared")) { _right.Path = _buildDone; LoadSide(_right); }
        }
        if (_buildStatus != null) DrawWrapped(_buildStatusError ? Red : Green, _buildStatus);
        foreach (var w in _buildWarnings.Take(4)) DrawWrapped(Orange, w);
        if (_buildWarnings.Count > 4) ImGui.TextDisabled($"... and {_buildWarnings.Count - 4} more warning(s)");
    }

    private void StartBuild(string cfgPath)
    {
        string output = _buildOutput.Trim();
        var entries = _buildItems.Select(i => new GlaBuildEntry(i.Name.Trim(), i.Anim!, i.Loop, i.Fps)).ToList();
        var template = _buildUseTemplate ? _buildTemplate?.Gla : null;
        string glaName = _buildGlaName.Trim();
        if (glaName.Length == 0) glaName = template?.Name ?? System.IO.Path.GetFileNameWithoutExtension(output);
        _settings.BuildOutput = output;
        _settings.Save();
        _building = true;
        _buildStatus = null;
        _buildWarnings = Array.Empty<string>();
        _buildDone = null;
        Task.Run(() =>
        {
            try
            {
                var baseSkel = template != null ? GlaSkeleton.FromGla(template) : GlaSkeleton.FromXsi(entries[0].Anim, glaName);
                var skel = new GlaSkeleton { Name = glaName, Scale = baseSkel.Scale, Bones = baseSkel.Bones };
                var r = GlaBuilder.Build(skel, entries);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(output))!);
                if (File.Exists(cfgPath)) File.Copy(cfgPath, cfgPath + ".bak", overwrite: true);
                File.WriteAllBytes(output, r.Gla);
                File.WriteAllText(cfgPath, r.Cfg, System.Text.Encoding.ASCII);
                RunOnUi(() =>
                {
                    _building = false;
                    _buildStatus = $"Built {System.IO.Path.GetFileName(output)}: {entries.Count} animation(s), {r.NumFrames} frames, {skel.Bones.Count} bones.";
                    _buildStatusError = false;
                    _buildWarnings = r.Warnings;
                    _buildDone = output;
                });
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine(e);
                RunOnUi(() =>
                {
                    _building = false;
                    _buildStatus = e.Message;
                    _buildStatusError = true;
                });
            }
        });
    }
}
