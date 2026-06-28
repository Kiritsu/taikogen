namespace TaikoMapper.Domain.Chart;

/// <summary>
/// A single placed taiko note: when it is hit, its color, and whether it is a
/// "finisher" (the large strong variant). Maps to an osu! <c>Hit</c> object.
/// </summary>
/// <param name="TimeMs">Hit time in milliseconds.</param>
/// <param name="Color">Don (centre) or Kat (rim).</param>
/// <param name="IsFinisher">True for the large/strong variant.</param>
public readonly record struct NoteEvent(double TimeMs, TaikoColor Color, bool IsFinisher = false);
