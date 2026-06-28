using NUnit.Framework;
using TaikoMapper.Beatmap.Conversion;
using TaikoMapper.Domain.Chart;
using TaikoMapper.Domain.Timing;

namespace TaikoMapper.Beatmap.Tests.Conversion;

public class MultiSegmentEncodingTests
{
    [Test]
    public void Build_emits_one_uninherited_timing_point_per_segment()
    {
        var chart = new TaikoChart(
            [new TimingSegment(0.0, 120.0), new TimingSegment(4000.0, 180.0)],
            [new NoteEvent(0.0, TaikoColor.Don), new NoteEvent(4000.0, TaikoColor.Kat)]);

        var beatmap = TaikoBeatmapBuilder.Build(chart);
        var timingPoints = beatmap.ControlPointInfo.TimingPoints;

        Assert.Multiple(() =>
        {
            Assert.That(timingPoints.Count, Is.EqualTo(2));
            Assert.That(timingPoints[0].Time, Is.EqualTo(0.0).Within(1e-6));
            Assert.That(60_000.0 / timingPoints[0].BeatLength, Is.EqualTo(120.0).Within(1e-6));
            Assert.That(timingPoints[1].Time, Is.EqualTo(4000.0).Within(1e-6));
            Assert.That(60_000.0 / timingPoints[1].BeatLength, Is.EqualTo(180.0).Within(1e-6));
        });
    }
}
