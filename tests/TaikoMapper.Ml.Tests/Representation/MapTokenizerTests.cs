using NUnit.Framework;
using TaikoMapper.Domain.Chart;
using TaikoMapper.Domain.Timing;
using TaikoMapper.Ml.Representation;

namespace TaikoMapper.Ml.Tests.Representation;

public class MapTokenizerTests
{
    private static readonly TimingSegment Segment = new(startMs: 137.0, bpm: 180.0);

    /// <summary>A note at a beat position, with its time rounded the way the placer rounds (ms integer).</summary>
    private static NoteEvent At(double beat, TaikoColor color, bool finisher = false) =>
        new(Math.Round(Segment.BeatsToTime(beat)), color, finisher);

    [Test]
    public void Round_trips_a_chart_on_the_grid()
    {
        var chart = new TaikoChart([Segment], new[]
        {
            At(0.0, TaikoColor.Don),
            At(0.25, TaikoColor.Kat),               // 1/4
            At(0.5, TaikoColor.Don, finisher: true),// finisher
            At(1.0 / 3.0, TaikoColor.Kat),          // 1/3 (triplet) — handled at 48 ticks/beat
            At(1.0, TaikoColor.Kat),
            At(2.5, TaikoColor.Don),
        }.OrderBy(n => n.TimeMs).ToArray());

        var tokenizer = new MapTokenizer();
        var encoded = tokenizer.Encode(chart, "mapper-x");
        var decoded = tokenizer.Decode(encoded);

        Assert.That(decoded.Notes, Is.EqualTo(chart.Notes), "decode(encode(chart)) reproduces the chart exactly");
    }

    [Test]
    public void Preserves_author_resolution_and_note_count()
    {
        var chart = new TaikoChart([Segment], [At(0.0, TaikoColor.Don), At(0.5, TaikoColor.Kat), At(1.0, TaikoColor.Don)
        ]);

        var encoded = new MapTokenizer(ticksPerBeat: 24).Encode(chart, "satoki");

        Assert.Multiple(() =>
        {
            Assert.That(encoded.AuthorId, Is.EqualTo("satoki"));
            Assert.That(encoded.TicksPerBeat, Is.EqualTo(24));
            Assert.That(encoded.NoteCount, Is.EqualTo(3));
            Assert.That(encoded.Length, Is.EqualTo(24 + 1), "sequence spans beat 0 to the last note at beat 1");
        });
    }

    [Test]
    public void Places_tokens_at_the_right_ticks()
    {
        var chart = new TaikoChart([Segment], [
            At(0.0, TaikoColor.Don),
            At(1.0, TaikoColor.Kat, finisher: true)
        ]);

        var encoded = new MapTokenizer(ticksPerBeat: 48).Encode(chart, "a");

        Assert.Multiple(() =>
        {
            Assert.That(encoded.Tokens[0], Is.EqualTo(TaikoToken.Don));
            Assert.That(encoded.Tokens[48], Is.EqualTo(TaikoToken.LargeKat), "beat 1 lands on tick 48");
            Assert.That(encoded.Tokens[24], Is.EqualTo(TaikoToken.None), "the half-beat tick is empty");
        });
    }

    [Test]
    public void Round_trips_a_two_tempo_chart_across_segments()
    {
        var seg0 = new TimingSegment(startMs: 0.0, bpm: 120.0);   // beat = 500 ms
        var seg1 = new TimingSegment(startMs: 4000.0, bpm: 180.0); // beat ≈ 333.33 ms, different tempo

        NoteEvent On(TimingSegment seg, double beat, TaikoColor color, bool finisher = false) =>
            new(Math.Round(seg.BeatsToTime(beat)), color, finisher);

        var chart = new TaikoChart([seg0, seg1], [
            On(seg0, 0.0, TaikoColor.Don),
            On(seg0, 2.0, TaikoColor.Kat),
            On(seg1, 0.0, TaikoColor.Don, finisher: true), // first note of the faster region
            On(seg1, 1.0, TaikoColor.Kat),
            On(seg1, 3.0, TaikoColor.Don)
        ]);

        var tokenizer = new MapTokenizer();
        var encoded = tokenizer.Encode(chart, "mapper-x");
        var decoded = tokenizer.Decode(encoded);

        Assert.Multiple(() =>
        {
            Assert.That(decoded.Notes, Is.EqualTo(chart.Notes), "multi-tempo decode(encode) is lossless across the tempo change");
            Assert.That(encoded.Segments, Has.Count.EqualTo(2));
            Assert.That(encoded.SegmentTickCounts.Sum(), Is.EqualTo(encoded.Length));
        });
    }

    [Test]
    public void Empty_chart_encodes_to_an_empty_sequence()
    {
        var chart = new TaikoChart([Segment], []);

        var encoded = new MapTokenizer().Encode(chart, "a");
        var decoded = new MapTokenizer().Decode(encoded);

        Assert.Multiple(() =>
        {
            Assert.That(encoded.Length, Is.Zero);
            Assert.That(decoded.Notes, Is.Empty);
        });
    }
}
