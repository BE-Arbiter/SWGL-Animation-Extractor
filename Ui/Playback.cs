namespace SWGLAnimationExtractor;

/// <summary>Shared looping playback position (normalized time) for the viewers.</summary>
public sealed class Playback
{
    public float Time01;
    public bool Playing = true;
    public float Speed = 1f;

    /// <summary>Advances by <paramref name="dtSeconds"/> of an animation lasting <paramref name="durationSeconds"/>; loops over [0,1).</summary>
    public void Update(double dtSeconds, float durationSeconds)
    {
        if (!Playing || durationSeconds <= 0f) return;
        double t = Time01 + dtSeconds * Speed / durationSeconds;
        t -= Math.Floor(t);
        Time01 = (float)t;
        if (Time01 >= 1f) Time01 = 0f;
    }

    public FrameSource Frame(AnimDef d)
    {
        int last = Math.Max(d.Length - 1, 0);
        float f = Math.Clamp(Time01 * d.Length, 0f, last);
        return new FrameSource(f, d.Length, d.Start);
    }

    /// <summary>Frame used by the difference tabs: same normalized mapping as <see cref="AnimComparer.Sample"/>.</summary>
    public FrameSource NormalizedFrame(AnimDef d) => AnimComparer.AtNormalized(d, Time01);

    public int FrameIndex(AnimDef d) => Math.Clamp((int)MathF.Floor(Time01 * d.Length), 0, Math.Max(d.Length - 1, 0));
}
