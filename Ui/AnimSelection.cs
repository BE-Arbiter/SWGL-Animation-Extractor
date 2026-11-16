namespace SWGLAnimationExtractor;

/// <summary>
/// Multi-selection of animation names in a list: click selects one, Ctrl+click toggles one, Shift+click adds the
/// range from the anchor (last plain or Ctrl click) to the clicked row, keeping what was already selected.
/// </summary>
public sealed class AnimSelection
{
    private readonly HashSet<string> _names = new(StringComparer.Ordinal);

    public string? Anchor { get; private set; }
    public int Count => _names.Count;
    public bool Contains(string name) => _names.Contains(name);

    public void Clear()
    {
        _names.Clear();
        Anchor = null;
    }

    /// <summary>Selects only <paramref name="name"/> (or nothing when null) and makes it the anchor.</summary>
    public void SetSingle(string? name)
    {
        _names.Clear();
        Anchor = name;
        if (name != null) _names.Add(name);
    }

    /// <summary>Applies a click on <paramref name="list"/>[<paramref name="index"/>], the list as currently shown.</summary>
    public void Click(IReadOnlyList<AnimDef> list, int index, bool ctrl, bool shift)
    {
        var name = list[index].Name;
        int anchor = -1;
        if (shift && Anchor != null)
            for (int i = 0; i < list.Count; i++)
                if (list[i].Name == Anchor) { anchor = i; break; }

        if (shift && anchor >= 0)
        {
            for (int i = Math.Min(anchor, index); i <= Math.Max(anchor, index); i++) _names.Add(list[i].Name);
        }
        else if (ctrl)
        {
            if (!_names.Remove(name)) _names.Add(name);
            Anchor = name;
        }
        else SetSingle(name);
    }

    /// <summary>The selected animations of <paramref name="all"/>, in its order.</summary>
    public List<AnimDef> Of(IReadOnlyList<AnimDef> all) => all.Where(a => _names.Contains(a.Name)).ToList();
}
