using System.Collections.Concurrent;
using osu.Game.Beatmaps;
using TaikoMapper.Audio.Decoding;
using TaikoMapper.Audio.Grid;
using TaikoMapper.Audio.Onsets;
using TaikoMapper.Beatmap.Conversion;
using TaikoMapper.Beatmap.Difficulty;
using TaikoMapper.Beatmap.IO;
using TaikoMapper.Domain.Rhythm;
using TaikoMapper.Ml.Representation;

namespace TaikoMapper.Ml.Data;

/// <summary>
/// Turns a <c>(.osu, audio)</c> pair into a <see cref="TrainingExample"/>: parse the map,
/// analyse the audio, tokenize, and extract per-tick features. This is the training corpus.
/// <see cref="BuildDirectory"/> processes a folder <b>in parallel</b> and decodes +
/// analyses each unique audio file only once (a beatmap set's difficulties share one audio,
/// and the FFT/onset analysis is timing-independent), so it's much faster than naive per-diff work.
/// </summary>
public sealed class CorpusBuilder(int ticksPerBeat = MapTokenizer.DefaultTicksPerBeat)
{
    private readonly MapTokenizer _tokenizer = new(ticksPerBeat);       // immutable → shared across threads
    private readonly MapFeatureExtractor _features = new(); // immutable → shared across threads

    /// <summary>The expensive, timing-independent per-song analysis (the onset envelope) shared by a set's difficulties.</summary>
    private sealed record SharedAudio(OnsetEnvelope Envelope);

    private sealed record MapJob(int Index, string OsuPath, string AudioPath, IBeatmap Beatmap, string Version);

    /// <summary>
    /// Scans <paramref name="folder"/> recursively and builds an example for every <b>taiko</b> .osu
    /// whose audio resolves, writing each to <paramref name="outputDir"/> plus the dataset manifest +
    /// author map. Runs in parallel; non-taiko / unreadable / audio-missing / failing maps are skipped.
    /// </summary>
    public IReadOnlyList<ExampleMeta> BuildDirectory(string folder, string outputDir, Action<string>? log = null, int? maxParallelism = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);
        ArgumentException.ThrowIfNullOrEmpty(outputDir);

        // Phase 1 (sequential, cheap): discover taiko maps, load each beatmap once, assign stable ids.
        log?.Invoke($"scanning {folder} for taiko maps...");
        var jobs = new List<MapJob>();
        int index = 0, scanned = 0;
        foreach (var osuPath in Directory.EnumerateFiles(folder, "*.osu", SearchOption.AllDirectories))
        {
            scanned++;
            if (TryLoadTaiko(osuPath, out var beatmap, out var audioPath, out var version))
                jobs.Add(new MapJob(index++, osuPath, audioPath, beatmap!, version));
            if (scanned % 50 == 0)
                log?.Invoke($"  ...scanned {scanned} .osu files, {jobs.Count} taiko maps so far");
        }

        var degree = maxParallelism is > 0 ? maxParallelism.Value : Environment.ProcessorCount;
        var songs = jobs.Select(j => Path.GetFullPath(j.AudioPath)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        log?.Invoke($"found {jobs.Count} taiko maps across {songs} songs; building with {degree} parallel jobs...");

        Directory.CreateDirectory(outputDir);

        // Phase 2 (parallel): build examples, decoding + analysing each unique audio only once.
        var audioCache = new ConcurrentDictionary<string, Lazy<SharedAudio>>(StringComparer.OrdinalIgnoreCase);
        var results = new ConcurrentBag<(int Index, ExampleMeta Meta)>();
        var options = new ParallelOptions { MaxDegreeOfParallelism = degree };
        int total = jobs.Count, completed = 0;

        Parallel.ForEach(jobs, options, job =>
        {
            try
            {
                var shared = audioCache.GetOrAdd(
                    Path.GetFullPath(job.AudioPath),
                    p => new Lazy<SharedAudio>(
                        () =>
                        {
                            log?.Invoke($"  analysing audio {Path.GetFileName(p)}...");
                            return AnalyzeAudio(p);
                        },
                        LazyThreadSafetyMode.ExecutionAndPublication)).Value;

                var example = BuildExample(job.Beatmap, shared);
                var id = $"{job.Index:D4}_{Slug(example.AuthorId)}_{Slug(job.Version)}";
                var meta = DatasetWriter.Write(example, outputDir, id);
                results.Add((job.Index, meta));
                var n = Interlocked.Increment(ref completed);
                log?.Invoke($"[{n}/{total}] {id}  ★{meta.Stars:F2}  {meta.Length}t  ({example.AuthorId})");
            }
            catch (Exception ex)
            {
                // One bad map (e.g. a malformed MP3) must not abort the run.
                var n = Interlocked.Increment(ref completed);
                log?.Invoke($"[{n}/{total}] skip {Path.GetFileName(job.OsuPath)}: {ex.GetType().Name}: {ex.Message}");
            }
        });

        var metas = results.OrderBy(r => r.Index).Select(r => r.Meta).ToList();
        DatasetWriter.WriteManifest(outputDir, metas);
        return metas;
    }

