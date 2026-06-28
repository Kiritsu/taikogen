using NUnit.Framework;
using TaikoMapper.Beatmap.IO;
using OsuBeatmap = osu.Game.Beatmaps.Beatmap;

namespace TaikoMapper.Beatmap.Tests.Io;

public class RoundTripTests
{
    [Test]
    public void Encode_is_a_fixed_point()
    {
        var beatmap = BeatmapIo.Load(Fixtures.Path(Fixtures.BasicTaiko));

        var encodedOnce = BeatmapIo.Encode(beatmap);
        var encodedTwice = BeatmapIo.Encode(DecodeString(encodedOnce));

        Assert.That(encodedTwice, Is.EqualTo(encodedOnce));
    }

    [Test]
    public void RoundTrip_preserves_hit_objects_and_timing()
    {
        var original = BeatmapIo.Load(Fixtures.Path(Fixtures.BasicTaiko));
        var roundTripped = DecodeString(BeatmapIo.Encode(original));

        Assert.Multiple(() =>
        {
            Assert.That(original.HitObjects, Is.Not.Empty, "fixture should contain hit objects");

            Assert.That(
                roundTripped.HitObjects.Select(h => h.StartTime),
                Is.EqualTo(original.HitObjects.Select(h => h.StartTime)),
                "hit-object start times should survive a round trip");

            Assert.That(
                roundTripped.ControlPointInfo.TimingPoints.Select(t => (t.Time, t.BeatLength)),
                Is.EqualTo(original.ControlPointInfo.TimingPoints.Select(t => (t.Time, t.BeatLength))),
                "uninherited timing points should survive a round trip");
        });
    }

    private static OsuBeatmap DecodeString(string osu)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(osu));
        return BeatmapIo.Decode(stream);
    }
}
