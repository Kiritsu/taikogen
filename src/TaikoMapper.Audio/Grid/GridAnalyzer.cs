using TaikoMapper.Audio.Decoding;
using TaikoMapper.Audio.Onsets;
using TaikoMapper.Domain.Rhythm;

namespace TaikoMapper.Audio.Grid;

/// <summary>
/// Composes tempo/offset analysis with onset peak-picking and quantization to produce a
/// <see cref="RhythmGrid"/> from audio.
/// </summary>
public sealed class GridAnalyzer(
    RhythmAnalyzer? rhythm = null,
    OnsetPeakPicker? picker = null,
    RhythmQuantizer? quantizer = null)
{
    private readonly RhythmAnalyzer _rhythm = rhythm ?? new RhythmAnalyzer();
    private readonly OnsetPeakPicker _picker = picker ?? new OnsetPeakPicker();
    private readonly RhythmQuantizer _quantizer = quantizer ?? new RhythmQuantizer();

    /// <summary>Runs the full analysis from audio to a quantized grid.</summary>
    public RhythmGrid Analyze(MonoAudio audio, double? bpmOverride = null, double? offsetMsOverride = null) =>
        Build(_rhythm.Analyze(audio, bpmOverride, offsetMsOverride));

    /// <summary>Builds the grid from an existing timing analysis (avoids recomputing the envelope).</summary>
    public RhythmGrid Build(RhythmAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);

        var onsets = _picker.Pick(analysis.Onsets);
        return _quantizer.Quantize(analysis.Segments, onsets);
    }
}
