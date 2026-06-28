using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NLayer;
using NVorbis;

namespace TaikoMapper.Audio.Decoding;

/// <summary>
/// Decodes .wav / .mp3 / .ogg into a mono <see cref="MonoAudio"/> at a fixed
/// sample rate. All decoders are managed and cross-platform:
/// WAV via NAudio.Core, MP3 via NLayer, OGG (Vorbis) via NVorbis. Resampling
/// uses NAudio's managed WDL resampler.
/// </summary>
/// <remarks>
/// Decode is a one-shot path (not the per-frame analysis hot path), so the
/// chunked accumulation here favours clarity over zero-alloc.
/// </remarks>
public static class AudioDecoder
{
    public const int DefaultSampleRate = 44100;

    private const int ChunkFrames = 16384;

    // Compressed formats carry an encoder/decoder delay (leading padding) that osu!'s
    // audio engine strips but our managed decoders do not. Left in, every detected
    // onset is ~this much late versus osu!'s timeline, so generated maps play off-beat.
    // We trim it so our timeline matches osu!. Measured ~30–38 ms on a reference MP3;
    // the principled fix is to read the LAME/Vorbis pre-skip header (future work).
    private const double CompressedDecoderDelayMs = 30.0;

    public static MonoAudio Decode(string path, int targetSampleRate = DefaultSampleRate)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (!File.Exists(path))
            throw new FileNotFoundException("Audio file not found.", path);

        var ext = Path.GetExtension(path).ToLowerInvariant();
        var (interleaved, channels, sampleRate) = ext switch
        {
            ".wav" => DecodeWav(path),
            ".mp3" => DecodeMp3(path),
            ".ogg" => DecodeOgg(path),
            _ => throw new NotSupportedException(
                $"Unsupported audio format '{ext}'. Supported formats: .wav, .mp3, .ogg.")
        };

        var mono = DownmixToMono(interleaved, channels);
        var resampled = Resample(mono, sampleRate, targetSampleRate);

        var compressed = ext is ".mp3" or ".ogg";
        if (compressed)
            resampled = TrimLeading(resampled, (int)Math.Round(CompressedDecoderDelayMs / 1000.0 * targetSampleRate));

        return new MonoAudio(resampled, targetSampleRate);
    }

    /// <summary>Drops the first <paramref name="samples"/> samples (decoder-delay compensation).</summary>
    private static float[] TrimLeading(float[] data, int samples)
    {
        if (samples <= 0 || samples >= data.Length)
            return data;
        return data[samples..];
    }

    private static (float[] interleaved, int channels, int sampleRate) DecodeWav(string path)
    {
        using var reader = new WaveFileReader(path);
        var samples = reader.ToSampleProvider();
        return ReadAll(samples.Read, samples.WaveFormat.Channels, samples.WaveFormat.SampleRate);
    }

    private static (float[] interleaved, int channels, int sampleRate) DecodeMp3(string path)
    {
        using var mp3 = new MpegFile(path);
        return ReadAll(mp3.ReadSamples, mp3.Channels, mp3.SampleRate);
    }

    private static (float[] interleaved, int channels, int sampleRate) DecodeOgg(string path)
    {
        using var vorbis = new VorbisReader(path);
        return ReadAll(vorbis.ReadSamples, vorbis.Channels, vorbis.SampleRate);
    }

    /// <summary>Reads all interleaved float samples via a chunked <c>ReadSamples</c>-style delegate.</summary>
    private static (float[] interleaved, int channels, int sampleRate) ReadAll(
        Func<float[], int, int, int> read, int channels, int sampleRate)
    {
        var samples = Drain(read, ChunkFrames * Math.Max(1, channels));
        return (samples, channels, sampleRate);
    }

    /// <summary>
    /// Pulls all samples from a chunked reader into a single exactly-sized array,
    /// growing the backing buffer geometrically — no per-chunk slice allocations.
    /// </summary>
    private static float[] Drain(Func<float[], int, int, int> read, int bufferSize)
    {
        var chunk = new float[bufferSize];
        var data = new float[bufferSize];
        var length = 0;
        int n;

        while ((n = read(chunk, 0, chunk.Length)) > 0)
        {
            if (length + n > data.Length)
                Array.Resize(ref data, Math.Max(data.Length * 2, length + n));
            Array.Copy(chunk, 0, data, length, n);
            length += n;
        }

        if (data.Length != length)
            Array.Resize(ref data, length);
        return data;
    }

    private static float[] DownmixToMono(float[] interleaved, int channels)
    {
        if (channels <= 1)
            return interleaved;

        var frames = interleaved.Length / channels;
        var mono = new float[frames];
        for (var f = 0; f < frames; f++)
        {
            var baseIndex = f * channels;
            var sum = 0f;
            for (var c = 0; c < channels; c++)
                sum += interleaved[baseIndex + c];
            mono[f] = sum / channels;
        }

        return mono;
    }

    private static float[] Resample(float[] mono, int sourceRate, int targetRate)
    {
        if (sourceRate == targetRate)
            return mono;

        ISampleProvider source = new FloatArraySampleProvider(mono, sourceRate);
        var resampler = new WdlResamplingSampleProvider(source, targetRate);
        return Drain(resampler.Read, ChunkFrames);
    }

    /// <summary>Exposes an in-memory mono float buffer as an NAudio <see cref="ISampleProvider"/>.</summary>
    private sealed class FloatArraySampleProvider(float[] data, int sampleRate) : ISampleProvider
    {
        private int _position;

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels: 1);

        public int Read(float[] buffer, int offset, int count)
        {
            var remaining = data.Length - _position;
            var n = Math.Min(count, remaining);
            if (n <= 0)
                return 0;

            Array.Copy(data, _position, buffer, offset, n);
            _position += n;
            return n;
        }
    }
}
