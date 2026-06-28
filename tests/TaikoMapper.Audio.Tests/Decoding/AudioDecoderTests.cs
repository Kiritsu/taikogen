using NAudio.Wave;
using NUnit.Framework;
using TaikoMapper.Audio.Decoding;

namespace TaikoMapper.Audio.Tests.Decoding;

/// <summary>
/// Decode pipeline: format read → downmix to mono → resample to a fixed rate.
/// WAV is written with NAudio (IEEE float) so values survive a round trip.
/// </summary>
public class AudioDecoderTests
{
    private readonly List<string> _tempFiles = [];

    [TearDown]
    public void Cleanup()
    {
        foreach (var f in _tempFiles)
        {
            try { File.Delete(f); } catch { /* best effort */ }
        }
        _tempFiles.Clear();
    }

    [Test]
    public void Wav_mono_round_trips_samples_without_resampling()
    {
        const int sampleRate = 44100;
        var tone = SineWave(440.0, 0.5, sampleRate, lengthSeconds: 1.0);
        var path = WriteWav(tone, channels: 1, sampleRate);

        var decoded = AudioDecoder.Decode(path, targetSampleRate: sampleRate);

        Assert.Multiple(() =>
        {
            Assert.That(decoded.SampleRate, Is.EqualTo(sampleRate));
            Assert.That(decoded.Length, Is.EqualTo(tone.Length));
            Assert.That(MaxAbsDiff(decoded.Samples, tone), Is.LessThan(1e-4));
        });
    }

    [Test]
    public void Wav_stereo_downmixes_to_channel_average()
    {
        const int sampleRate = 44100;
        var frames = sampleRate / 2;

        // L = +0.5, R = -0.5  ->  mono ≈ 0;   then a block of L = R = 0.3 -> mono ≈ 0.3
        var interleaved = new float[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            var second = i >= frames / 2;
            interleaved[i * 2] = second ? 0.3f : 0.5f;
            interleaved[i * 2 + 1] = second ? 0.3f : -0.5f;
        }

        var path = WriteWav(interleaved, channels: 2, sampleRate);
        var decoded = AudioDecoder.Decode(path, targetSampleRate: sampleRate);

        Assert.Multiple(() =>
        {
            Assert.That(decoded.Length, Is.EqualTo(frames));
            Assert.That(decoded.Samples[frames / 4], Is.EqualTo(0.0f).Within(1e-4), "L+R cancel");
            Assert.That(decoded.Samples[3 * frames / 4], Is.EqualTo(0.3f).Within(1e-4), "equal channels preserved");
        });
    }

    [Test]
    public void Wav_is_resampled_to_target_rate()
    {
        const int sourceRate = 22050;
        const int targetRate = 44100;
        var tone = SineWave(440.0, 0.5, sourceRate, lengthSeconds: 1.0);
        var path = WriteWav(tone, channels: 1, sourceRate);

        var decoded = AudioDecoder.Decode(path, targetSampleRate: targetRate);

        Assert.Multiple(() =>
        {
            Assert.That(decoded.SampleRate, Is.EqualTo(targetRate));
            // ~2x the samples (resampler latency makes this approximate).
            Assert.That(decoded.Length, Is.EqualTo(tone.Length * 2).Within(tone.Length * 0.02));
            Assert.That(decoded.DurationSeconds, Is.EqualTo(1.0).Within(0.02));
        });
    }

    [Test]
    public void Unsupported_extension_throws()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".flac");
        File.WriteAllBytes(path, [0, 1, 2, 3]);
        _tempFiles.Add(path);

        Assert.That(() => AudioDecoder.Decode(path), Throws.TypeOf<NotSupportedException>());
    }

    private static float[] SineWave(double frequency, double amplitude, int sampleRate, double lengthSeconds)
    {
        var n = (int)(lengthSeconds * sampleRate);
        var s = new float[n];
        for (var i = 0; i < n; i++)
            s[i] = (float)(amplitude * Math.Sin(2.0 * Math.PI * frequency * i / sampleRate));
        return s;
    }

    private static double MaxAbsDiff(float[] a, float[] b)
    {
        var max = 0.0;
        for (var i = 0; i < a.Length; i++)
            max = Math.Max(max, Math.Abs(a[i] - b[i]));
        return max;
    }

    private string WriteWav(float[] interleaved, int channels, int sampleRate)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".wav");
        using (var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels)))
            writer.WriteSamples(interleaved, 0, interleaved.Length);
        _tempFiles.Add(path);
        return path;
    }
}
