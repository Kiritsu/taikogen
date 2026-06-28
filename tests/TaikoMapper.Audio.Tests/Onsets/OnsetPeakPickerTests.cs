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
    public void Returns_nothing_for_silence()
    {
        var silence = new MonoAudio(new float[SampleRate * 2], SampleRate);
        var odf = new SpectralFluxAnalyzer().Analyze(silence);

        Assert.That(new OnsetPeakPicker().Pick(odf), Is.Empty);
    }
}
