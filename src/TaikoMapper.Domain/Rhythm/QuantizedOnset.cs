namespace TaikoMapper.Domain.Rhythm;

/// <summary>
/// An <see cref="Rhythm.Onset"/> aligned to the rhythm grid: the nearest tick,
/// which divisor that tick belongs to, and how far the raw onset sat from it.
/// The residual is a "snap confidence" — how far the onset moved to reach the tick — so
/// low-confidence snaps can be told apart from clean ones.
/// </summary>
/// <param name="Onset">The raw detected onset.</param>
/// <param name="SnappedMs">Time of the nearest grid tick, in milliseconds.</param>
/// <param name="Divisor">Coarsest divisor whose grid contains the snapped tick.</param>
/// <param name="ResidualMs">Signed offset of the raw onset from the tick (onset − tick), in ms.</param>
/// <param name="OnTick">True when the onset sits within tolerance of its tick.</param>
public readonly record struct QuantizedOnset(
    Onset Onset,
    double SnappedMs,
    BeatDivisor Divisor,
    double ResidualMs,
    bool OnTick)
{
    public double AbsResidualMs => Math.Abs(ResidualMs);
}
