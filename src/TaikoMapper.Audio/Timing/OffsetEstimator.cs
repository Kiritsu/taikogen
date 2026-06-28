using TaikoMapper.Audio.Onsets;

namespace TaikoMapper.Audio.Timing;

/// <summary>
/// Estimates the beat-grid phase (offset of the first beat) given a tempo, by
/// finding the phase whose pulse train best aligns with onset strength.
/// </summary>
/// <remarks>
/// Resolution is bounded by the ODF hop (e.g. ~5.8 ms at 44.1 kHz / hop 256);
/// a parabolic refinement gives sub-frame precision. Manual offset override
/// bypasses this entirely.
/// </remarks>
public sealed class OffsetEstimator
{
    public double EstimateOffsetMs(OnsetEnvelope odf, double bpm)
    {
        ArgumentNullException.ThrowIfNull(odf);
        if (bpm <= 0 || !double.IsFinite(bpm))
            throw new ArgumentOutOfRangeException(nameof(bpm));

        var d = odf.Flux;
        var m = d.Length;
        var periodFrames = 60.0 * odf.FrameRate / bpm;
        var phaseCount = Math.Max(1, (int)Math.Ceiling(periodFrames));

        if (m == 0)
            return 0.0;

        var phaseScores = new double[phaseCount];
        var bestPhase = 0;

        for (var phase = 0; phase < phaseCount; phase++)
        {
            var acc = 0.0;
            for (double pos = phase; pos < m; pos += periodFrames)
            {
                var idx = (int)Math.Round(pos);
                if (idx >= 0 && idx < m)
                    acc += d[idx];
            }

            phaseScores[phase] = acc;
            if (acc > phaseScores[bestPhase])
                bestPhase = phase;
        }

        var refinedPhase = PeakInterpolation.Refine(phaseScores, bestPhase, 0, phaseCount - 1);
        if (refinedPhase < 0.0)
            refinedPhase += periodFrames;

        return odf.FrameToMs(refinedPhase);
    }
}
