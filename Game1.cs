using System.Collections.Concurrent;
using ImGuiNET;
using Microsoft.Xna.Framework;
using Vec2 = System.Numerics.Vector2;

namespace SWGLAnimationExtractor;

public partial class Game1 : Game
{
    private readonly GraphicsDeviceManager _graphics;
    private ImGuiRenderer _imgui = null!;
    private readonly Settings _settings = Settings.Load();

    public Game1()
    {
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1600,
            PreferredBackBufferHeight = 900,
        };
        IsMouseVisible = true;
        Window.AllowUserResizing = true;
        Window.Title = "SWGL Animation Extractor";
    }

    private readonly ConcurrentQueue<Action> _uiActions = new();
    private string? _uiError;

    /// <summary>Queues work (e.g. from a dialog thread) to run on the UI thread during Update.</summary>
    public void RunOnUi(Action a) => _uiActions.Enqueue(a);

    protected override void LoadContent()
    {
        _imgui = new ImGuiRenderer(this);
        InitSides();
        InitCenter();
    }

    protected override void Update(GameTime gameTime)
    {
        while (_uiActions.TryDequeue(out var a))
        {
            _uiError = null; // a new UI action supersedes the previous failure
            try { a(); } catch (Exception e) { System.Diagnostics.Debug.WriteLine(e); _uiError = e.Message; }
        }
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(30, 30, 34));
        _imgui.BeginFrame(gameTime);
        DrawUi();
        _imgui.EndFrame();
        base.Draw(gameTime);
    }

    private void DrawUi()
    {
        var vp = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(vp.WorkPos);
        ImGui.SetNextWindowSize(vp.WorkSize);
        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove |
                                       ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBringToFrontOnFocus;
        if (!ImGui.Begin("Main", flags)) { ImGui.End(); return; }

        if (ImGui.BeginTable("columns", 3, ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV))
        {
            ImGui.TableSetupColumn("Reference", ImGuiTableColumnFlags.WidthFixed, 300f);
            ImGui.TableSetupColumn("Center", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Compared", ImGuiTableColumnFlags.WidthFixed, 300f);
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); DrawSidePanel(_left, _right, "Reference");
            ImGui.TableNextColumn(); DrawCenter();
            ImGui.TableNextColumn(); DrawSidePanel(_right, _left, "Compared");
            ImGui.EndTable();
        }
        if (_uiError != null) DrawWrapped(Red, _uiError);
        ImGui.End();
        DrawBuilder();
    }
}
