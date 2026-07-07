using NUnit.Framework;
using TaikoMapper.Audio.Decoding;
using TaikoMapper.Audio.Grid;

namespace TaikoMapper.Audio.Tests.Grid;

/// <summary>
/// End-to-end stage-1 detection on synthetic click tracks. Real-world audio is
/// noisier, so these assert the algorithm is correct on clean input and that 
/// manual overrides bypass detection.
/// </summary>
public class RhythmAnalyzerTests
{
    private const int SampleRate = 44100;

    [TestCase(110.0)]
    [TestCase(128.0)]
    [TestCase(150.0)]
    [TestCase(174.0)]
    [TestCase(200.0)] // osu! songs are commonly fast…
    [TestCase(255.0)] // …and high-BPM maps must not collapse to the half-tempo subharmonic
    public void Detects_tempo_of_a_synthetic_click_track(double bpm)
    {
        var audio = new MonoAudio(Synth.ClickTrack(bpm, offsetMs: 0, durationSeconds: 20.0, SampleRate), SampleRate);

        var analysis = new RhythmAnalyzer().Analyze(audio);

        Assert.Multiple(() =>
        {
            Assert.That(analysis.Segment.Bpm, Is.EqualTo(bpm).Within(2.0));
            Assert.That(analysis.Confidence, Is.GreaterThan(0.3), "clean periodic input should be confident");
            Assert.That(analysis.BpmOverridden, Is.False);
        });
    }

    [Test]
    public void Sub_beat_energy_does_not_double_the_detected_tempo()
    {
        // Strong beats at 150 BPM plus weaker off-beats (a 300 BPM track) — the sub-beat energy that
        // tempts the tempo autocorrelation to lock onto the half-beat and report 300.
        const double bpm = 150.0;
        var beats = Synth.ClickTrack(bpm, offsetMs: 0, durationSeconds: 20.0, SampleRate);
        var offbeats = Synth.ClickTrack(bpm * 2, offsetMs: 0, durationSeconds: 20.0, SampleRate, seed: 99);
        var mixed = new float[beats.Length];
        for (var i = 0; i < mixed.Length; i++)
            mixed[i] = 0.7f * beats[i] + 0.3f * offbeats[i];

        var analysis = new RhythmAnalyzer().Analyze(new MonoAudio(mixed, SampleRate));

        Assert.That(analysis.Segment.Bpm, Is.EqualTo(bpm).Within(6.0), "detected the beat, not the doubled sub-beat");
    }

    [Test]
    public void Detects_offset_of_a_synthetic_click_track()
    {
        const double bpm = 150.0;       // period = 400 ms
        const double offsetMs = 250.0;
        var audio = new MonoAudio(Synth.ClickTrack(bpm, offsetMs, durationSeconds: 20.0, SampleRate), SampleRate);

        var analysis = new RhythmAnalyzer().Analyze(audio);

        Assert.That(analysis.Segment.StartMs, Is.EqualTo(offsetMs).Within(20.0));
    }

    [Test]
    public void Manual_overrides_bypass_detection()
    {
        var audio = new MonoAudio(Synth.ClickTrack(150, offsetMs: 250, durationSeconds: 8.0, SampleRate), SampleRate);

        var analysis = new RhythmAnalyzer().Analyze(audio, bpmOverride: 180.0, offsetMsOverride: 42.0);

        Assert.Multiple(() =>
        {
            Assert.That(analysis.Segment.Bpm, Is.EqualTo(180.0));
            Assert.That(analysis.Segment.StartMs, Is.EqualTo(42.0));
            Assert.That(analysis.BpmOverridden, Is.True);
            Assert.That(analysis.OffsetOverridden, Is.True);
            Assert.That(analysis.Confidence, Is.EqualTo(1.0));
        });
    }

    [Test]
    public void Produces_a_valid_timing_segment()
    {
        var audio = new MonoAudio(Synth.ClickTrack(128, offsetMs: 0, durationSeconds: 10.0, SampleRate), SampleRate);

        var segment = new RhythmAnalyzer().Analyze(audio).Segment;

        Assert.That(segment.BeatLengthMs, Is.EqualTo(60_000.0 / segment.Bpm).Within(1e-9));
    }
}
