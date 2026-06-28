using System.Collections.Concurrent;
using TaikoMapper.Audio.Decoding;
using TaikoMapper.Audio.Onsets;
using TaikoMapper.Audio.Timing;
using TaikoMapper.Beatmap.IO;

namespace TaikoMapper.Ml.Evaluation;

/// <summary>How the detected timing compares to a map's human (ground-truth) timing points.</summary>
public sealed record TimingEvalResult(
    string Id,
    double HumanBpm,
    double DetectedBpm,
    double OffsetPhaseErrorMs,
    int HumanTimingPoints,
    int DetectedSegments);

/// <summary>
/// Scores <see cref="TimingAnalyzer"/> against a folder of taiko maps whose human timing points are
/// the ground truth: per map it reports the offset-phase error, the auto-detected BPM vs the map's
/// BPM, and the detected-vs-human timing-point counts. Runs in parallel and caches the per-song
/// onset envelope (the expensive part), like the corpus builder.
/// </summary>
public sealed class TimingEvaluator
{
    private sealed record Job(string OsuPath, string AudioPath, string Id, double Bpm, double OffsetMs, int HumanTimingPoints);

    public IReadOnlyList<TimingEvalResult> Evaluate(string folder, Action<string>? log = null, int? maxParallelism = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);

        var jobs = new List<Job>();
        foreach (var osuPath in Directory.EnumerateFiles(folder, "*.osu", SearchOption.AllDirectories))
        {
            try
            {
                var beatmap = BeatmapIo.Load(osuPath);
                if (beatmap.BeatmapInfo.Ruleset.OnlineID != 1)
                    continue;

                var timingPoints = beatmap.ControlPointInfo.TimingPoints;
                if (timingPoints.Count == 0 || timingPoints[0].BeatLength <= 0)
                    continue;

                var audio = Path.Combine(Path.GetDirectoryName(osuPath) ?? ".", beatmap.Metadata.AudioFile);
                if (!File.Exists(audio))
                    continue;

                jobs.Add(new Job(osuPath, audio,
                    $"{beatmap.Metadata.Author.Username}/{beatmap.BeatmapInfo.DifficultyName}",
                    60_000.0 / timingPoints[0].BeatLength, timingPoints[0].Time, timingPoints.Count));
            }
            catch (Exception)
            {
                // ignore unreadable maps
            }
        }

        var envelopeCache = new ConcurrentDictionary<string, Lazy<OnsetEnvelope>>(StringComparer.OrdinalIgnoreCase);
        var results = new ConcurrentBag<TimingEvalResult>();
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = maxParallelism is > 0 ? maxParallelism.Value : Environment.ProcessorCount,
        };

        Parallel.ForEach(jobs, options, job =>
        {
            try
            {
                var envelope = envelopeCache.GetOrAdd(
                    Path.GetFullPath(job.AudioPath),
                    p => new Lazy<OnsetEnvelope>(() => new SpectralFluxAnalyzer().Analyze(AudioDecoder.Decode(p)),
                        LazyThreadSafetyMode.ExecutionAndPublication)).Value;

                var detectedBpm = new TempoEstimator().Estimate(envelope).Bpm;
                var segments = new TimingAnalyzer().Analyze(envelope, job.Bpm);

                var beatMs = 60_000.0 / job.Bpm;
                var phaseError = Math.Abs(WrapHalf(segments[0].StartMs - job.OffsetMs, beatMs));

                results.Add(new TimingEvalResult(job.Id, job.Bpm, detectedBpm, phaseError, job.HumanTimingPoints, segments.Count));
                log?.Invoke($"{job.Id}: offsetErr {phaseError,4:F0}ms  bpm {detectedBpm:F1}/{job.Bpm:F1}  segs {segments.Count}/{job.HumanTimingPoints}");
            }
            catch (Exception ex)
            {
                log?.Invoke($"skip {Path.GetFileName(job.OsuPath)}: {ex.GetType().Name}");
            }
        });

        return results.OrderBy(r => r.Id, StringComparer.Ordinal).ToList();
    }

    private static double WrapHalf(double x, double m)
    {
        var r = ((x % m) + m) % m;
        return r > m / 2.0 ? r - m : r;
    }
}
