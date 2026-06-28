namespace TaikoMapper.Audio.Onsets;

/// <summary>
/// Onset detection function (ODF): a per-frame onset-strength signal over time,
/// the shared input to tempo and offset estimation.
/// </summary>
public sealed class OnsetEnvelope
{
    public OnsetEnvelope(double[] flux, int sampleRate, int hopSize, int frameSize, double[]? bands = null, int bandCount = 0)
    {
        ArgumentNullException.ThrowIfNull(flux);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(hopSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameSize);
        ArgumentOutOfRangeException.ThrowIfNegative(bandCount);

        Flux = flux;
        SampleRate = sampleRate;
        HopSize = hopSize;
        FrameSize = frameSize;
        Bands = bands ?? [];
        BandCount = bandCount;
    }

    /// <summary>Onset strength per analysis frame.</summary>
    public double[] Flux { get; }

    /// <summary>Per-frame log-spaced spectral-band energies, row-major <c>[frame * BandCount + band]</c> (empty if not computed).</summary>
    public double[] Bands { get; }

    /// <summary>Number of spectral bands per frame (0 if not computed).</summary>
    public int BandCount { get; }

    /// <summary>Linear-interpolated energy of <paramref name="band"/> at a (fractional) frame; 0 if bands were not computed.</summary>
    public double BandEnergy(double frame, int band)
    {
        if (BandCount == 0 || band < 0 || band >= BandCount)
            return 0.0;

        var frames = Bands.Length / BandCount;
        if (frames == 0)
            return 0.0;
        if (frame <= 0) return Bands[band];
        if (frame >= frames - 1) return Bands[(frames - 1) * BandCount + band];

        var f0 = (int)Math.Floor(frame);
        var frac = frame - f0;
        return Bands[f0 * BandCount + band] * (1 - frac) + Bands[(f0 + 1) * BandCount + band] * frac;
    }

    public int SampleRate { get; }

    public int HopSize { get; }

    public int FrameSize { get; }

    public int Count => Flux.Length;

    /// <summary>Analysis frames per second.</summary>
    public double FrameRate => (double)SampleRate / HopSize;

    // A frame's onset time is taken at its window centre (frame*hop + frameSize/2),
    // which is where the analysed energy sits — not the window start.
    public double FrameToSeconds(double frame) => (frame * HopSize + FrameSize / 2.0) / SampleRate;

    public double FrameToMs(double frame) => FrameToSeconds(frame) * 1000.0;
}
