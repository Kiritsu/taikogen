using NUnit.Framework;
using TaikoMapper.Audio.Grid;
using TaikoMapper.Domain.Rhythm;
using TaikoMapper.Domain.Timing;

namespace TaikoMapper.Audio.Tests.Grid;

public class RhythmQuantizerMultiSegmentTests
{
    [Test]
    public void Quantizes_each_onset_against_its_active_segment()
    {
        var seg0 = new TimingSegment(0.0, 120.0);      // beat 500 ms
        var seg1 = new TimingSegment(4031.0, 240.0);   // starts off seg0's grid; beat 250 ms
        var onsets = new List<Onset>
        {
            new(1000.0, 1.0),  // seg0 beat 2
            new(4031.0, 1.0),  // seg1 beat 0 — 31 ms past a seg0 beat, so OFF seg0's grid
        };

        var multi = new RhythmQuantizer().Quantize([seg0, seg1], onsets);
        var single = new RhythmQuantizer().Quantize(seg0, onsets); // for contrast

        Assert.Multiple(() =>
        {
            Assert.That(multi.Segments.Count, Is.EqualTo(2));
            Assert.That(multi.Onsets[0].OnTick, Is.True);
            Assert.That(multi.Onsets[0].SnappedMs, Is.EqualTo(1000.0).Within(1e-6));

            // Against its own segment the second onset lands exactly on a tick…
            Assert.That(multi.Onsets[1].OnTick, Is.True);
            Assert.That(multi.Onsets[1].SnappedMs, Is.EqualTo(4031.0).Within(1e-6));
            // …whereas forcing seg0 onto it would NOT snap cleanly (proves the per-segment routing).
            Assert.That(single.Onsets[1].OnTick, Is.False);
        });
    }
}
