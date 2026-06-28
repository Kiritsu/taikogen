using TaikoMapper.Domain.Timing;

namespace TaikoMapper.Ml.Representation;

/// <summary>
/// A taiko map expressed as a per-tick token sequence on the rhythm grid — the unit the model
/// reads and writes, and the alignment target for per-tick audio features. Token <c>i</c> sits on the
/// <see cref="TokenGrid"/> built from <see cref="Segments"/> and <see cref="SegmentTickCounts"/>; with
/// a single segment this is just beat <c>i / <see cref="TicksPerBeat"/></c>.
/// </summary>
/// <param name="AuthorId">Identifier of the mapper whose style this example represents (conditions generation).</param>
/// <param name="Segments">Timing segments (one per tempo/offset region; the grid spans them in order).</param>
/// <param name="TicksPerBeat">Grid resolution: ticks per beat (see <see cref="MapTokenizer.DefaultTicksPerBeat"/>).</param>
/// <param name="SegmentTickCounts">Ticks contributed by each segment (sums to <c>Tokens.Count</c>).</param>
/// <param name="Tokens">One token per grid tick, across all segments to the last note.</param>
public sealed record TokenizedMap(
    string AuthorId,
    IReadOnlyList<TimingSegment> Segments,
    int TicksPerBeat,
    IReadOnlyList<int> SegmentTickCounts,
    IReadOnlyList<TaikoToken> Tokens)
{
    /// <summary>The first segment — convenient for the common single-tempo case.</summary>
    public TimingSegment Segment => Segments[0];

    /// <summary>Alias for <see cref="Segment"/>.</summary>
    public TimingSegment PrimarySegment => Segments[0];

    /// <summary>Number of grid ticks (sequence length).</summary>
    public int Length => Tokens.Count;

    /// <summary>Number of ticks that carry a note (everything but <see cref="TaikoToken.None"/>).</summary>
    public int NoteCount => Tokens.Count(t => t != TaikoToken.None);

    /// <summary>The tick timeline this map sits on.</summary>
    internal TokenGrid Grid() => TokenGrid.FromCounts(Segments, TicksPerBeat, SegmentTickCounts);
}
