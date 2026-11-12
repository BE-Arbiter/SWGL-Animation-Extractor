using System.Numerics;

namespace SWGLAnimationExtractor;

public enum Classification { Pending, Identical, Minor, Major, Exclusive, Failed }

public record BoneDiff(string Bone, float MaxAngleDeg, int AngleFrame, float MaxOffset, int OffsetFrame);

public sealed record AnimComparison(
    Classification Class, int FramesA, int FramesB, float FpsA, float FpsB, int Samples,
    float MeanAngleDeg, IReadOnlyList<BoneDiff> Bones, IReadOnlyList<string> OnlyInA, IReadOnlyList<string> OnlyInB);

public static class AnimComparer
{
    public static FrameSource Sample(AnimDef d, int i, int samples)
    {
        float t = samples == 1 ? 0f : i / (samples - 1f);
        return AtNormalized(d, t);
    }

    /// <summary>Frame at normalized time t in [0,1]: Frame = t * (Length - 1), as used by the comparison.</summary>
    public static FrameSource AtNormalized(AnimDef d, float t) =>
        new FrameSource(Math.Clamp(t, 0f, 1f) * Math.Max(d.Length - 1, 0), d.Length, d.Start);

    private static Quaternion Rotation(Matrix4x4 m)
    {
        m.Translation = Vector3.Zero;
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(m));
    }

    public static AnimComparison Compare(GlaFile a, AnimDef da, GlaFile b, AnimDef db)
    {
        if (da.Length <= 0 || db.Length <= 0)
            throw new ArgumentException($"Animation '{(da.Length <= 0 ? da.Name : db.Name)}' has no frames.");
        if (da.Start < 0 || da.Start + da.Length > a.NumFrames)
            throw new ArgumentException($"Animation frames exceed the GLA ({a.NumFrames} frames).");
        if (db.Start < 0 || db.Start + db.Length > b.NumFrames)
            throw new ArgumentException($"Animation frames exceed the GLA ({b.NumFrames} frames).");
        int samples = Math.Max(da.Length, db.Length);
        var common = new List<(int ia, int ib)>();
        var onlyA = new List<string>();
        var onlyB = new List<string>();
        for (int i = 0; i < a.Bones.Count; i++)
        {
            int j = b.FindBone(a.Bones[i].Name);
            if (j >= 0) common.Add((i, j)); else onlyA.Add(a.Bones[i].Name);
        }
        for (int j = 0; j < b.Bones.Count; j++)
            if (a.FindBone(b.Bones[j].Name) < 0) onlyB.Add(b.Bones[j].Name);

        if (common.Count == 0)
            throw new ArgumentException("No bone in common between the two GLAs.");

        var maxAngle = new float[common.Count];
        var angleFrame = new int[common.Count];
        var maxOff = new float[common.Count];
        var offFrame = new int[common.Count];
        double sum = 0;
        long count = 0;

        for (int s = 0; s < samples; s++)
        {
            var ma = a.ModelSpace(Sample(da, s, samples));
            var mb = b.ModelSpace(Sample(db, s, samples));
            for (int k = 0; k < common.Count; k++)
            {
                var (ia, ib) = common[k];
                float dot = Math.Min(1f, Math.Abs(Quaternion.Dot(Rotation(ma[ia]), Rotation(mb[ib]))));
                float angle = 2f * MathF.Acos(dot) * (180f / MathF.PI);
                float off = Vector3.Distance(ma[ia].Translation, mb[ib].Translation);
                sum += angle; count++;
                if (angle > maxAngle[k]) { maxAngle[k] = angle; angleFrame[k] = s; }
                if (off > maxOff[k]) { maxOff[k] = off; offFrame[k] = s; }
            }
        }

        float mean = count == 0 ? 0f : (float)(sum / count);
        var bones = new List<BoneDiff>();
        for (int k = 0; k < common.Count; k++)
            if (maxAngle[k] > Thresholds.AngleDeg || maxOff[k] > Thresholds.Offset)
                bones.Add(new BoneDiff(a.Bones[common[k].ia].Name, maxAngle[k], angleFrame[k], maxOff[k], offFrame[k]));
        bones = bones.OrderByDescending(x => x.MaxAngleDeg).ThenByDescending(x => x.MaxOffset)
            .ThenBy(x => x.Bone, StringComparer.Ordinal).ToList();

        var cls = da.Length == db.Length && bones.Count == 0 ? Classification.Identical
            : mean >= Thresholds.MajorMeanAngleDeg ? Classification.Major : Classification.Minor;
        return new AnimComparison(cls, da.Length, db.Length, da.Fps, db.Fps, samples, mean, bones, onlyA, onlyB);
    }
}
