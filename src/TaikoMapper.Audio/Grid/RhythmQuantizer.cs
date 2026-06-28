using TaikoMapper.Domain.Rhythm;
using TaikoMapper.Domain.Timing;

namespace TaikoMapper.Audio.Grid;

/// <summary>
/// Snaps onsets onto the beat grid. Each onset is matched to
/// the nearest tick across all supported divisors; the tick is labelled with the
/// <b>coarsest</b> divisor that contains it (so a half-beat hit reads as 1/2, not
/// 1/4), and the signed residual is recorded as snap confidence.
/// </summary>
public sealed class RhythmQuantizer
{
    private readonly IReadOnlyList<BeatDivisor> _divisors;
    private readonly double _onTickToleranceMs;

    public RhythmQuantizer(IReadOnlyList<BeatDivisor>? divisors = null, double onTickToleranceMs = 12.0)
    {
        _divisors = divisors ?? BeatDivisors.Supported;
        if (_divisors.Count == 0)
            throw new ArgumentException("At least one divisor is required.", nameof(divisors));

        _onTickToleranceMs = onTickToleranceMs;
    }

    /// <summary>Quantizes a list of onsets against a single timing segment into a grid.</summary>
    public RhythmGrid Quantize(TimingSegment segment, IReadOnlyList<Onset> onsets) =>
        Quantize([segment], onsets);

    /// <summary>
    /// Quantizes onsets against multiple timing segments: each onset snaps onto the grid of the
    /// segment active at its time (so a tempo change re-aligns the grid mid-song).
    /// </summary>
    public RhythmGrid Quantize(IReadOnlyList<TimingSegment> segments, IReadOnlyList<Onset> onsets)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(onsets);
        if (segments.Count == 0)
            throw new ArgumentException("At least one timing segment is required.", nameof(segments));

        var quantized = new List<QuantizedOnset>(onsets.Count);
        foreach (var onset in onsets)
            quantized.Add(Quantize(segments.SegmentAt(onset.TimeMs), onset));

        return new RhythmGrid(segments, quantized, _divisors);
    }

    /// <summary>Snaps a single onset to its nearest grid tick.</summary>
    public QuantizedOnset Quantize(TimingSegment segment, Onset onset)
    {
        var beats = segment.TimeToBeats(onset.TimeMs);

        var bestDistance = double.PositiveInfinity;
        var bestTickBeat = 0.0;
        var bestDivisor = _divisors[0];

        // Divisors are ordered coarse→fine; the strict "<" keeps the coarsest
        // divisor on an exact tie, so shared positions get the simpler label.
        foreach (var divisor in _divisors)
        {
            var ticksPerBeat = divisor.TicksPerBeat();
            var tickBeat = Math.Round(beats * ticksPerBeat) / ticksPerBeat;
            var distance = Math.Abs(beats - tickBeat);

            if (distance < bestDistance - 1e-9)
            {
                bestDistance = distance;
                bestTickBeat = tickBeat;
                bestDivisor = divisor;
            }
        }

        var snappedMs = segment.BeatsToTime(bestTickBeat);
        var residualMs = onset.TimeMs - snappedMs;
        var onTick = Math.Abs(residualMs) <= _onTickToleranceMs;

        return new QuantizedOnset(onset, snappedMs, bestDivisor, residualMs, onTick);
    }
}
