using TaikoMapper.Audio.Onsets;
using TaikoMapper.Domain.Rhythm;
using TaikoMapper.Domain.Timing;

namespace TaikoMapper.Ml.Representation;

/// <summary>
/// Builds the per-tick conditioning features the model reads, aligned 1:1 with a
/// <see cref="TokenizedMap"/>'s tokens. All features are derived from the audio analysis and the
/// grid, and per-map normalized to roughly [0, 1] / [-1, 1]. Author is NOT here — it is a learned
/// embedding indexed by id.
/// </summary>
/// <remarks>
/// Feature columns (see <see cref="FeatureNames"/>): onset strength at the tick; smoothed local onset
/// density over a wide window (<c>local_density</c>, the section's energy) and a narrow one
/// (<c>local_density_fine</c>, which spikes on short drum bursts); the metrical phase (tick-in-beat and
/// beat-in-bar as sin/cos); the global <c>target_difficulty</c>; a per-tick <c>local_intensity</c>
/// (= target × wide density, so calm sections stay easy even at a high target); and
/// <see cref="SpectralBands"/> log-spaced spectral-band energies (the timbre at the tick). All per-map normalized.
/// </remarks>
public sealed class MapFeatureExtractor
{
    /// <summary>Number of spectral-band energy features appended after the base columns.</summary>
    public const int SpectralBands = 6;

    private static readonly string[] BaseFeatureNames =
    [
        "onset_strength",
        "local_density",
        "local_density_fine",
        "tick_in_beat_sin",
        "tick_in_beat_cos",
        "beat_in_bar_sin",
        "beat_in_bar_cos",
        "target_difficulty",
        "local_intensity",
    ];

    public static readonly string[] FeatureNames =
        [.. BaseFeatureNames, .. Enumerable.Range(0, SpectralBands).Select(b => $"band_{b}")];

    public int FeatureCount => FeatureNames.Length;

    private readonly double _densityWindowMs;
    private readonly double _fineDensityWindowMs;

