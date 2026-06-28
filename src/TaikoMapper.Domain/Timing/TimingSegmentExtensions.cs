namespace TaikoMapper.Domain.Timing;

/// <summary>
/// Helpers for working with an ordered list of <see cref="TimingSegment"/>s (a song's timing
/// points). The shared currency for multi-tempo support: every stage resolves the active
/// segment at a time rather than assuming one global tempo.
/// </summary>
public static class TimingSegmentExtensions
{
    extension(IReadOnlyList<TimingSegment> segments)
    {
        /// <summary>
        /// The segment active at <paramref name="timeMs"/>: the last segment starting at or before it
        /// (the first segment when the time precedes them all). Assumes <paramref name="segments"/> is
        /// ordered by <see cref="TimingSegment.StartMs"/>, which is how grids are built.
        /// </summary>
        public TimingSegment SegmentAt(double timeMs)
        {
            ArgumentNullException.ThrowIfNull(segments);
            if (segments.Count == 0)
                throw new ArgumentException("At least one timing segment is required.", nameof(segments));

            var active = segments[0];
            for (var i = 1; i < segments.Count; i++)
            {
                if (segments[i].StartMs <= timeMs)
                    active = segments[i];
                else
                    break;
            }

            return active;
        }

        /// <summary>Index of the segment active at <paramref name="timeMs"/> (companion to <see cref="SegmentAt"/>).</summary>
        public int SegmentIndexAt(double timeMs)
        {
            ArgumentNullException.ThrowIfNull(segments);
            var index = 0;
            for (var i = 1; i < segments.Count; i++)
            {
                if (segments[i].StartMs <= timeMs)
                    index = i;
                else
                    break;
            }

            return index;
        }
    }
}
