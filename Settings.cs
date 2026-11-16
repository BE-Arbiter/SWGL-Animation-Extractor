using System.Text.Json;

namespace SWGLAnimationExtractor;

public sealed class Settings
{
    public string? LeftGla { get; set; }
    public string? RightGla { get; set; }
    public string? Glm { get; set; }
    public string? ExportDir { get; set; }
    public string? BuildXsiDir { get; set; }
    public string? BuildOutput { get; set; }
    public string? BuildSkeleton { get; set; }
    /// <summary>The GLA builder writes "&lt;gla name&gt;.cfg" instead of animation.cfg.</summary>
    public bool BuildCfgNamed { get; set; }

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SWGLAnimationExtractor", "settings.json");

    public static Settings Load()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new(); }
        catch { return new(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch { /* non-fatal */ }
    }
}