    public MapFeatureExtractor(double densityWindowMs = 2000.0, double fineDensityWindowMs = 200.0)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(densityWindowMs);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fineDensityWindowMs);
        _densityWindowMs = densityWindowMs;
        _fineDensityWindowMs = fineDensityWindowMs;
    }

    /// <summary>
    /// Produces a <c>grid.Count</c>×<see cref="FeatureCount"/> matrix: row <c>i</c> is the feature vector
    /// at global tick <c>i</c> of <paramref name="grid"/>. Metrical phase is taken in each tick's own
    /// (possibly different-tempo) segment.
    /// </summary>
    internal float[][] Extract(TokenGrid grid, IReadOnlyList<QuantizedOnset> onsets, OnsetEnvelope envelope, double targetStars)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(onsets);
        ArgumentNullException.ThrowIfNull(envelope);

        var length = grid.Count;
        var maxFlux = 1e-9;
        foreach (var f in envelope.Flux) maxFlux = Math.Max(maxFlux, f);

        var onsetMs = new double[onsets.Count];
        for (var i = 0; i < onsets.Count; i++) onsetMs[i] = onsets[i].SnappedMs;
        Array.Sort(onsetMs);

        var maxDensity = MaxDensity(onsetMs, grid, _densityWindowMs);
        var maxDensityFine = MaxDensity(onsetMs, grid, _fineDensityWindowMs);
        var difficulty = Math.Clamp(targetStars / 10.0, 0.0, 1.0);
        var maxBand = MaxBands(envelope);

        var rows = new float[length][];
        int lo = 0, hi = 0, loFine = 0, hiFine = 0;
        for (var tick = 0; tick < length; tick++)
        {
            var seg = grid.Segments[grid.SegmentIndexOfTick(tick)];
            var timeMs = grid.TimeMsOf(tick);          // monotonic across the whole grid
            var localBeat = seg.TimeToBeats(timeMs);
            double beatsPerBar = Math.Max(1, seg.BeatsPerMeasure);

            // local density via forward-moving windows (tick times are time-ordered): a wide one for
            // section energy and a narrow one that spikes on short bursts.
            while (lo < onsetMs.Length && onsetMs[lo] < timeMs - _densityWindowMs) lo++;
            while (hi < onsetMs.Length && onsetMs[hi] <= timeMs + _densityWindowMs) hi++;
            while (loFine < onsetMs.Length && onsetMs[loFine] < timeMs - _fineDensityWindowMs) loFine++;
            while (hiFine < onsetMs.Length && onsetMs[hiFine] <= timeMs + _fineDensityWindowMs) hiFine++;
            var density = (hi - lo) / (2.0 * _densityWindowMs / 1000.0);
            var densityFine = (hiFine - loFine) / (2.0 * _fineDensityWindowMs / 1000.0);
            var densityNorm = Math.Clamp(density / maxDensity, 0.0, 1.0);

            var tickInBeat = localBeat - Math.Floor(localBeat);
            var beatInBar = ((localBeat % beatsPerBar) + beatsPerBar) % beatsPerBar / beatsPerBar;

            var row = new float[FeatureNames.Length];
            row[0] = (float)Math.Clamp(FluxAt(envelope, timeMs) / maxFlux, 0.0, 1.0);
            row[1] = (float)densityNorm;
            row[2] = (float)Math.Clamp(densityFine / maxDensityFine, 0.0, 1.0);
            row[3] = (float)Math.Sin(2 * Math.PI * tickInBeat);
            row[4] = (float)Math.Cos(2 * Math.PI * tickInBeat);
            row[5] = (float)Math.Sin(2 * Math.PI * beatInBar);
            row[6] = (float)Math.Cos(2 * Math.PI * beatInBar);
            row[7] = (float)difficulty;
            row[8] = (float)(difficulty * densityNorm); // local_intensity: ease off where the song is calm

            var frame = (timeMs / 1000.0 * envelope.SampleRate - envelope.FrameSize / 2.0) / envelope.HopSize;
            for (var b = 0; b < SpectralBands; b++)
                row[BaseFeatureNames.Length + b] = (float)Math.Clamp(envelope.BandEnergy(frame, b) / maxBand[b], 0.0, 1.0);

            rows[tick] = row;
        }

        return rows;
    }

    /// <summary>Single-segment convenience overload: a uniform grid of <paramref name="length"/> ticks.</summary>
    public float[][] Extract(
        TimingSegment segment,
        IReadOnlyList<QuantizedOnset> onsets,
        OnsetEnvelope envelope,
        int ticksPerBeat,
        int length,
        double targetStars)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ticksPerBeat);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        return Extract(TokenGrid.SingleSegment(segment, ticksPerBeat, length), onsets, envelope, targetStars);
    }

    /// <summary>Per-band peak energy over the whole song, so band energies normalize to [0, 1] per map.</summary>
    private static double[] MaxBands(OnsetEnvelope envelope)
    {
        var max = new double[SpectralBands];
        Array.Fill(max, 1e-9);
        var bandCount = envelope.BandCount;
        if (bandCount == 0)
            return max;

        var frames = envelope.Bands.Length / bandCount;
        var use = Math.Min(SpectralBands, bandCount);
        for (var f = 0; f < frames; f++)
            for (var b = 0; b < use; b++)
                max[b] = Math.Max(max[b], envelope.Bands[f * bandCount + b]);
        return max;
    }

    /// <summary>Linear-interpolated onset-envelope strength at an absolute time (ms).</summary>
    private static double FluxAt(OnsetEnvelope envelope, double timeMs)
    {
        if (envelope.Count == 0) return 0.0;
        var frame = (timeMs / 1000.0 * envelope.SampleRate - envelope.FrameSize / 2.0) / envelope.HopSize;
        if (frame <= 0) return envelope.Flux[0];
        if (frame >= envelope.Count - 1) return envelope.Flux[^1];

        var f0 = (int)Math.Floor(frame);
        var frac = frame - f0;
        return envelope.Flux[f0] * (1 - frac) + envelope.Flux[f0 + 1] * frac;
    }

    /// <summary>Peak local density over the grid (sampled per beat), so densities normalize to [0, 1] per map.</summary>
    private static double MaxDensity(double[] onsetMs, TokenGrid grid, double windowMs)
    {
        var max = 1e-9;
        int lo = 0, hi = 0;
        for (var tick = 0; tick < grid.Count; tick += grid.TicksPerBeat)
        {
            var timeMs = grid.TimeMsOf(tick);
            while (lo < onsetMs.Length && onsetMs[lo] < timeMs - windowMs) lo++;
            while (hi < onsetMs.Length && onsetMs[hi] <= timeMs + windowMs) hi++;
            max = Math.Max(max, (hi - lo) / (2.0 * windowMs / 1000.0));
        }
        return max;
    }
}
