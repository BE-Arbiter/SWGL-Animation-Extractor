using Xunit;
using SWGLAnimationExtractor;

namespace SWGLAnimationExtractor.Tests;

public class PlaybackTests
{
    [Fact]
    public void Update_advances_and_wraps()
    {
        var p = new Playback { Speed = 1f };
        p.Update(1.5, 2f);
        p.Update(1.5, 2f);
        Assert.Equal(0.5f, p.Time01, 3);
    }

    [Fact]
    public void Default_speed_is_half()
    {
        var p = new Playback();
        p.Update(1.0, 2f);
        Assert.Equal(0.25f, p.Time01, 3);
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
    public void AtNormalized_maps_time_to_length_minus_one()
    {
        var d = new AnimDef("X", 5, 10, 0, 20f);
        Assert.Equal(0f, AnimComparer.AtNormalized(d, 0f).Frame);
        Assert.Equal(9f, AnimComparer.AtNormalized(d, 1f).Frame);
        Assert.Equal(9f, AnimComparer.AtNormalized(d, 2f).Frame);
        Assert.Equal(0f, AnimComparer.AtNormalized(new AnimDef("Y", 0, 1, 0, 20f), 0.7f).Frame);
        var f = AnimComparer.AtNormalized(d, 0.5f);
        Assert.Equal((10, 5), (f.Count, f.First));
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
