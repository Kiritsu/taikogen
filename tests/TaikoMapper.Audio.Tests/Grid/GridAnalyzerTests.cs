using NUnit.Framework;
using TaikoMapper.Audio.Decoding;
using TaikoMapper.Audio.Grid;
using TaikoMapper.Domain.Rhythm;

namespace TaikoMapper.Audio.Tests.Grid;

/// <summary>
/// End-to-end stage-1 grid: audio → tempo/offset → onsets → quantized grid.
/// </summary>
public class GridAnalyzerTests
{
    private const int SampleRate = 44100;

    [Test]
    public void On_beat_clicks_snap_to_whole_divisor()
    {
        // 150 BPM (beat = 400 ms), clicks on every beat.
        var audio = new MonoAudio(Synth.ClickTrack(150, offsetMs: 250, durationSeconds: 16.0, SampleRate), SampleRate);

        var grid = new GridAnalyzer().Analyze(audio);

        Assert.Multiple(() =>
        {
            Assert.That(grid.PrimarySegment.Bpm, Is.EqualTo(150.0).Within(2.0));
            Assert.That(grid.Onsets, Is.Not.Empty);
            Assert.That(grid.OnTickFraction, Is.GreaterThan(0.8));

            var whole = grid.Onsets.Count(o => o.Divisor == BeatDivisor.Whole);
            Assert.That(whole, Is.GreaterThan(grid.Onsets.Count / 2), "most on-beat clicks should read as 1/1");
        });
    }

    [Test]
    public void Half_beat_clicks_use_whole_and_half_under_overrides()
    {
        // Clicks every 200 ms; with BPM forced to 150 (beat 400 ms) these fall on
        // beats 0, 0.5, 1, 1.5, ... — i.e. alternating 1/1 and 1/2.
        var audio = new MonoAudio(Synth.ClickTrack(300, offsetMs: 0, durationSeconds: 12.0, SampleRate), SampleRate);

        var grid = new GridAnalyzer().Analyze(audio, bpmOverride: 150.0, offsetMsOverride: 0.0);

        var wholeOrHalf = grid.Onsets.Count(o => o.Divisor is BeatDivisor.Whole or BeatDivisor.Half);

        Assert.Multiple(() =>
        {
            Assert.That(grid.Onsets, Is.Not.Empty);
            Assert.That(grid.OnTickFraction, Is.GreaterThan(0.8));
            Assert.That(grid.Onsets.Any(o => o.Divisor == BeatDivisor.Half), Is.True, "off-beats should read as 1/2");
            Assert.That((double)wholeOrHalf / grid.Onsets.Count, Is.GreaterThan(0.9));
        });
    }
}
