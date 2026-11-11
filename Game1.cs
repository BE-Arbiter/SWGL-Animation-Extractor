using ImGuiNET;
using Microsoft.Xna.Framework;
using Vec2 = System.Numerics.Vector2;

namespace SWGLAnimationExtractor;

public class Game1 : Game
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

    protected override void LoadContent() => _imgui = new ImGuiRenderer(this);

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
            ImGui.TableNextColumn(); ImGui.TextUnformatted("Reference");
            ImGui.TableNextColumn(); ImGui.TextUnformatted("Center");
            ImGui.TableNextColumn(); ImGui.TextUnformatted("Compared");
            ImGui.EndTable();
        }
        ImGui.End();
    }
}
