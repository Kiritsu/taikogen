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
}
