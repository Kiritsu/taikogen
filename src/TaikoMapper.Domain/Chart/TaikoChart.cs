using TaikoMapper.Domain.Timing;

namespace TaikoMapper.Domain.Chart;

/// <summary>
/// Timing plus a sequence of placed notes — the in-memory form of a map. This is what gets
/// written to a <c>.osu</c> file and what the difficulty calculator scores.
/// </summary>
public sealed class TaikoChart
{
    public TaikoChart(IReadOnlyList<TimingSegment> segments, IReadOnlyList<NoteEvent> notes)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(notes);
        if (segments.Count == 0)
            throw new ArgumentException("A chart needs at least one timing segment.", nameof(segments));

        Segments = segments;
        Notes = notes;
    }

    public IReadOnlyList<TimingSegment> Segments { get; }

    public IReadOnlyList<NoteEvent> Notes { get; }

    public int NoteCount => Notes.Count;

    /// <summary>Average notes per second across the span of placed notes (0 if &lt; 2 notes).</summary>
    public double NotesPerSecond
    {
        get
        {
            if (Notes.Count < 2)
                return 0.0;

            var spanMs = Notes[^1].TimeMs - Notes[0].TimeMs;
            return spanMs > 0 ? Notes.Count / (spanMs / 1000.0) : 0.0;
        }
    }
}
