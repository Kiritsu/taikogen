namespace TaikoMapper.Domain.Rhythm;

/// <summary>
/// A supported beat subdivision. The enum value is the number of ticks per beat
/// (the denominator of the musical fraction): 1/1→1, 1/2→2, 1/3→3, 1/4→4,
/// 1/6→6, 1/8→8. 1/12 and 1/16 may be added later.
/// </summary>
public enum BeatDivisor
{
    /// <summary>1/1 — on the beat.</summary>
    Whole = 1,

    /// <summary>1/2 — half-beat (off-beats).</summary>
    Half = 2,

    /// <summary>1/3 — triplet.</summary>
    Triplet = 3,

    /// <summary>1/4 — sixteenth-note grid (four per beat).</summary>
    Quarter = 4,

    /// <summary>1/6 — triplet sixteenths (six per beat).</summary>
    Sextuplet = 6,

    /// <summary>1/8 — eight per beat.</summary>
    Eighth = 8,

    /// <summary>1/12 — triplet 32nds (twelve per beat). Used for dense placement, not onset snapping.</summary>
    Twelfth = 12,

    /// <summary>1/16 — sixteen per beat. Used for dense placement, not onset snapping.</summary>
    Sixteenth = 16,
}

public static class BeatDivisors
{
    /// <summary>
    /// Supported divisors ordered coarsest-first, so quantization prefers the
    /// simpler rhythm when an onset lands on several grids at once (e.g. a
    /// position shared by 1/2 and 1/4 is labelled 1/2).
    /// </summary>
    public static readonly IReadOnlyList<BeatDivisor> Supported =
    [
        BeatDivisor.Whole,
        BeatDivisor.Half,
        BeatDivisor.Triplet,
        BeatDivisor.Quarter,
        BeatDivisor.Sextuplet,
        BeatDivisor.Eighth,
    ];

    /// <summary>
    /// <see cref="Supported"/> plus the finer 1/12 and 1/16 subdivisions. Onset quantization
    /// uses the coarser <see cref="Supported"/> set.
    /// </summary>
    public static readonly IReadOnlyList<BeatDivisor> Extended =
    [
        BeatDivisor.Whole,
        BeatDivisor.Half,
        BeatDivisor.Triplet,
        BeatDivisor.Quarter,
        BeatDivisor.Sextuplet,
        BeatDivisor.Eighth,
        BeatDivisor.Twelfth,
        BeatDivisor.Sixteenth,
    ];

    /// <summary>Number of grid ticks per beat for this divisor.</summary>
    public static int TicksPerBeat(this BeatDivisor divisor) => (int)divisor;
}
