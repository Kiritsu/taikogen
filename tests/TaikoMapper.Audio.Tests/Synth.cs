namespace TaikoMapper.Audio.Tests;

/// <summary>Deterministic synthetic signals for testing stage-1 analysis.</summary>
internal static class Synth
{
    /// <summary>
    /// A mono click track: short decaying noise bursts on every beat at the given
    /// tempo and offset. Noise bursts give a broadband transient the spectral-flux
    /// detector picks up cleanly. Deterministic for a fixed <paramref name="seed"/>.
    /// </summary>
    public static float[] ClickTrack(
        double bpm, double offsetMs, double durationSeconds, int sampleRate, int seed = 1234)
    {
        var total = (int)(durationSeconds * sampleRate);
        var signal = new float[total];
        var rng = new Random(seed);

        var periodSamples = 60.0 / bpm * sampleRate;
        var offsetSamples = offsetMs / 1000.0 * sampleRate;
        var burstLength = (int)(0.010 * sampleRate); // 10 ms
        var decay = 0.002 * sampleRate;            // ~2 ms time constant

        for (var pos = offsetSamples; pos < total; pos += periodSamples)
        {
            var start = (int)Math.Round(pos);
            for (var j = 0; j < burstLength && start + j < total; j++)
            {
                if (start + j < 0) continue;
                var env = Math.Exp(-j / decay);
                signal[start + j] += (float)((rng.NextDouble() * 2.0 - 1.0) * env * 0.8);
            }
        }

        return signal;
    }

    /// <summary>A single decaying noise burst centred at <paramref name="clickMs"/> in silence.</summary>
    public static float[] SingleClick(double clickMs, double durationSeconds, int sampleRate, int seed = 7)
    {
        var total = (int)(durationSeconds * sampleRate);
        var signal = new float[total];
        var rng = new Random(seed);

        var start = (int)Math.Round(clickMs / 1000.0 * sampleRate);
        var burstLength = (int)(0.010 * sampleRate);
        var decay = 0.002 * sampleRate;

        for (var j = 0; j < burstLength && start + j < total; j++)
        {
            if (start + j < 0) continue;
            var env = Math.Exp(-j / decay);
            signal[start + j] += (float)((rng.NextDouble() * 2.0 - 1.0) * env * 0.8);
        }

        return signal;
    }
}
