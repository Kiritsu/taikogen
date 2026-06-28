using NUnit.Framework;
using TaikoMapper.Audio.Decoding;
using TaikoMapper.Audio.Onsets;

namespace TaikoMapper.Audio.Tests.Onsets;

public class SpectralFluxAnalyzerTests
{
    private const int SampleRate = 44100;

    [Test]
    public void Flux_peaks_at_the_frame_containing_a_lone_click()
    {
        const double clickMs = 500.0;
        var audio = new MonoAudio(Synth.SingleClick(clickMs, durationSeconds: 1.5, SampleRate), SampleRate);
        var analyzer = new SpectralFluxAnalyzer(frameSize: 1024, hopSize: 256);

        var odf = analyzer.Analyze(audio);

        var peakFrame = ArgMax(odf.Flux);
        var peakMs = odf.FrameToMs(peakFrame);

        // The rising-energy frame should sit within ~2 hops (~12 ms) of the click.
        Assert.That(peakMs, Is.EqualTo(clickMs).Within(12.0));
    }

    [Test]
    public void Flux_is_non_negative_and_frame_count_matches_hop()
    {
        var audio = new MonoAudio(Synth.ClickTrack(120, 0, durationSeconds: 4.0, SampleRate), SampleRate);
        var analyzer = new SpectralFluxAnalyzer(frameSize: 1024, hopSize: 256);

        var odf = analyzer.Analyze(audio);

        var expectedFrames = 1 + (audio.Length - 1024) / 256;
        Assert.Multiple(() =>
        {
            Assert.That(odf.Count, Is.EqualTo(expectedFrames));
            Assert.That(odf.Flux, Has.All.GreaterThanOrEqualTo(0.0));
            Assert.That(odf.FrameRate, Is.EqualTo(SampleRate / 256.0).Within(1e-9));
        });
    }

    [Test]
    public void Spectral_bands_separate_low_from_high_frequencies()
    {
        var analyzer = new SpectralFluxAnalyzer(frameSize: 1024, hopSize: 256, bandCount: 6);

        var lowPeak = DominantBand(analyzer, Sine(200.0, 1.0));
        var highPeak = DominantBand(analyzer, Sine(8000.0, 1.0));

        Assert.Multiple(() =>
        {
            Assert.That(analyzer.BandCount, Is.EqualTo(6));
            Assert.That(lowPeak, Is.LessThan(highPeak), "a 200 Hz tone lands in a lower band than an 8 kHz tone");
        });
    }

    /// <summary>The band carrying the most total energy across the whole signal.</summary>
    private static int DominantBand(SpectralFluxAnalyzer analyzer, float[] samples)
    {
        var odf = analyzer.Analyze(new MonoAudio(samples, SampleRate));
        var bandCount = odf.BandCount;
        var totals = new double[bandCount];
        for (var f = 0; f < odf.Bands.Length / bandCount; f++)
            for (var b = 0; b < bandCount; b++)
                totals[b] += odf.Bands[f * bandCount + b];
        return ArgMax(totals);
    }

    private static float[] Sine(double frequency, double seconds)
    {
        var s = new float[(int)(seconds * SampleRate)];
        for (var i = 0; i < s.Length; i++)
            s[i] = (float)(0.5 * Math.Sin(2.0 * Math.PI * frequency * i / SampleRate));
        return s;
    }

    private static int ArgMax(double[] values)
    {
        var best = 0;
        for (var i = 1; i < values.Length; i++)
            if (values[i] > values[best])
                best = i;
        return best;
    }
}
