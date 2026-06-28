namespace TaikoMapper.Domain.Rhythm;

/// <summary>
/// A detected musical onset: the time a sound begins and a relative strength.
/// Produced by peak-picking the onset envelope.
/// </summary>
/// <param name="TimeMs">Onset time in milliseconds from the start of the audio.</param>
/// <param name="Strength">Relative onset strength, normalized to (0, 1].</param>
public readonly record struct Onset(double TimeMs, double Strength);
