namespace TaikoMapper.Ml.Representation;

/// <summary>
/// The per-tick vocabulary the sequence model reads and writes: what a grid tick carries — a
/// don/kat circle, optionally the large "finisher" variant, or nothing. A note round-trips
/// losslessly through this set.
/// </summary>
/// <remarks>
/// Drum rolls and swells are deliberately not in the vocabulary; the model emits only the five
/// hit types, so its output space contains nothing that can't be decoded into a note.
/// </remarks>
public enum TaikoToken
{
    /// <summary>Empty tick — no note.</summary>
    None = 0,

    /// <summary>Don (red / centre).</summary>
    Don = 1,

    /// <summary>Kat (blue / rim).</summary>
    Kat = 2,

    /// <summary>Large (finisher) don.</summary>
    LargeDon = 3,

    /// <summary>Large (finisher) kat.</summary>
    LargeKat = 4,
}
