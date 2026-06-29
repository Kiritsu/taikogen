using NUnit.Framework;
using TaikoMapper.Audio.Decoding;
using TaikoMapper.Audio.Onsets;

namespace TaikoMapper.Audio.Tests.Onsets;

public class OnsetPeakPickerTests
{
    private const int SampleRate = 44100;

    [Test]
    public void Picks_one_onset_per_click()
    {
        // 120 BPM click track, offset 250 ms, 8 s: clicks at 250, 750, ... 7750 → 16 clicks.
        var audio = new MonoAudio(Synth.ClickTrack(120, offsetMs: 250, durationSeconds: 8.0, SampleRate), SampleRate);
        var odf = new SpectralFluxAnalyzer().Analyze(audio);

        var onsets = new OnsetPeakPicker().Pick(odf);

        Assert.Multiple(() =>
        {
            Assert.That(onsets.Count, Is.EqualTo(16).Within(2));
            Assert.That(onsets.Select(o => o.Strength), Has.All.InRange(0.0, 1.0));

            foreach (var o in onsets)
            {
                var nearestClick = 250.0 + Math.Round((o.TimeMs - 250.0) / 500.0) * 500.0;
                Assert.That(Math.Abs(o.TimeMs - nearestClick), Is.LessThan(20.0), $"onset at {o.TimeMs:F1} ms");
            }
        });
    }

    [Test]
    public void Tempo_aware_picking_keeps_fast_bursts_a_flat_floor_would_merge()
    {
        // Impulses ~17 ms apart (1/16 at ~216 BPM). The flat 30 ms floor merges them into one;
        // the tempo-aware floor (≈ a 1/32 interval) keeps them as a burst.
        var flux = new double[120];
        for (var k = 0; k < 8; k++)
            flux[6 * k] = 1.0; // 6 frames × (128/44100 s) ≈ 17.4 ms apart
        var odf = new OnsetEnvelope(flux, sampleRate: 44100, hopSize: 128, frameSize: 512);

        var flat = new OnsetPeakPicker().Pick(odf);
        var tempoAware = new OnsetPeakPicker().Pick(odf, bpm: 216.0);

        Assert.Multiple(() =>
        {
            Assert.That(flat.Count, Is.LessThanOrEqualTo(2), "the flat 30 ms floor collapses the burst");
            Assert.That(tempoAware.Count, Is.GreaterThanOrEqualTo(6), "the tempo-aware floor keeps the burst");
        });
    }

    [Test]
    public void Returns_nothing_for_silence()
    {
        var silence = new MonoAudio(new float[SampleRate * 2], SampleRate);
        var odf = new SpectralFluxAnalyzer().Analyze(silence);

        Assert.That(new OnsetPeakPicker().Pick(odf), Is.Empty);
    }
}
