using System.Collections.Concurrent;
using System.Diagnostics;

namespace SWGLAnimationExtractor;

/// <summary>Computes the comparison of every common animation in the background.</summary>
public sealed class ComparisonStore
{
    private sealed class State
    {
        public readonly ConcurrentDictionary<string, AnimComparison> Results = new(StringComparer.Ordinal);
        public readonly ConcurrentDictionary<string, string> Errors = new(StringComparer.Ordinal);
    }

    private readonly Func<GlaFile, AnimDef, GlaFile, AnimDef, AnimComparison> _compare;
    private volatile State _state = new();
    private int _generation;

    public ComparisonStore(Func<GlaFile, AnimDef, GlaFile, AnimDef, AnimComparison>? compare = null) =>
        _compare = compare ?? AnimComparer.Compare;

    public void Start(GlaSide left, GlaSide right)
    {
        int gen = Interlocked.Increment(ref _generation);
        var state = new State();
        _state = state;
        var common = AnimSets.Common(left, right);
        Task.Run(() =>
        {
            foreach (var da in common)
            {
                if (gen != Volatile.Read(ref _generation)) return;
                var db = right.Find(da.Name);
                if (db == null) continue;
                try { state.Results[da.Name] = _compare(left.Gla, da, right.Gla, db); }
                catch (Exception e)
                {
                    Debug.WriteLine($"Comparison of '{da.Name}' failed: {e}");
                    state.Errors[da.Name] = e.Message;
                }
            }
        });
    }

    public void Clear()
    {
        Interlocked.Increment(ref _generation);
        _state = new State();
    }

    public AnimComparison? Result(string name) => _state.Results.TryGetValue(name, out var r) ? r : null;

    /// <summary>Error message when the comparison of <paramref name="name"/> failed, otherwise null.</summary>
    public string? Error(string name) => _state.Errors.TryGetValue(name, out var e) ? e : null;

    /// <summary>Pending until computed; Failed (see <see cref="Error"/>) when the computation threw.</summary>
    public Classification Get(string name)
    {
        var s = _state;
        if (s.Results.TryGetValue(name, out var r)) return r.Class;
        return s.Errors.ContainsKey(name) ? Classification.Failed : Classification.Pending;
    }
}
