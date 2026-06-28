using TaikoMapper.Domain.Chart;

namespace TaikoMapper.Ml.Representation;

/// <summary>
/// Converts between a placed <see cref="TaikoChart"/> and the per-tick
/// <see cref="TokenizedMap"/> the model consumes and produces — the same grid the rest of the
/// system uses, expressed as a token sequence.
/// </summary>
/// <remarks>
/// Encoding snaps each note to the nearest grid tick; decoding places a note at each
/// non-empty tick's time. Because every divisor the placer uses (1/1, 1/2, 1/3, 1/4, 1/6,
/// 1/8, 1/12, 1/16) lands on an integer tick at <see cref="DefaultTicksPerBeat"/>, a chart
/// already on the grid round-trips losslessly (<c>Decode(Encode(chart)) == chart</c>).
/// </remarks>
public sealed class MapTokenizer
{
    /// <summary>Default grid resolution. 48 = LCM that puts 1/1…1/16 (incl. 1/3, 1/6, 1/12) on integer ticks.</summary>
    public const int DefaultTicksPerBeat = 48;

    private readonly int _ticksPerBeat;

    public MapTokenizer(int ticksPerBeat = DefaultTicksPerBeat)
    {
        if (ticksPerBeat <= 0)
            throw new ArgumentOutOfRangeException(nameof(ticksPerBeat), ticksPerBeat, "Ticks per beat must be positive.");
        _ticksPerBeat = ticksPerBeat;
    }

    /// <summary>Encodes a chart's first segment into a token sequence from beat 0 to the last note.</summary>
    public TokenizedMap Encode(TaikoChart chart, string authorId)
    {
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentException.ThrowIfNullOrEmpty(authorId);

        var lastNoteMs = double.NegativeInfinity;
        foreach (var note in chart.Notes)
            lastNoteMs = Math.Max(lastNoteMs, note.TimeMs);

        var grid = TokenGrid.Build(chart.Segments, _ticksPerBeat, lastNoteMs);
        var tokens = new TaikoToken[grid.Count]; // all None; empty when there are no notes
        foreach (var note in chart.Notes)
        {
            var tick = grid.TickOfTime(note.TimeMs);
            if (tick >= 0 && tick < tokens.Length)
                tokens[tick] = TokenFor(note);
        }

        return new TokenizedMap(authorId, chart.Segments, _ticksPerBeat, grid.SegmentCounts, tokens);
    }

    /// <summary>Decodes a token sequence back into a chart, placing a note at each non-empty tick.</summary>
    public TaikoChart Decode(TokenizedMap map)
    {
        ArgumentNullException.ThrowIfNull(map);

        var grid = map.Grid();
        var notes = new List<NoteEvent>(map.NoteCount);
        for (var tick = 0; tick < map.Tokens.Count; tick++)
        {
            var token = map.Tokens[tick];
            if (token == TaikoToken.None)
                continue;

            var timeMs = Math.Round(grid.TimeMsOf(tick));
            notes.Add(NoteFor(token, timeMs));
        }

        return new TaikoChart(map.Segments, notes);
    }

    private static TaikoToken TokenFor(NoteEvent note) =>
        note.IsFinisher
            ? (note.Color == TaikoColor.Don ? TaikoToken.LargeDon : TaikoToken.LargeKat)
            : (note.Color == TaikoColor.Don ? TaikoToken.Don : TaikoToken.Kat);

    private static NoteEvent NoteFor(TaikoToken token, double timeMs) => token switch
    {
        TaikoToken.Don => new NoteEvent(timeMs, TaikoColor.Don),
        TaikoToken.Kat => new NoteEvent(timeMs, TaikoColor.Kat),
        TaikoToken.LargeDon => new NoteEvent(timeMs, TaikoColor.Don, IsFinisher: true),
        TaikoToken.LargeKat => new NoteEvent(timeMs, TaikoColor.Kat, IsFinisher: true),
        _ => throw new ArgumentOutOfRangeException(nameof(token), token, "Not a note token."),
    };
}
