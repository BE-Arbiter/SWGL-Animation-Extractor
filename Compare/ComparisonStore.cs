using System.Collections.Concurrent;

namespace SWGLAnimationExtractor;

/// <summary>Computes the comparison of every common animation in the background.</summary>
public sealed class ComparisonStore
{
    private ConcurrentDictionary<string, AnimComparison> _results = new(StringComparer.Ordinal);
    private int _generation;

    public void Start(GlaSide left, GlaSide right)
    {
        int gen = Interlocked.Increment(ref _generation);
        var results = new ConcurrentDictionary<string, AnimComparison>(StringComparer.Ordinal);
        _results = results;
        var common = AnimSets.Common(left, right);
        Task.Run(() =>
        {
            foreach (var da in common)
            {
                if (gen != Volatile.Read(ref _generation)) return;
                var db = right.Find(da.Name);
                if (db == null) continue;
                try { results[da.Name] = AnimComparer.Compare(left.Gla, da, right.Gla, db); }
                catch (Exception) { /* leave this animation Pending */ }
            }
        });
    }

    public void Clear()
    {
        Interlocked.Increment(ref _generation);
        _results = new ConcurrentDictionary<string, AnimComparison>(StringComparer.Ordinal);
    }

    public AnimComparison? Result(string name) => _results.TryGetValue(name, out var r) ? r : null;

    public Classification Get(string name) => Result(name)?.Class ?? Classification.Pending;
}
