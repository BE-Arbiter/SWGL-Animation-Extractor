using System.Numerics;
using Xunit;

namespace SWGLAnimationExtractor.Tests;

public class ComparisonStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cmpstore_" + Guid.NewGuid().ToString("N"));
    public ComparisonStoreTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private GlaSide Side()
    {
        var id = (Quaternion.Identity, Vector3.Zero);
        File.WriteAllBytes(Path.Combine(_dir, "t.gla"),
            TestGla.Build(new[] { "root" }, new[] { -1 }, new[] { Matrix4x4.Identity }, new[,] { { id } }));
        File.WriteAllText(Path.Combine(_dir, "animation.cfg"), "OK 0 1 -1 20\nBAD 0 1 -1 20\n");
        return GlaSide.Load(Path.Combine(_dir, "t.gla"));
    }

    [Fact]
    public void Failure_is_recorded_with_message()
    {
        var side = Side();
        var store = new ComparisonStore((a, da, b, db) =>
            da.Name == "BAD" ? throw new InvalidOperationException("boom") : AnimComparer.Compare(a, da, b, db));
        Assert.Equal(Classification.Pending, store.Get("OK"));
        store.Start(side, side);
        var end = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < end && (store.Get("OK") == Classification.Pending || store.Get("BAD") == Classification.Pending))
            Thread.Sleep(10);
        Assert.Equal(Classification.Identical, store.Get("OK"));
        Assert.Equal(Classification.Failed, store.Get("BAD"));
        Assert.Equal("boom", store.Error("BAD"));
        Assert.Null(store.Result("BAD"));
        store.Clear();
        Assert.Equal(Classification.Pending, store.Get("BAD"));
    }
}
