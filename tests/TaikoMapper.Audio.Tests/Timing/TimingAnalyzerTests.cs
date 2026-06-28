using NUnit.Framework;
using TaikoMapper.Audio.Onsets;
using TaikoMapper.Audio.Timing;
using TaikoMapper.Domain.Timing;

namespace TaikoMapper.Audio.Tests.Timing;

public class TimingAnalyzerTests
{
    private const int SampleRate = 44100;
    private const int HopSize = 256;
    private const int FrameSize = 1024;

    /// <summary>An onset envelope with an impulse every <paramref name="impulsePeriodFrames"/> frames.</summary>
    private static OnsetEnvelope ClickTrack(int frames, double impulsePeriodFrames)
    {
        var flux = new double[frames];
        for (double pos = 0; pos < frames; pos += impulsePeriodFrames)
        {
            var f = (int)Math.Round(pos);
            if (f < frames)
                flux[f] = 1.0;
        }
        return new OnsetEnvelope(flux, SampleRate, HopSize, FrameSize);
    }

    private static double PeriodFrames(double bpm) => 60.0 * ((double)SampleRate / HopSize) / bpm;

    [Test]
    public void Constant_aligned_tempo_yields_a_single_segment()
    {
        var period = PeriodFrames(120.0);
        var odf = ClickTrack(frames: 5200, impulsePeriodFrames: period); // ~30 s on the grid

        var segments = new TimingAnalyzer().Analyze(odf, bpm: 120.0);

        Assert.That(segments, Has.Count.EqualTo(1));
    }

    [Test]
    public void A_drifting_grid_is_re_anchored_into_multiple_same_bpm_segments()
    {
        // Clicks slightly faster than the analysed BPM ⇒ the phase drifts ⇒ re-anchors needed.
        var period = PeriodFrames(120.0);
        var odf = ClickTrack(frames: 5200, impulsePeriodFrames: period * 0.98);

        var segments = new TimingAnalyzer().Analyze(odf, bpm: 120.0);

        Assert.Multiple(() =>
        {
            Assert.That(segments.Count, Is.GreaterThan(1), "drift should produce re-anchoring segments");
            Assert.That(segments, Has.All.Matches<TimingSegment>(s => Math.Abs(s.Bpm - 120.0) < 1e-9),
                "Tier 1 keeps one BPM and only re-anchors the offset");
            Assert.That(segments.Select(s => s.StartMs), Is.Ordered.Ascending, "segments start in order");
        });
    }

    /// <summary>A click track that runs at <paramref name="bpm1"/> then switches to <paramref name="bpm2"/>.</summary>
    private static OnsetEnvelope TwoTempoClickTrack(int frames, int splitFrame, double bpm1, double bpm2)
    {
        var flux = new double[frames];
        for (double pos = 0; pos < splitFrame; pos += PeriodFrames(bpm1))
            flux[(int)Math.Round(pos)] = 1.0;
        for (double pos = splitFrame; pos < frames; pos += PeriodFrames(bpm2))
        {
            var f = (int)Math.Round(pos);
            if (f < frames)
                flux[f] = 1.0;
        }
        return new OnsetEnvelope(flux, SampleRate, HopSize, FrameSize);
    }

    [Test]
    public void Multi_tempo_detects_a_tempo_change_into_two_regions()
    {
        // 0–20 s @160 BPM, then 20–40 s @200 BPM.
        var odf = TwoTempoClickTrack(frames: 6891, splitFrame: 3445, bpm1: 160.0, bpm2: 200.0);

        var segments = new TimingAnalyzer().AnalyzeMultiTempo(odf, bpmPrior: 180.0);

        Assert.Multiple(() =>
        {
            Assert.That(segments.Count, Is.GreaterThanOrEqualTo(2), "a tempo change should split into regions");
            Assert.That(segments.Any(s => Math.Abs(s.Bpm - 160.0) <= 6.0), Is.True, "first region ≈160 BPM");
            Assert.That(segments.Any(s => Math.Abs(s.Bpm - 200.0) <= 6.0), Is.True, "second region ≈200 BPM");
            Assert.That(segments.Select(s => s.StartMs), Is.Ordered.Ascending);
        });
    }

    [Test]
    public void Multi_tempo_keeps_a_constant_tempo_song_at_one_tempo()
    {
        var odf = ClickTrack(frames: 6891, impulsePeriodFrames: PeriodFrames(180.0));

        var segments = new TimingAnalyzer().AnalyzeMultiTempo(odf, bpmPrior: 180.0);

        Assert.That(segments, Has.All.Matches<TimingSegment>(s => Math.Abs(s.Bpm - 180.0) <= 1e-9),
            "no false tempo split on a constant-tempo track");
    }
}
