using NUnit.Framework;
using TaikoMapper.Audio.Onsets;
using TaikoMapper.Domain.Rhythm;
using TaikoMapper.Domain.Timing;
using TaikoMapper.Ml.Representation;

namespace TaikoMapper.Ml.Tests.Representation;

public class MapFeatureExtractorTests
{
    private static readonly TimingSegment Segment = new(startMs: 0.0, bpm: 180.0);

    private static OnsetEnvelope FlatEnvelope()
    {
        var flux = new double[2000];
        Array.Fill(flux, 0.5);
        return new OnsetEnvelope(flux, sampleRate: 44100, hopSize: 512, frameSize: 1024);
    }

    private static List<QuantizedOnset> Onsets(int count)
    {
        var beatMs = Segment.BeatLengthMs;
        var list = new List<QuantizedOnset>();
        for (var i = 0; i < count; i++)
        {
            var t = i * beatMs;
            list.Add(new QuantizedOnset(new Onset(t, 1.0), t, BeatDivisor.Whole, 0.0, OnTick: true));
        }
        return list;
    }

    [Test]
    public void Produces_a_rectangular_matrix_aligned_to_tokens()
    {
        var extractor = new MapFeatureExtractor();
        var length = 96;

        var features = extractor.Extract(Segment, Onsets(24), FlatEnvelope(), ticksPerBeat: 48, length: length, targetStars: 5.0);

        Assert.Multiple(() =>
        {
            Assert.That(features.Length, Is.EqualTo(length));
            Assert.That(features, Has.All.Length.EqualTo(MapFeatureExtractor.FeatureNames.Length));
            Assert.That(features.SelectMany(r => r), Has.All.Matches<float>(float.IsFinite));
        });
    }

    [Test]
    public void Is_deterministic()
    {
        var extractor = new MapFeatureExtractor();
        var a = extractor.Extract(Segment, Onsets(24), FlatEnvelope(), 48, 96, 5.0);
        var b = extractor.Extract(Segment, Onsets(24), FlatEnvelope(), 48, 96, 5.0);

        Assert.That(a.SelectMany(r => r), Is.EqualTo(b.SelectMany(r => r)));
    }

    [Test]
    public void Encodes_metrical_phase_and_difficulty()
    {
        var names = MapFeatureExtractor.FeatureNames;
        var sin = Array.IndexOf(names, "tick_in_beat_sin");
        var cos = Array.IndexOf(names, "tick_in_beat_cos");
        var diff = Array.IndexOf(names, "target_difficulty");

        var f = new MapFeatureExtractor().Extract(Segment, Onsets(8), FlatEnvelope(), ticksPerBeat: 48, length: 96, targetStars: 6.0);

        Assert.Multiple(() =>
        {
            // tick 0 is a downbeat: phase 0 → sin 0, cos 1.
            Assert.That(f[0][sin], Is.EqualTo(0f).Within(1e-5));
            Assert.That(f[0][cos], Is.EqualTo(1f).Within(1e-5));
            // tick 24 = half a beat (48/beat): phase π → sin 0, cos -1.
            Assert.That(f[24][cos], Is.EqualTo(-1f).Within(1e-5));
            Assert.That(f[0][diff], Is.EqualTo(0.6f).Within(1e-5), "6★ / 10");
        });
    }

    [Test]
    public void Fine_density_and_intensity_ease_off_where_the_song_is_calm()
    {
        var names = MapFeatureExtractor.FeatureNames;
        var density = Array.IndexOf(names, "local_density");
        var fine = Array.IndexOf(names, "local_density_fine");
        var intensity = Array.IndexOf(names, "local_intensity");

        // A dense 1/4 run only from beat 16 onward, so an early tick is genuinely outside the wide
        // (±2000 ms) density window — far enough to read as calm.
        var beatMs = Segment.BeatLengthMs;
        var busy = new List<QuantizedOnset>();
        for (var i = 0; i < 16; i++)
        {
            var t = 16 * beatMs + i * (beatMs / 4.0);
            busy.Add(new QuantizedOnset(new Onset(t, 1.0), t, BeatDivisor.Quarter, 0.0, OnTick: true));
        }

        var f = new MapFeatureExtractor().Extract(Segment, busy, FlatEnvelope(), ticksPerBeat: 48, length: 48 * 28, targetStars: 8.0);
        var calmTick = 48 * 2;  // beat 2 — > 2 s before any onset
        var busyTick = 48 * 20; // beat 20 — inside the run

        Assert.Multiple(() =>
        {
            Assert.That(fine, Is.GreaterThanOrEqualTo(0), "local_density_fine column exists");
            Assert.That(f[busyTick][density], Is.GreaterThan(f[calmTick][density]), "wide density higher where busy");
            Assert.That(f[busyTick][fine], Is.GreaterThan(f[calmTick][fine]), "fine density higher where busy");
            Assert.That(f[busyTick][intensity], Is.GreaterThan(f[calmTick][intensity]), "intensity eases off in the calm region");
        });
    }
}
