using System.Numerics;
using Xunit;

namespace SWGLAnimationExtractor.Tests;

public class AnimSetsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "animsets_" + Guid.NewGuid().ToString("N"));
    public AnimSetsTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private GlaSide Side(string sub, params string[] names)
    {
        var dir = Path.Combine(_dir, sub);
        Directory.CreateDirectory(dir);
        var id = (Quaternion.Identity, Vector3.Zero);
        File.WriteAllBytes(Path.Combine(dir, "t.gla"),
            TestGla.Build(new[] { "root" }, new[] { -1 }, new[] { Matrix4x4.Identity }, new[,] { { id } }));
        File.WriteAllText(Path.Combine(dir, "animation.cfg"),
            string.Join("\n", names.Select((n, i) => $"{n} {i} 1 -1 20")) + "\n");
        return GlaSide.Load(Path.Combine(dir, "t.gla"));
    }

    [Fact]
    public void Common_and_exclusive_split_by_name()
    {
        var l = Side("l", "A", "B", "C");
        var r = Side("r", "B", "C", "D");
        Assert.Equal(new[] { "B", "C" }, AnimSets.Common(l, r).Select(a => a.Name));
        Assert.Equal(new[] { "A" }, AnimSets.Exclusive(l, r).Select(a => a.Name));
        Assert.Equal(new[] { "D" }, AnimSets.Exclusive(r, l).Select(a => a.Name));
        Assert.Empty(AnimSets.Common(l, null));
        Assert.Equal(new[] { "A", "B", "C" }, AnimSets.Exclusive(l, null).Select(a => a.Name));
    }
}
