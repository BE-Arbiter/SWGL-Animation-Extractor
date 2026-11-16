using Xunit;

namespace SWGLAnimationExtractor.Tests;

public class AnimSelectionTests
{
    private static readonly AnimDef[] Anims =
        Enumerable.Range(1, 15).Select(i => new AnimDef($"anim {i}", i, 1, -1, 20)).ToArray();

    private static string[] Names(AnimSelection s) => s.Of(Anims).Select(a => a.Name).ToArray();

    [Fact]
    public void Ctrl_and_shift_clicks_build_the_expected_set()
    {
        var s = new AnimSelection();
        s.Click(Anims, 0, false, false);   // anim 1
        s.Click(Anims, 4, true, false);    // ctrl anim 5
        s.Click(Anims, 9, false, true);    // shift anim 10
        s.Click(Anims, 12, true, false);   // ctrl anim 13
        s.Click(Anims, 14, false, true);   // shift anim 15
        Assert.Equal(new[] { 1, 5, 6, 7, 8, 9, 10, 13, 14, 15 }.Select(i => $"anim {i}"), Names(s));
    }

    [Fact]
    public void Plain_click_replaces_and_ctrl_click_toggles()
    {
        var s = new AnimSelection();
        s.Click(Anims, 2, false, false);
        s.Click(Anims, 3, true, false);
        s.Click(Anims, 2, true, false);
        Assert.Equal(new[] { "anim 4" }, Names(s));
        s.Click(Anims, 7, false, false);
        Assert.Equal(new[] { "anim 8" }, Names(s));
    }

    [Fact]
    public void Shift_click_works_backwards_and_without_anchor_selects_one()
    {
        var s = new AnimSelection();
        s.Click(Anims, 5, false, true);
        Assert.Equal(new[] { "anim 6" }, Names(s));
        s.Click(Anims, 2, false, true);
        Assert.Equal(new[] { "anim 3", "anim 4", "anim 5", "anim 6" }, Names(s));
    }

    [Fact]
    public void Shift_range_uses_the_list_as_shown()
    {
        var shown = new[] { Anims[0], Anims[4], Anims[9] }; // e.g. a filtered list
        var s = new AnimSelection();
        s.Click(shown, 0, false, false);
        s.Click(shown, 2, false, true);
        Assert.Equal(new[] { "anim 1", "anim 5", "anim 10" }, Names(s));
    }
}
