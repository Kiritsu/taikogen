using TaikoMapper.Audio.Onsets;

namespace TaikoMapper.Audio.Timing;

/// <summary>A candidate tempo and its (unweighted) autocorrelation strength.</summary>
public readonly record struct TempoCandidate(double Bpm, double Strength);

/// <summary>Result of tempo estimation: the chosen BPM, a confidence in [0, 1], and ranked candidates.</summary>
public sealed record TempoResult(double Bpm, double Confidence, IReadOnlyList<TempoCandidate> Candidates);

/// <summary>
/// Estimates global tempo from an onset detection function via autocorrelation.
/// A log-Gaussian tempo prior over BPM resolves octave
/// ambiguity (½×/2×) without hard-coding a single expected tempo.
/// </summary>
public sealed class TempoEstimator
{
    private readonly double _minBpm;
    private readonly double _maxBpm;
    private readonly double _priorCenterBpm;
    private readonly double _priorWidth;

    // Defaults tuned for osu!: songs are commonly 160–260+ BPM, so the search range
    // reaches 330 and the prior centres on 180. The range ceiling of 330 also stops a
    // ~170–200 BPM song from being doubled into an out-of-range octave.
    public TempoEstimator(double minBpm = 100, double maxBpm = 330, double priorCenterBpm = 180, double priorWidth = 0.9)
    {
        if (minBpm <= 0 || maxBpm <= minBpm)
            throw new ArgumentException("Require 0 < minBpm < maxBpm.");

        _minBpm = minBpm;
        _maxBpm = maxBpm;
        _priorCenterBpm = priorCenterBpm;
        _priorWidth = priorWidth;
    }

    public TempoResult Estimate(OnsetEnvelope odf)
    {
        ArgumentNullException.ThrowIfNull(odf);

        var d = odf.Flux;
        var m = d.Length;
        var frameRate = odf.FrameRate;

        var lagMin = Math.Max(1, (int)Math.Floor(60.0 * frameRate / _maxBpm));
        var lagMax = Math.Min(m - 1, (int)Math.Ceiling(60.0 * frameRate / _minBpm));

        if (m < 4 || lagMax <= lagMin)
            return new TempoResult(_priorCenterBpm, 0.0, [new TempoCandidate(_priorCenterBpm, 0.0)]);

        var mean = Mean(d);
        var variance = Variance(d, mean);
        if (variance <= double.Epsilon)
            return new TempoResult(_priorCenterBpm, 0.0, [new TempoCandidate(_priorCenterBpm, 0.0)]);

        // Normalised autocorrelation (correlation coefficient) per lag, plus a
        // prior-weighted score used only for picking the lag.
        var correlation = new double[lagMax + 1];
        var weighted = new double[lagMax + 1];
        var bestLag = lagMin;

        for (var lag = lagMin; lag <= lagMax; lag++)
        {
            var acc = 0.0;
            for (var n = lag; n < m; n++)
                acc += (d[n] - mean) * (d[n - lag] - mean);

            var r = acc / ((m - lag) * variance); // ≈ correlation coefficient in [-1, 1]
            var bpm = 60.0 * frameRate / lag;

            correlation[lag] = r;
            weighted[lag] = r * Prior(bpm);

            if (weighted[lag] > weighted[bestLag])
                bestLag = lag;
        }

        // Octave correction: a clean periodic ODF correlates almost as strongly at
        // 2×/3× the true period (subharmonics), and the prior can tip selection onto
        // one. If a faster, in-range tempo (the half/third lag) is comparably strong,
        // prefer it — that is the fundamental beat.
        bestLag = CorrectOctave(correlation, bestLag, lagMin);

        var refinedLag = PeakInterpolation.Refine(weighted, bestLag, lagMin, lagMax);
        var chosenBpm = 60.0 * frameRate / refinedLag;
        var confidence = Math.Clamp(correlation[bestLag], 0.0, 1.0);

        return new TempoResult(chosenBpm, confidence, BuildCandidates(chosenBpm, confidence));
    }

    /// <summary>
    /// Walks from the chosen lag toward shorter lags (faster tempos) by integer
    /// divisors while the divided-lag correlation stays within a fraction of the
    /// current peak and remains in range.
    /// </summary>
    private static int CorrectOctave(double[] correlation, int lag, int lagMin)
    {
        const double keepFraction = 0.80;
        const int maxDivisor = 3;
        const int searchRadius = 2;

        var improved = true;
        while (improved)
        {
            improved = false;
            for (var divisor = 2; divisor <= maxDivisor; divisor++)
            {
                var target = (int)Math.Round(lag / (double)divisor);
                if (target < lagMin)
                    continue;

                // The divided lag can be off by a frame from the sharp fundamental
                // peak, so take the strongest correlation in a small neighbourhood.
                var candidate = -1;
                for (var l = Math.Max(lagMin, target - searchRadius); l <= target + searchRadius && l < correlation.Length; l++)
                {
                    if (candidate < 0 || correlation[l] > correlation[candidate])
                        candidate = l;
                }

                if (candidate >= lagMin && correlation[candidate] >= keepFraction * correlation[lag])
                {
                    lag = candidate;
                    improved = true;
                    break;
                }
            }
        }

        return lag;
    }

    private IReadOnlyList<TempoCandidate> BuildCandidates(double bpm, double strength)
    {
        var list = new List<TempoCandidate> { new(bpm, strength) };
        if (bpm / 2.0 >= _minBpm) list.Add(new TempoCandidate(bpm / 2.0, strength));
        if (bpm * 2.0 <= _maxBpm) list.Add(new TempoCandidate(bpm * 2.0, strength));
        return list;
    }

    private double Prior(double bpm)
    {
        var z = Math.Log(bpm / _priorCenterBpm) / _priorWidth;
        return Math.Exp(-0.5 * z * z);
    }

    private static double Mean(double[] d)
    {
        var sum = 0.0;
        for (var i = 0; i < d.Length; i++) sum += d[i];
        return sum / d.Length;
    }

    private static double Variance(double[] d, double mean)
    {
        var sum = 0.0;
        for (var i = 0; i < d.Length; i++)
        {
            var v = d[i] - mean;
            sum += v * v;
        }
        return sum / d.Length;
    }
}
