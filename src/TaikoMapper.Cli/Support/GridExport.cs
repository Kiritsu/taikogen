using System.Text.Json;
using TaikoMapper.Audio.Grid;
using TaikoMapper.Domain.Rhythm;

namespace TaikoMapper.Cli.Support;

/// <summary>
/// Writes the detected timing + rhythm grid to JSON — an inspectable snapshot of the analysis.
/// Divisors are emitted as their denominators (1/4 → 4).
/// </summary>
internal static class GridExport
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static void Write(string path, string source, RhythmAnalysis analysis, RhythmGrid grid)
    {
        var segment = grid.PrimarySegment;

        var dto = new GridDto(
            source,
            new SegmentDto(segment.Bpm, segment.StartMs, segment.BeatLengthMs, segment.BeatsPerMeasure),
            analysis.Confidence,
            analysis.BpmOverridden,
            analysis.OffsetOverridden,
            [.. analysis.Candidates.Select(c => new CandidateDto(c.Bpm, c.Strength))],
            [.. grid.SupportedDivisors.Select(d => (int)d)],
            grid.OnTickFraction,
            [.. grid.Onsets.Select(o => new OnsetDto(o.Onset.TimeMs, o.Onset.Strength, o.SnappedMs, (int)o.Divisor, o.ResidualMs, o.OnTick))]);

        File.WriteAllText(path, JsonSerializer.Serialize(dto, JsonOptions));
    }

    private sealed record GridDto(
        string Source,
        SegmentDto Segment,
        double Confidence,
        bool BpmOverridden,
        bool OffsetOverridden,
        IReadOnlyList<CandidateDto> TempoCandidates,
        IReadOnlyList<int> Divisors,
        double OnTickFraction,
        IReadOnlyList<OnsetDto> Onsets);

    private sealed record SegmentDto(double Bpm, double OffsetMs, double BeatLengthMs, int BeatsPerMeasure);

    private sealed record CandidateDto(double Bpm, double Strength);

    private sealed record OnsetDto(double TimeMs, double Strength, double SnappedMs, int Divisor, double ResidualMs, bool OnTick);
}
