using TaikoMapper.Audio.Decoding;
using TaikoMapper.Audio.Onsets;
using TaikoMapper.Audio.Timing;
using TaikoMapper.Domain.Timing;

namespace TaikoMapper.Audio.Grid;

/// <summary>
/// Full result of timing analysis. Carries the detected <see cref="Segments"/> (one per detected
/// tempo/offset region) plus inspectable intermediates: confidence, ranked tempo candidates, the
/// onset envelope, and which values were supplied by manual override rather than detected.
/// </summary>
public sealed record RhythmAnalysis(
    IReadOnlyList<TimingSegment> Segments,
    double Confidence,
    bool BpmOverridden,
    bool OffsetOverridden,
    IReadOnlyList<TempoCandidate> Candidates,
    OnsetEnvelope Onsets)
{
    /// <summary>The first segment — convenient for the common single-tempo case.</summary>
    public TimingSegment Segment => Segments[0];

    /// <summary>Alias for <see cref="Segment"/>.</summary>
    public TimingSegment PrimarySegment => Segments[0];
}

/// <summary>
/// Top-level analysis: decode → onset envelope → tempo → timing. Produces one or more
/// <see cref="TimingSegment"/>s — automatic timing (<see cref="TimingAnalyzer"/>) detects the
/// offset and re-anchors it when the grid drifts. A manual offset override pins a single
/// segment; a manual BPM override skips tempo detection. Fully automatic timing is hard and
/// will be wrong on some tracks — overrides always win.
/// </summary>
public sealed class RhythmAnalyzer(
    SpectralFluxAnalyzer? flux = null,
    TempoEstimator? tempo = null,
    TimingAnalyzer? timing = null)
{
    private readonly SpectralFluxAnalyzer _flux = flux ?? new SpectralFluxAnalyzer();
    private readonly TempoEstimator _tempo = tempo ?? new TempoEstimator();
    private readonly TimingAnalyzer _timing = timing ?? new TimingAnalyzer();

    public RhythmAnalysis Analyze(MonoAudio audio, double? bpmOverride = null, double? offsetMsOverride = null)
    {
        ArgumentNullException.ThrowIfNull(audio);

        var onsets = _flux.Analyze(audio);

        double bpm;
        double confidence;
        IReadOnlyList<TempoCandidate> candidates;

        if (bpmOverride is { } forcedBpm)
        {
            bpm = forcedBpm;
            confidence = 1.0;
            candidates = [new TempoCandidate(forcedBpm, 1.0)];
        }
        else
        {
            var result = _tempo.Estimate(onsets);
            bpm = result.Bpm;
            confidence = result.Confidence;
            candidates = result.Candidates;
        }

        // Timing detection, most-asserted wins:
        //  • manual offset → a single pinned segment;
        //  • manual BPM (no offset) → one tempo, detect offset + drift (Tier 1);
        //  • fully automatic → detect tempo changes too (Tier 2), with the detected BPM as the octave prior.
        var segments = offsetMsOverride is { } off
            ? [new TimingSegment(off, bpm)]
            : bpmOverride is not null
                ? _timing.Analyze(onsets, bpm)
                : _timing.AnalyzeMultiTempo(onsets, bpm);

        return new RhythmAnalysis(
            segments,
            confidence,
            BpmOverridden: bpmOverride.HasValue,
            OffsetOverridden: offsetMsOverride.HasValue,
            candidates,
            onsets);
    }
}
