namespace TaikoMapper.Audio.Decoding;

/// <summary>
/// A decoded mono PCM signal: float samples nominally in [-1, 1] at a known sample rate.
/// The input to all audio analysis.
/// </summary>
public sealed class MonoAudio
{
    public MonoAudio(float[] samples, int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive.");

        Samples = samples;
        SampleRate = sampleRate;
    }

    /// <summary>Mono PCM samples.</summary>
    public float[] Samples { get; }

    /// <summary>Samples per second.</summary>
    public int SampleRate { get; }

    public int Length => Samples.Length;

    public double DurationSeconds => (double)Samples.Length / SampleRate;

    public ReadOnlySpan<float> AsSpan() => Samples;

    /// <summary>Converts a sample index to its time in milliseconds.</summary>
    public double SampleToMs(long sampleIndex) => sampleIndex * 1000.0 / SampleRate;
}