    /// <summary>Builds one example from a single (.osu, audio) pair.</summary>
    public TrainingExample Build(string osuPath, string audioPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(osuPath);
        ArgumentException.ThrowIfNullOrEmpty(audioPath);
        return BuildExample(BeatmapIo.Load(osuPath), AnalyzeAudio(audioPath));
    }

    /// <summary>Decode → onset envelope (FFT). Timing-independent, so it's cached per audio file. Peak-picking
    /// is tempo-aware and therefore done per difficulty in <see cref="BuildExample"/>.</summary>
    private static SharedAudio AnalyzeAudio(string audioPath)
    {
        var audio = AudioDecoder.Decode(audioPath);
        var envelope = new SpectralFluxAnalyzer().Analyze(audio);
        return new SharedAudio(envelope);
    }

    private TrainingExample BuildExample(IBeatmap beatmap, SharedAudio shared)
    {
        var author = beatmap.Metadata.Author.Username;
        if (string.IsNullOrWhiteSpace(author))
            author = "unknown";

        var chart = TaikoChartExtractor.Extract(beatmap);
        var stars = TaikoDifficulty.StarRating(beatmap);

        // Peak-pick (tempo-aware, so fast bursts survive) and quantize onto this difficulty's grid.
        // Same analysis the generator uses, so training features match inference features.
        var onsets = new OnsetPeakPicker().Pick(shared.Envelope, chart.Segments[0].Bpm);
        var grid = new RhythmQuantizer(BeatDivisors.Extended).Quantize(chart.Segments, onsets);
        var tokenized = _tokenizer.Encode(chart, author);
        var featureRows = _features.Extract(tokenized.Grid(), grid.Onsets, shared.Envelope, stars);

        var primary = chart.Segments[0];
        return new TrainingExample(
            author, ticksPerBeat, primary.Bpm, primary.StartMs, stars,
            MapFeatureExtractor.FeatureNames, tokenized.Tokens, featureRows);
    }

    /// <summary>Loads a taiko (.osu mode 1) whose audio file exists next to it; false otherwise.</summary>
    private static bool TryLoadTaiko(string osuPath, out IBeatmap? beatmap, out string audioPath, out string version)
    {
        beatmap = null;
        audioPath = string.Empty;
        version = string.Empty;
        try
        {
            var loaded = BeatmapIo.Load(osuPath);
            if (loaded.BeatmapInfo.Ruleset.OnlineID != 1)
                return false;

            beatmap = loaded;
            version = loaded.BeatmapInfo.DifficultyName;
            audioPath = Path.Combine(Path.GetDirectoryName(osuPath) ?? ".", loaded.Metadata.AudioFile);
            return File.Exists(audioPath);
        }
        catch (Exception)
        {
            return false; // unreadable/corrupt .osu → skip silently
        }
    }

    private static string Slug(string? text)
    {
        var chars = (text ?? string.Empty).Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-').ToArray();
        var slug = new string(chars).Trim('-');
        return slug.Length == 0 ? "x" : slug;
    }
}
