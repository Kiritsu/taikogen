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
/// <remarks>
/// Two onset envelopes are computed. A <b>coarse</b> one (wide STFT window) drives tempo and
/// offset detection: it averages out sub-beat detail, so the autocorrelation locks onto the beat
/// rather than a subdivision (a sharp envelope tends to double the BPM). A <b>fine</b> one (the
/// default, narrow window) is returned as <see cref="RhythmAnalysis.Onsets"/> for peak-picking and
/// the model's features, where resolving fast bursts matters.
/// </remarks>
public sealed class RhythmAnalyzer(
    SpectralFluxAnalyzer? flux = null,
    TempoEstimator? tempo = null,
    TimingAnalyzer? timing = null,
    SpectralFluxAnalyzer? timingFlux = null)
{
    private readonly SpectralFluxAnalyzer _flux = flux ?? new SpectralFluxAnalyzer();                 // fine: onsets + features
    private readonly SpectralFluxAnalyzer _timingFlux = timingFlux ?? new SpectralFluxAnalyzer(1024, 256); // coarse: stable tempo + offset
    private readonly TempoEstimator _tempo = tempo ?? new TempoEstimator();
    private readonly TimingAnalyzer _timing = timing ?? new TimingAnalyzer();

    public RhythmAnalysis Analyze(MonoAudio audio, double? bpmOverride = null, double? offsetMsOverride = null)
    {
        ArgumentNullException.ThrowIfNull(audio);

        var onsets = _flux.Analyze(audio);              // fine — for peak-picking + features
        var timingOnsets = _timingFlux.Analyze(audio);  // coarse — for tempo + offset/drift

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
            var result = _tempo.Estimate(timingOnsets);
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
                ? _timing.Analyze(timingOnsets, bpm)
                : _timing.AnalyzeMultiTempo(timingOnsets, bpm);

        return new RhythmAnalysis(
            segments,
            confidence,
            BpmOverridden: bpmOverride.HasValue,
            OffsetOverridden: offsetMsOverride.HasValue,
            candidates,
            onsets);
    }
}
