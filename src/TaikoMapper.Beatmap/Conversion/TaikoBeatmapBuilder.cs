using osu.Game.Audio;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Beatmaps.Timing;
using osu.Game.Rulesets.Taiko;
using osu.Game.Rulesets.Taiko.Objects;
using TaikoMapper.Domain.Chart;
using OsuBeatmap = osu.Game.Beatmaps.Beatmap;

namespace TaikoMapper.Beatmap.Conversion;

/// <summary>
/// Builds an osu! <see cref="OsuBeatmap"/> from a <see cref="TaikoChart"/>. Timing
/// segments become uninherited <see cref="TimingControlPoint"/>s; notes become
/// taiko <see cref="Hit"/>s.
/// </summary>
/// <remarks>
/// Color and finisher are encoded via hit <see cref="HitSampleInfo"/>s, because that is what
/// both the taiko beatmap converter (which the difficulty calculator runs) and the legacy
/// encoder read — not the <c>Hit.Type</c> property. Clap ⇒ rim (kat); finish ⇒ strong.
/// </remarks>
public static class TaikoBeatmapBuilder
{
    public static OsuBeatmap Build(TaikoChart chart, string? title = null, string? audioFilename = null, string? version = null)
    {
        ArgumentNullException.ThrowIfNull(chart);

        var beatmap = new OsuBeatmap
        {
            BeatmapInfo = new BeatmapInfo
            {
                Ruleset = new TaikoRuleset().RulesetInfo,
                Difficulty = new BeatmapDifficulty(),
                DifficultyName = version ?? "Auto",
                Metadata = new BeatmapMetadata
                {
                    Title = title ?? "Generated",
                    Artist = "TaikoMapper",
                },
            },
        };

        if (audioFilename is not null)
            beatmap.Metadata.AudioFile = audioFilename;

        foreach (var segment in chart.Segments)
        {
            beatmap.ControlPointInfo.Add(segment.StartMs, new TimingControlPoint
            {
                BeatLength = segment.BeatLengthMs,
                TimeSignature = new TimeSignature(segment.BeatsPerMeasure),
            });
        }

        foreach (var note in chart.Notes)
            beatmap.HitObjects.Add(CreateHit(note));

        return beatmap;
    }

    private static Hit CreateHit(NoteEvent note)
    {
        var samples = new List<HitSampleInfo> { new(HitSampleInfo.HIT_NORMAL) };

        if (note.Color == TaikoColor.Kat)
            samples.Add(new HitSampleInfo(HitSampleInfo.HIT_CLAP));
        if (note.IsFinisher)
            samples.Add(new HitSampleInfo(HitSampleInfo.HIT_FINISH));

        return new Hit { StartTime = note.TimeMs, Samples = samples };
    }
}
