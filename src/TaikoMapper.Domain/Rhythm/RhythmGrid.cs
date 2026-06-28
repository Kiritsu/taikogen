using TaikoMapper.Domain.Timing;

namespace TaikoMapper.Domain.Rhythm;

/// <summary>
/// A song's timing and rhythm: the ordered timing segments, the onsets snapped onto their
/// beat grid, and which divisors the grid uses. Everything downstream reads from it.
/// </summary>
public sealed class RhythmGrid
{
    public RhythmGrid(
        IReadOnlyList<TimingSegment> segments,
        IReadOnlyList<QuantizedOnset> onsets,
        IReadOnlyList<BeatDivisor> supportedDivisors)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(onsets);
        ArgumentNullException.ThrowIfNull(supportedDivisors);
        if (segments.Count == 0)
            throw new ArgumentException("A rhythm grid needs at least one timing segment.", nameof(segments));

        Segments = segments;
        Onsets = onsets;
        SupportedDivisors = supportedDivisors;
    }

    public IReadOnlyList<TimingSegment> Segments { get; }

    public IReadOnlyList<QuantizedOnset> Onsets { get; }

    public IReadOnlyList<BeatDivisor> SupportedDivisors { get; }

    /// <summary>The first timing segment — a convenience for the common single-tempo case.</summary>
    public TimingSegment PrimarySegment => Segments[0];

    /// <summary>Fraction of onsets that snapped cleanly onto a tick.</summary>
    public double OnTickFraction =>
        Onsets.Count == 0 ? 0.0 : (double)Onsets.Count(o => o.OnTick) / Onsets.Count;
}
