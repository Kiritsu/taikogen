using NUnit.Framework;
using TaikoMapper.Audio.Grid;
using TaikoMapper.Domain.Rhythm;
using TaikoMapper.Domain.Timing;

namespace TaikoMapper.Audio.Tests.Grid;

public class RhythmQuantizerTests
{
    // 150 BPM => beat length 400 ms, starting at t = 0.
    private static readonly TimingSegment Segment = new(0.0, 150.0);

    [TestCase(0.0, BeatDivisor.Whole, 0.0)]
    [TestCase(200.0, BeatDivisor.Half, 200.0)]
    [TestCase(100.0, BeatDivisor.Quarter, 100.0)]
    [TestCase(50.0, BeatDivisor.Eighth, 50.0)]
    [TestCase(133.3333333, BeatDivisor.Triplet, 133.3333333)]
    [TestCase(66.6666667, BeatDivisor.Sextuplet, 66.6666667)]
    public void Snaps_onset_to_the_coarsest_matching_divisor(double timeMs, BeatDivisor expected, double expectedSnap)
    {
        var q = new RhythmQuantizer().Quantize(Segment, new Onset(timeMs, 1.0));

        Assert.Multiple(() =>
        {
            Assert.That(q.Divisor, Is.EqualTo(expected));
            Assert.That(q.SnappedMs, Is.EqualTo(expectedSnap).Within(1e-3));
            Assert.That(q.OnTick, Is.True);
            Assert.That(q.AbsResidualMs, Is.LessThan(1e-3));
        });
    }

    [Test]
    public void Records_the_signed_residual_from_the_tick()
    {
        var late = new RhythmQuantizer().Quantize(Segment, new Onset(203.0, 1.0));
        var early = new RhythmQuantizer().Quantize(Segment, new Onset(197.0, 1.0));

        Assert.Multiple(() =>
        {
            Assert.That(late.SnappedMs, Is.EqualTo(200.0).Within(1e-6));
            Assert.That(late.ResidualMs, Is.EqualTo(3.0).Within(1e-6));   // onset after the tick → positive
            Assert.That(early.ResidualMs, Is.EqualTo(-3.0).Within(1e-6)); // onset before the tick → negative
            Assert.That(late.OnTick, Is.True);
        });
    }

    [Test]
    public void Flags_an_off_grid_onset_as_not_on_tick()
    {
        // 25 ms = 1/16 of a beat — exactly between the 1/8 ticks, beyond the divisor set.
        var q = new RhythmQuantizer().Quantize(Segment, new Onset(25.0, 1.0));

        Assert.Multiple(() =>
        {
            Assert.That(q.OnTick, Is.False);
            Assert.That(q.AbsResidualMs, Is.EqualTo(25.0).Within(1e-6));
        });
    }

    [Test]
    public void Builds_a_grid_carrying_segment_and_divisors()
    {
        Onset[] onsets = [new(0.0, 1.0), new(200.0, 0.5), new(400.0, 0.8)];

        var grid = new RhythmQuantizer().Quantize(Segment, onsets);

        Assert.Multiple(() =>
        {
            Assert.That(grid.Segments, Has.Count.EqualTo(1));
            Assert.That(grid.PrimarySegment.Bpm, Is.EqualTo(150.0));
            Assert.That(grid.Onsets, Has.Count.EqualTo(3));
            Assert.That(grid.SupportedDivisors, Is.EqualTo(BeatDivisors.Supported));
            Assert.That(grid.OnTickFraction, Is.EqualTo(1.0));
        });
    }
}
