using System.Numerics;
using FftFlat;
using TaikoMapper.Audio.Decoding;

namespace TaikoMapper.Audio.Onsets;

/// <summary>
/// Computes an onset detection function via half-wave-rectified spectral flux
/// over a short-time Fourier transform.
/// </summary>
/// <remarks>
/// Not thread-safe: the instance reuses internal FFT/magnitude buffers across
/// frames to avoid per-frame allocation. Use one instance per
/// thread. <see cref="FrameSize"/> must be a power of two.
/// </remarks>
public sealed class SpectralFluxAnalyzer
{
    private readonly int _frameSize;
    private readonly int _hopSize;
    private readonly int _bins;
    private readonly int _bandCount;
    private readonly int[] _binBand;   // which log-spaced band each FFT bin contributes to (-1 = skip)
    private readonly double[] _window;
    private readonly Complex[] _buffer;
    private double[] _prevMagnitude;
    private double[] _currentMagnitude;
    private readonly FastFourierTransform _fft;

    // A 512-sample window (~12 ms) and 128-sample hop (~3 ms) keep enough frequency resolution for the
    // tempo/band features while resolving onsets close enough to separate fast drum bursts (down to ~1/16
    // at high tempos), which a wider window would smear into one bump.
    public SpectralFluxAnalyzer(int frameSize = 512, int hopSize = 128, int bandCount = 6)
    {
        if (!IsPowerOfTwo(frameSize))
            throw new ArgumentException("Frame size must be a power of two.", nameof(frameSize));
        if (hopSize <= 0 || hopSize > frameSize)
            throw new ArgumentOutOfRangeException(nameof(hopSize), hopSize, "Hop size must be in (0, frameSize].");
        if (bandCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(bandCount), bandCount, "Band count must be positive.");

        _frameSize = frameSize;
        _hopSize = hopSize;
        _bandCount = bandCount;
        _bins = frameSize / 2 + 1;
        _binBand = LogBandMap(_bins, bandCount);
        _window = HannWindow(frameSize);
        _buffer = new Complex[frameSize];
        _prevMagnitude = new double[_bins];
        _currentMagnitude = new double[_bins];
        _fft = new FastFourierTransform(frameSize);
    }

    public int FrameSize => _frameSize;

    public int HopSize => _hopSize;

    public int BandCount => _bandCount;

    public OnsetEnvelope Analyze(MonoAudio audio)
    {
        ArgumentNullException.ThrowIfNull(audio);

        var samples = audio.Samples;
        var length = samples.Length;
        var frameCount = length >= _frameSize ? 1 + (length - _frameSize) / _hopSize : 0;
        var flux = new double[frameCount];
        var bands = new double[frameCount * _bandCount]; // per-frame log-band energies, row-major [frame][band]

        Array.Clear(_prevMagnitude);

        for (var f = 0; f < frameCount; f++)
        {
            var start = f * _hopSize;

            for (var i = 0; i < _frameSize; i++)
                _buffer[i] = new Complex(_window[i] * samples[start + i], 0.0);

            _fft.Forward(_buffer);

            var sum = 0.0;
            var bandBase = f * _bandCount;
            for (var k = 0; k < _bins; k++)
            {
                var magnitude = _buffer[k].Magnitude;
                var diff = magnitude - _prevMagnitude[k];
                if (diff > 0.0)
                    sum += diff; // half-wave rectification: only rising energy counts
                _currentMagnitude[k] = magnitude;

                var band = _binBand[k];
                if (band >= 0)
                    bands[bandBase + band] += magnitude; // per-band energy (the spectral envelope)
            }

            flux[f] = sum;
            (_prevMagnitude, _currentMagnitude) = (_currentMagnitude, _prevMagnitude);
        }

        return new OnsetEnvelope(flux, audio.SampleRate, _hopSize, _frameSize, bands, _bandCount);
    }

    /// <summary>Assigns each FFT bin to a log-spaced band (bin 0 / DC is dropped).</summary>
    private static int[] LogBandMap(int bins, int bandCount)
    {
        var map = new int[bins];
        map[0] = -1;
        var logHi = Math.Log(bins - 1);
        for (var k = 1; k < bins; k++)
        {
            var band = (int)(Math.Log(k) / logHi * bandCount);
            map[k] = Math.Clamp(band, 0, bandCount - 1);
        }
        return map;
    }

    private static double[] HannWindow(int size)
    {
        var w = new double[size];
        for (var i = 0; i < size; i++)
            w[i] = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (size - 1)));
        return w;
    }

    private static bool IsPowerOfTwo(int x) => x > 0 && (x & (x - 1)) == 0;
}
