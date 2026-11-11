using Xunit;
using SWGLAnimationExtractor;

namespace SWGLAnimationExtractor.Tests;

public class PlaybackTests
{
    [Fact]
    public void Update_advances_and_wraps()
    {
        var p = new Playback();
        p.Update(1.5, 2f);
        p.Update(1.5, 2f);
        Assert.Equal(0.5f, p.Time01, 3);
    }

    [Fact]
    public void Paused_does_not_advance()
    {
        var p = new Playback { Playing = false };
        p.Update(1.0, 2f);
        Assert.Equal(0f, p.Time01);
    }

    [Fact]
    public void Zero_duration_does_not_advance()
    {
        var p = new Playback();
        p.Update(1.0, 0f);
        Assert.Equal(0f, p.Time01);
    }

    [Fact]
    public void Frame_is_clamped_to_last()
    {
        var p = new Playback { Time01 = 0.9999f };
        var d = new AnimDef("X", 5, 10, 0, 20f);
        Assert.Equal(9, p.FrameIndex(d));
        var f = p.Frame(d);
        Assert.Equal((10, 5), (f.Count, f.First));
        Assert.True(f.Frame <= 9f);
    }
}
