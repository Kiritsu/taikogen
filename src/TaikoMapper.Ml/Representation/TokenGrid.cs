using TaikoMapper.Domain.Timing;

namespace TaikoMapper.Ml.Representation;

/// <summary>
/// The per-tick timeline a <see cref="TokenizedMap"/> sits on, across one or more
/// <see cref="TimingSegment"/>s. Each segment contributes a contiguous run of ticks at its own
/// tempo: a non-final segment runs up to the next segment's start (exclusive), the final segment
/// runs to the last event (inclusive). Global tick <c>i</c> therefore maps to a specific segment
/// and a time, and a single-segment grid is identical to the old "beat <c>i/ticksPerBeat</c>" model.
/// </summary>
internal sealed class TokenGrid
{
    public IReadOnlyList<TimingSegment> Segments { get; }
    public int TicksPerBeat { get; }
    public int[] SegmentCounts { get; } // ticks contributed by each segment
    private readonly int[] _cum;          // cumulative tick starts, length Segments.Count + 1

    private TokenGrid(IReadOnlyList<TimingSegment> segments, int ticksPerBeat, int[] segmentCounts, int[] cumulative)
    {
        Segments = segments;
        TicksPerBeat = ticksPerBeat;
        SegmentCounts = segmentCounts;
        _cum = cumulative;
    }

    /// <summary>Total number of ticks (sequence length).</summary>
    public int Count => _cum[^1];

    public static TokenGrid FromCounts(IReadOnlyList<TimingSegment> segments, int ticksPerBeat, IReadOnlyList<int> counts)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(counts);
        if (segments.Count == 0)
            throw new ArgumentException("At least one segment is required.", nameof(segments));
        if (counts.Count != segments.Count)
            throw new ArgumentException("counts must have one entry per segment.", nameof(counts));

        var c = new int[counts.Count];
        var cumulative = new int[segments.Count + 1];
        for (var k = 0; k < segments.Count; k++)
        {
            c[k] = Math.Max(0, counts[k]);
            cumulative[k + 1] = cumulative[k] + c[k];
        }
        return new TokenGrid(segments, ticksPerBeat, c, cumulative);
    }

    /// <summary>A single-segment grid of exactly <paramref name="length"/> ticks (old behaviour).</summary>
    public static TokenGrid SingleSegment(TimingSegment segment, int ticksPerBeat, int length) =>
        FromCounts([segment], ticksPerBeat, [length]);

    /// <summary>
    /// Builds the timeline that spans <paramref name="segments"/> up to <paramref name="lastEventMs"/>
    /// (the last note/onset time): non-final segments fill to the next boundary, the final segment to
    /// the last event inclusive.
    /// </summary>
    public static TokenGrid Build(IReadOnlyList<TimingSegment> segments, int ticksPerBeat, double lastEventMs)
    {
        ArgumentNullException.ThrowIfNull(segments);
        var n = segments.Count;
        var counts = new int[n];
        for (var k = 0; k < n; k++)
        {
            if (k < n - 1)
            {
                var span = segments[k + 1].StartMs - segments[k].StartMs;
                counts[k] = Math.Max(0, (int)Math.Ceiling(span / segments[k].BeatLengthMs * ticksPerBeat - 1e-6));
            }
            else
            {
                var localTick = double.IsFinite(lastEventMs)
                    ? (int)Math.Round(segments[k].TimeToBeats(lastEventMs) * ticksPerBeat)
                    : -1;
                counts[k] = localTick < 0 ? 0 : localTick + 1;
            }
        }
        return FromCounts(segments, ticksPerBeat, counts);
    }

    /// <summary>The segment that global tick <paramref name="tick"/> belongs to.</summary>
    public int SegmentIndexOfTick(int tick)
    {
        for (var s = 0; s < Segments.Count; s++)
            if (tick < _cum[s + 1])
                return s;
        return Segments.Count - 1;
    }

    /// <summary>Absolute time (ms) of global tick <paramref name="tick"/>.</summary>
    public double TimeMsOf(int tick)
    {
        var s = SegmentIndexOfTick(tick);
        var local = tick - _cum[s];
        return Segments[s].BeatsToTime((double)local / TicksPerBeat);
    }

    /// <summary>Global tick nearest <paramref name="timeMs"/> (within that time's active segment); -1 if it can't be placed.</summary>
    public int TickOfTime(double timeMs)
    {
        var s = Segments.SegmentIndexAt(timeMs);
        if (SegmentCounts[s] == 0)
            return -1;
        var local = (int)Math.Round(Segments[s].TimeToBeats(timeMs) * TicksPerBeat);
        local = Math.Clamp(local, 0, SegmentCounts[s] - 1);
        return _cum[s] + local;
    }
}
