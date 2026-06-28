namespace TaikoMapper.Domain.Timing;

/// <summary>
/// A region of constant tempo, starting at <see cref="StartMs"/> with a fixed BPM and meter.
/// Maps directly to an osu! uninherited timing point. A song with drift or tempo changes is
/// a sequence of these.
/// </summary>
public readonly record struct TimingSegment
{
    public TimingSegment(double startMs, double bpm, int beatsPerMeasure = 4)
    {
        if (bpm <= 0 || !double.IsFinite(bpm))
            throw new ArgumentOutOfRangeException(nameof(bpm), bpm, "BPM must be a positive, finite value.");
        if (beatsPerMeasure <= 0)
            throw new ArgumentOutOfRangeException(nameof(beatsPerMeasure), beatsPerMeasure, "Meter must be positive.");

        StartMs = startMs;
        Bpm = bpm;
        BeatsPerMeasure = beatsPerMeasure;
    }

    /// <summary>Time of the first beat of this segment, in milliseconds (osu! "offset").</summary>
    public double StartMs { get; }

    /// <summary>Tempo in beats per minute.</summary>
    public double Bpm { get; }

    /// <summary>Beats per measure (meter numerator). Defaults to 4/4.</summary>
    public int BeatsPerMeasure { get; }

    /// <summary>Milliseconds per beat — osu!'s uninherited timing point "beatLength".</summary>
    public double BeatLengthMs => 60_000.0 / Bpm;

    /// <summary>Beats elapsed from this segment's start at the given absolute time (ms).</summary>
    public double TimeToBeats(double ms) => (ms - StartMs) / BeatLengthMs;

    /// <summary>Absolute time (ms) of a position measured in beats from this segment's start.</summary>
    public double BeatsToTime(double beats) => StartMs + beats * BeatLengthMs;
}
