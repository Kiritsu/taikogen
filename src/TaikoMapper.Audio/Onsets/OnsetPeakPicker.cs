using TaikoMapper.Domain.Rhythm;

namespace TaikoMapper.Audio.Onsets;

/// <summary>
/// Turns the continuous onset envelope into a discrete list of
/// <see cref="Onset"/>s by peak-picking with adaptive thresholding.
/// </summary>
/// <remarks>
/// A frame is accepted as an onset when it is (a) a local maximum within
/// <see cref="_localWindow"/> frames, (b) above an adaptive threshold derived from
/// the local moving mean of the envelope, and (c) at least
/// <see cref="_minSeparationMs"/> after the previous accepted onset. Strengths are
/// normalized to (0, 1] against the loudest onset so they are comparable across
/// tracks.
/// </remarks>
public sealed class OnsetPeakPicker
{
    private readonly int _localWindow;
    private readonly int _meanWindow;
    private readonly double _thresholdFactor;
    private readonly double _minSeparationMs;

    public OnsetPeakPicker(
        int localWindow = 1,
        int meanWindow = 12,
        double thresholdFactor = 1.5,
        double minSeparationMs = 30.0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(localWindow, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(meanWindow, 1);

        _localWindow = localWindow;
        _meanWindow = meanWindow;
        _thresholdFactor = thresholdFactor;
        _minSeparationMs = minSeparationMs;
    }

    public IReadOnlyList<Onset> Pick(OnsetEnvelope envelope) => PickCore(envelope, _minSeparationMs);

    /// <summary>
    /// Peak-picks with a <b>tempo-aware</b> minimum separation: at <paramref name="bpm"/> the floor drops
    /// to roughly a 1/32 interval (clamped), so fast 1/16 drum bursts survive instead of being merged.
    /// </summary>
    public IReadOnlyList<Onset> Pick(OnsetEnvelope envelope, double bpm)
    {
        if (bpm <= 0 || !double.IsFinite(bpm))
            return Pick(envelope);
        var beatMs = 60_000.0 / bpm;
        return PickCore(envelope, Math.Clamp(beatMs / 32.0, 7.0, _minSeparationMs));
    }

    private IReadOnlyList<Onset> PickCore(OnsetEnvelope envelope, double minSeparationMs)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var flux = envelope.Flux;
        var m = flux.Length;
        if (m == 0)
            return [];

        var globalMax = 0.0;
        for (var i = 0; i < m; i++)
            if (flux[i] > globalMax) globalMax = flux[i];

        if (globalMax <= 0.0)
            return [];

        var minSeparationFrames = Math.Max(1, (int)Math.Round(minSeparationMs / 1000.0 * envelope.FrameRate));

        var onsets = new List<Onset>();
        var lastFrame = int.MinValue;

        for (var n = 0; n < m; n++)
        {
            var value = flux[n];
            if (value <= 0.0)
                continue;

            if (!IsLocalMaximum(flux, n, m))
                continue;

            var threshold = LocalMean(flux, n, m) * _thresholdFactor;
            if (value < threshold)
                continue;

            if (n - lastFrame < minSeparationFrames)
            {
                // Too close to the previous onset: keep whichever is stronger.
                if (onsets.Count > 0 && value > onsets[^1].Strength * globalMax)
                    onsets[^1] = new Onset(envelope.FrameToMs(n), value / globalMax);
                lastFrame = n;
                continue;
            }

            onsets.Add(new Onset(envelope.FrameToMs(n), value / globalMax));
            lastFrame = n;
        }

        return onsets;
    }

    private bool IsLocalMaximum(double[] flux, int n, int m)
    {
        for (var k = -_localWindow; k <= _localWindow; k++)
        {
            var j = n + k;
            if (j < 0 || j >= m || j == n)
                continue;
            if (flux[j] > flux[n])
                return false;
        }

        return true;
    }

    private double LocalMean(double[] flux, int n, int m)
    {
        var sum = 0.0;
        var count = 0;
        for (var k = -_meanWindow; k <= _meanWindow; k++)
        {
            var j = n + k;
            if (j < 0 || j >= m)
                continue;
            sum += flux[j];
            count++;
        }

        return count > 0 ? sum / count : 0.0;
    }
}
