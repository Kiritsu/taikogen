using TaikoMapper.Domain.Rhythm;
using TaikoMapper.Domain.Timing;

namespace TaikoMapper.Cli.Tests;

/// <summary>Builds synthetic rhythm grids directly (no audio) for CLI/pipeline tests.</summary>
internal static class GridFactory
{
    public static RhythmGrid Quarters(double bpm, int count)
    {
        var segment = new TimingSegment(0.0, bpm);
        var quarterMs = segment.BeatLengthMs / 4.0;

        var onsets = new List<QuantizedOnset>(count);
        for (var i = 0; i < count; i++)
        {
            var time = i * quarterMs;
            var divisor = (i % 4 == 0) ? BeatDivisor.Whole
                                : (i % 2 == 0) ? BeatDivisor.Half
                                : BeatDivisor.Quarter;
            onsets.Add(new QuantizedOnset(new Onset(time, 1.0), time, divisor, 0.0, OnTick: true));
        }

        return new RhythmGrid([segment], onsets, BeatDivisors.Supported);
    }
}
