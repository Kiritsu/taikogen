using osu.Game.Audio;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Objects.Types;
using TaikoMapper.Domain.Chart;
using TaikoMapper.Domain.Timing;

namespace TaikoMapper.Beatmap.Conversion;

/// <summary>
/// The inverse of <see cref="TaikoBeatmapBuilder"/>: reads an osu! beatmap back into a
/// <see cref="TaikoChart"/>. Timing comes from the uninherited timing points; each circle
/// becomes a note whose color/finisher is read from its hit samples (clap/whistle ⇒ kat,
/// finish ⇒ strong). Drum rolls and swells are skipped — they are not in the note model.
/// </summary>
public static class TaikoChartExtractor
{
    public static TaikoChart Extract(IBeatmap beatmap)
    {
        ArgumentNullException.ThrowIfNull(beatmap);

        var segments = new List<TimingSegment>();
        foreach (var timing in beatmap.ControlPointInfo.TimingPoints)
        {
            if (timing.BeatLength <= 0 || !double.IsFinite(timing.BeatLength))
                continue;
            segments.Add(new TimingSegment(timing.Time, 60_000.0 / timing.BeatLength, timing.TimeSignature.Numerator));
        }

        if (segments.Count == 0)
            segments.Add(new TimingSegment(0.0, 120.0));

        var notes = new List<NoteEvent>();
        foreach (var hitObject in beatmap.HitObjects)
        {
            if (hitObject is IHasPath or IHasDuration)
                continue; // drum roll / swell — not handled by the colorer

            var color = HasRimSample(hitObject.Samples) ? TaikoColor.Kat : TaikoColor.Don;
            var finisher = HasFinishSample(hitObject.Samples);
            notes.Add(new NoteEvent(hitObject.StartTime, color, finisher));
        }

        notes.Sort((a, b) => a.TimeMs.CompareTo(b.TimeMs));
        return new TaikoChart(segments, notes);
    }

    private static bool HasRimSample(IList<HitSampleInfo> samples)
    {
        foreach (var sample in samples)
            if (sample.Name is HitSampleInfo.HIT_CLAP or HitSampleInfo.HIT_WHISTLE)
                return true;
        return false;
    }

    private static bool HasFinishSample(IList<HitSampleInfo> samples)
    {
        foreach (var sample in samples)
            if (sample.Name == HitSampleInfo.HIT_FINISH)
                return true;
        return false;
    }
}
