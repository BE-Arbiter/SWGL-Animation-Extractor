using Xunit;
using System.Text.Json;
using SWGLAnimationExtractor;

namespace SWGLAnimationExtractor.Tests;

public class SmokeTests
{
    [Fact]
    public void Settings_roundtrip_through_json()
    {
        var s = new Settings { LeftGla = "a.gla", RightGla = "b.gla", Glm = "m.glm", ExportDir = "out" };
        var r = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(s))!;
        Assert.Equal(("a.gla", "b.gla", "m.glm", "out"), (r.LeftGla, r.RightGla, r.Glm, r.ExportDir));
    }
}
