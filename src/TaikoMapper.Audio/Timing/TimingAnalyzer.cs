using TaikoMapper.Audio.Onsets;
using TaikoMapper.Domain.Timing;

namespace TaikoMapper.Audio.Timing;

/// <summary>
/// Timing detection from the onset envelope. Two tiers:
/// <list type="bullet">
/// <item><b>Tier 1</b> (<see cref="Analyze"/>) — one known tempo: precise global offset, then the beat
/// <b>phase</b> is tracked in sliding windows and re-anchored (same BPM, corrected offset) when it
/// drifts, so a slightly-off BPM stays aligned the whole way through.</item>
/// <item><b>Tier 2</b> (<see cref="AnalyzeMultiTempo"/>) — detects tempo <i>changes</i>: windowed tempo
/// estimation + an octave-robust change-point split into tempo regions, each then handled by Tier 1.</item>
/// </list>
/// Both phase- and tempo-tracking are deliberately conservative (continuous unwrapping, smoothing,
/// hysteresis) so per-window jitter on real audio does not invent segments.
/// </summary>
public sealed class TimingAnalyzer(
    double windowSeconds = 8.0,
    double stepSeconds = 2.0,
    double driftFraction = 0.28,
    double minSegmentSeconds = 12.0,
    TempoEstimator? tempo = null)
{
    // re-anchor when |drift| exceeds this fraction of a beat
    // hysteresis: minimum gap between re-anchors
    private readonly TempoEstimator _tempo = tempo ?? new TempoEstimator();

    // Tier-2 (tempo-change) knobs. Tempo changes are coarse, so the windows are large and a change must
    // be both sizable and sustained — most songs are single-tempo and must stay one region.
    private const double TempoWindowSeconds = 8.0;
    private const double TempoStepSeconds = 4.0;
    private const double BpmChangeThreshold = 8.0; // a new tempo must differ from the region mean by this
    private const double BpmTolerance = 6.0;       // ...and the confirming windows must agree within this
    private const int MinStableWindows = 2;        // ...over at least this many windows

    private const double Smoothing = 0.25; // EMA factor for the tracked phase

    /// <summary>Tier 1: one tempo throughout — detect the offset and re-anchor it on drift.</summary>
    public IReadOnlyList<TimingSegment> Analyze(OnsetEnvelope odf, double bpm, int beatsPerMeasure = 4)
    {
        ArgumentNullException.ThrowIfNull(odf);
        if (bpm <= 0 || !double.IsFinite(bpm))
            throw new ArgumentOutOfRangeException(nameof(bpm));

        var segments = new List<TimingSegment>();
        AnchorRegion(odf, 0, odf.Flux.Length, bpm, beatsPerMeasure, isFirst: true, segments);
        return segments;
    }

    /// <summary>
    /// Tier 2: detect tempo changes (using <paramref name="bpmPrior"/> — the song's dominant tempo — to
    /// resolve octave ambiguity), splitting into tempo regions that each get the Tier-1 offset+drift
    /// treatment. A single-tempo song yields one region, identical to <see cref="Analyze"/>.
    /// </summary>
    public IReadOnlyList<TimingSegment> AnalyzeMultiTempo(OnsetEnvelope odf, double bpmPrior, int beatsPerMeasure = 4)
    {
        ArgumentNullException.ThrowIfNull(odf);
        if (bpmPrior <= 0 || !double.IsFinite(bpmPrior))
            throw new ArgumentOutOfRangeException(nameof(bpmPrior));

        var segments = new List<TimingSegment>();
        var regions = DetectTempoRegions(odf, bpmPrior);
        for (var k = 0; k < regions.Count; k++)
            AnchorRegion(odf, regions[k].Start, regions[k].End, regions[k].Bpm, beatsPerMeasure, isFirst: k == 0, segments);
        return segments;
    }

    /// <summary>
    /// Emits the segments for one tempo region [<paramref name="regionStart"/>, <paramref name="regionEnd"/>):
    /// a starting segment (the global offset for the first region; the first downbeat at/after the boundary
    /// otherwise), then drift re-anchors within the region.
    /// </summary>
    private void AnchorRegion(OnsetEnvelope odf, int regionStart, int regionEnd, double bpm, int meter, bool isFirst, List<TimingSegment> segments)
    {
        var flux = odf.Flux;
        var frameRate = odf.FrameRate;
        var periodFrames = 60.0 * frameRate / bpm;
        var beatMs = 60_000.0 / bpm;

        var regionOffset = BestPhaseMs(flux, regionStart, regionEnd, periodFrames, odf);
        var phase = Mod(regionOffset, beatMs);
        var regionStartMs = odf.FrameToMs(regionStart);
        var segStart = isFirst
            ? regionOffset
            : phase + Math.Ceiling((regionStartMs - phase) / beatMs) * beatMs;

        segments.Add(new TimingSegment(segStart, bpm, meter));
        if (regionEnd <= regionStart)
            return;

        var driftTol = Math.Max(15.0, beatMs * driftFraction);
        var windowFrames = Math.Max(1, (int)(windowSeconds * frameRate));
        var stepFrames = Math.Max(1, (int)(stepSeconds * frameRate));

        // Continuous, smoothed phase tracking (unwrap each estimate to nearest the running value so an
        // off-beat fold flip is not read as drift); only sustained drift triggers a re-anchor.
        var curOffset = segStart;
        var anchorPhase = Mod(segStart, beatMs);
        var tracked = anchorPhase;
        var lastAnchorMs = double.NegativeInfinity;

        for (var c = regionStart + windowFrames / 2; c < regionEnd; c += stepFrames)
        {
            var s0 = Math.Max(regionStart, c - windowFrames / 2);
            var s1 = Math.Min(regionEnd, c + windowFrames / 2);
            var raw = Mod(BestPhaseMs(flux, s0, s1, periodFrames, odf), beatMs);
            var centerMs = odf.FrameToMs(c);

            tracked += Smoothing * WrapHalf(raw - tracked, beatMs);
            var drift = tracked - anchorPhase;

            if (Math.Abs(drift) > driftTol && centerMs - lastAnchorMs >= minSegmentSeconds * 1000.0)
            {
                var beatHere = curOffset + Math.Round((centerMs - curOffset) / beatMs) * beatMs;
                var newOffset = beatHere + drift;
                segments.Add(new TimingSegment(newOffset, bpm, meter));
                curOffset = newOffset;
                anchorPhase = tracked;
                lastAnchorMs = centerMs;
            }
        }
    }

    /// <summary>Splits the song into tempo regions [startFrame, endFrame, bpm] by windowed tempo + change-point.</summary>
    private IReadOnlyList<(int Start, int End, double Bpm)> DetectTempoRegions(OnsetEnvelope odf, double bpmPrior)
    {
        var m = odf.Flux.Length;
        var frameRate = odf.FrameRate;
        var win = Math.Max(1, (int)(TempoWindowSeconds * frameRate));
        var step = Math.Max(1, (int)(TempoStepSeconds * frameRate));
        if (m < win * 2)
            return [(0, m, bpmPrior)];

        // Windowed tempo, median-filtered, then octave-normalised SEQUENTIALLY (each toward the previous)
        // so an octave flip in one window does not register as a tempo change — only genuine within-octave
        // tempo shifts do.
        var centers = new List<int>();
        var raw = new List<double>();
        for (var c = win / 2; c < m; c += step)
        {
            var s0 = Math.Max(0, c - win / 2);
            var s1 = Math.Min(m, c + win / 2);
            var slice = new double[s1 - s0];
            Array.Copy(odf.Flux, s0, slice, 0, slice.Length);
            raw.Add(_tempo.Estimate(new OnsetEnvelope(slice, odf.SampleRate, odf.HopSize, odf.FrameSize)).Bpm);
            centers.Add(c);
        }

        // Windowed tempo on real music is jittery: dense sections make autocorrelation lock onto a simple
        // metrical RATIO of the true tempo (½, ¾, 4/3, 3/2, 2…). Snap any window explained by such a ratio
        // of the song's dominant tempo back to it, so only a genuinely different tempo can start a region.
        var sm = MedianFilter3(raw);
        var nb = new double[sm.Length];
        for (var i = 0; i < sm.Length; i++)
            nb[i] = SnapToPriorMetrically(sm[i], bpmPrior);

        var regions = new List<(int Start, int End, double Bpm)>();
        var regionStartIdx = 0;
        var sum = nb[0];
        var count = 1;
        for (var i = 1; i < nb.Length; i++)
        {
            var mean = sum / count;
            if (Math.Abs(nb[i] - mean) > BpmChangeThreshold && IsSustainedChange(nb, i, mean))
            {
                regions.Add((regionStartIdx == 0 ? 0 : centers[regionStartIdx], centers[i], mean));
                regionStartIdx = i;
                sum = nb[i];
                count = 1;
            }
            else
            {
                sum += nb[i];
                count++;
            }
        }
        regions.Add((regionStartIdx == 0 ? 0 : centers[regionStartIdx], m, sum / count));
        return regions;
    }

    /// <summary>A change is real only if it departs the old level and the next windows agree on the new level.</summary>
    private static bool IsSustainedChange(double[] nb, int i, double oldMean)
    {
        if (i + MinStableWindows - 1 >= nb.Length)
            return false;
        for (var k = 0; k < MinStableWindows; k++)
        {
            if (Math.Abs(nb[i + k] - oldMean) <= BpmChangeThreshold)
                return false; // a window that snapped back to the old level ⇒ jitter, not a change
            if (Math.Abs(nb[i + k] - nb[i]) > BpmTolerance)
                return false; // the new windows disagree ⇒ not a stable new level
        }
        return true;
    }

    /// <summary>Best beat-phase offset (ms) over a frame window, by folding the flux into one beat period.</summary>
    private static double BestPhaseMs(double[] flux, int start, int end, double periodFrames, OnsetEnvelope odf)
    {
        var p = Math.Max(1, (int)Math.Ceiling(periodFrames));
        var fold = new double[p];
        for (var i = Math.Max(0, start); i < Math.Min(flux.Length, end); i++)
            fold[(int)Math.Round(i % periodFrames) % p] += flux[i];

        var best = 0;
        for (var b = 1; b < p; b++)
            if (fold[b] > fold[best])
                best = b;

        var refined = PeakInterpolation.Refine(fold, best, 0, p - 1);
        if (refined < 0.0)
            refined += p;

        return odf.FrameToMs(refined);
    }

    private static double[] MedianFilter3(IReadOnlyList<double> x)
    {
        var y = new double[x.Count];
        for (var i = 0; i < x.Count; i++)
        {
            double a = x[Math.Max(0, i - 1)], b = x[i], c = x[Math.Min(x.Count - 1, i + 1)];
            y[i] = a + b + c - Math.Max(a, Math.Max(b, c)) - Math.Min(a, Math.Min(b, c)); // median of three
        }
        return y;
    }

    private static readonly double[] MetricalRatios = [1.0, 0.5, 2.0, 2.0 / 3.0, 1.5, 0.75, 4.0 / 3.0, 1.0 / 3.0, 3.0];

    /// <summary>
    /// If <paramref name="bpm"/> is within ~3% of a simple metrical ratio of <paramref name="prior"/> it is a
    /// subdivision/grouping artifact of the same tempo ⇒ returns <paramref name="prior"/>. Otherwise it is a
    /// genuinely different tempo ⇒ returns it, octave-normalised toward the prior.
    /// </summary>
    private static double SnapToPriorMetrically(double bpm, double prior)
    {
        if (bpm <= 0 || prior <= 0)
            return bpm;

        var bestErr = double.MaxValue;
        foreach (var r in MetricalRatios)
        {
            var err = Math.Abs(bpm - prior * r);
            if (err < bestErr)
                bestErr = err;
        }
        return bestErr <= prior * 0.03 ? prior : OctaveToward(bpm, prior);
    }

    /// <summary>Multiplies/divides <paramref name="bpm"/> by 2 until it is within √2 of <paramref name="reference"/>.</summary>
    private static double OctaveToward(double bpm, double reference)
    {
        if (bpm <= 0 || reference <= 0)
            return bpm;
        const double root2 = 1.4142135623730951;
        while (bpm < reference / root2)
            bpm *= 2.0;
        while (bpm > reference * root2)
            bpm /= 2.0;
        return bpm;
    }

    private static double Mod(double x, double m) => ((x % m) + m) % m;

    private static double WrapHalf(double x, double m)
    {
        var r = Mod(x, m);
        return r > m / 2.0 ? r - m : r;
    }
}
