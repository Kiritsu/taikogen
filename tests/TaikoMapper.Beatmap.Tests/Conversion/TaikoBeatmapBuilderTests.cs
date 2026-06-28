using NUnit.Framework;
using TaikoMapper.Beatmap.Conversion;
using TaikoMapper.Beatmap.Difficulty;
using TaikoMapper.Beatmap.IO;
using TaikoMapper.Domain.Chart;
using TaikoMapper.Domain.Timing;

namespace TaikoMapper.Beatmap.Tests.Conversion;

/// <summary>
/// Verifies the stage-2 hand-off: a <see cref="TaikoChart"/> builds into a valid
/// osu! taiko beatmap whose official difficulty computes, scales with density, and
/// survives a round trip through the encoder.
/// </summary>
public class TaikoBeatmapBuilderTests
{
    private static TaikoChart MonoChart(int count, double stepMs, double bpm = 150.0)
    {
        var notes = new NoteEvent[count];
        for (var i = 0; i < count; i++)
            notes[i] = new NoteEvent(i * stepMs, TaikoColor.Don);
        return new TaikoChart([new TimingSegment(0.0, bpm)], notes);
    }

    [Test]
    public void Build_produces_a_taiko_beatmap_with_the_expected_objects()
    {
        var beatmap = TaikoBeatmapBuilder.Build(MonoChart(16, stepMs: 200));

        Assert.Multiple(() =>
        {
            Assert.That(beatmap.HitObjects, Has.Count.EqualTo(16));
            Assert.That(beatmap.BeatmapInfo.Ruleset.OnlineID, Is.EqualTo(1)); // taiko
            Assert.That(beatmap.ControlPointInfo.TimingPoints, Is.Not.Empty);
        });
    }

    [Test]
    public void StarRating_is_positive_and_grows_with_density()
    {
        var sparse = TaikoDifficulty.StarRating(TaikoBeatmapBuilder.Build(MonoChart(20, stepMs: 400))); // 1 note / beat
        var dense = TaikoDifficulty.StarRating(TaikoBeatmapBuilder.Build(MonoChart(80, stepMs: 100)));  // 4 notes / beat

        Assert.Multiple(() =>
        {
            Assert.That(sparse, Is.GreaterThan(0.0));
            Assert.That(dense, Is.GreaterThan(sparse), "denser placement should rate harder");
        });
    }

    [Test]
    public void Built_map_round_trips_through_the_encoder()
    {
        var beatmap = TaikoBeatmapBuilder.Build(MonoChart(24, stepMs: 200));

        var osu = BeatmapIo.Encode(beatmap);
        var reDecoded = BeatmapIo.Decode(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(osu)));

        Assert.That(reDecoded.HitObjects, Has.Count.EqualTo(24));
    }
}
