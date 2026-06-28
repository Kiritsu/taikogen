using System.Globalization;
using TaikoMapper.Audio.Decoding;
using TaikoMapper.Audio.Grid;
using TaikoMapper.Beatmap.Conversion;
using TaikoMapper.Beatmap.Difficulty;
using TaikoMapper.Beatmap.IO;
using TaikoMapper.Cli.Support;
using TaikoMapper.Domain.Chart;
using TaikoMapper.Domain.Rhythm;
using TaikoMapper.Ml.Data;
using TaikoMapper.Ml.Evaluation;
using TaikoMapper.Ml.Inference;
using TaikoMapper.Ml.Model;
using TaikoMapper.Ml.Representation;

namespace TaikoMapper.Cli;

/// <summary>Command dispatch for the console host.</summary>
internal static class App
{
    public static int Run(string[] args)
    {
        if (args.Length == 0)
            return PrintUsageAnd(1);

        var command = args[0];
        var rest = args[1..];

        try
        {
            return command switch
            {
                "analyze" => RunAnalyze(rest),
                "generate" => RunGenerate(rest),
                "difficulty" => RunDifficulty(rest),
                "dataset" => RunDataset(rest),
                "train" => RunTrain(rest),
                "timing-eval" => RunTimingEval(rest),
                "-h" or "--help" or "help" => PrintUsageAnd(0),
                _ => UnknownCommand(command),
            };
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or NotSupportedException or ArgumentException or IOException)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    // ---- analyze --------------------------------------------------------------

    private static int RunAnalyze(string[] args)
    {
        var parsed = new ArgParser(args, ValueOptions("--bpm", "--offset", "--export"));
        var path = parsed.FirstPositional();
        if (path is null)
            return Error("analyze requires an audio file path.");

        var bpm = parsed.GetDouble("--bpm");
        var offset = parsed.GetDouble("--offset");
        var dump = parsed.GetFlag("--dump");
        var dumpGrid = parsed.GetFlag("--dump-grid");
        var exportPath = parsed.GetString("--export");

        var audio = AudioDecoder.Decode(path);
        var analysis = new RhythmAnalyzer().Analyze(audio, bpm, offset);
        var segment = analysis.Segment;

        Console.WriteLine($"Analyzing: {path}");
        Console.WriteLine($"  duration    : {audio.DurationSeconds:F2} s");
        Console.WriteLine($"  sample rate : {audio.SampleRate} Hz");
        Console.WriteLine($"  BPM         : {segment.Bpm:F2}  ({Source(analysis.BpmOverridden, analysis.Confidence)})");
        Console.WriteLine($"  offset      : {segment.StartMs:F0} ms  ({(analysis.OffsetOverridden ? "manual" : "detected")})");
        Console.WriteLine($"  beat length : {segment.BeatLengthMs:F2} ms  ({segment.BeatsPerMeasure}/4)");

        if (analysis.Segments.Count > 1)
        {
            Console.WriteLine($"  timing pts  : {analysis.Segments.Count} (auto re-anchored for drift)");
            for (var i = 0; i < analysis.Segments.Count; i++)
                Console.WriteLine($"    [{i}] {analysis.Segments[i].StartMs,8:F0} ms  {analysis.Segments[i].Bpm:F2} BPM");
        }

        if (dump)
            DumpDetail(analysis);

        if (dumpGrid || exportPath is not null)
        {
            var grid = new GridAnalyzer().Build(analysis);
            if (dumpGrid)
                DumpGrid(grid);
            if (exportPath is not null)
            {
                GridExport.Write(exportPath, path, analysis, grid);
                Console.WriteLine($"  exported    : {exportPath}");
            }
        }

        return 0;
    }

    // ---- generate -------------------------------------------------------------

    private static int RunGenerate(string[] args)
    {
        var parsed = new ArgParser(args, ValueOptions("--difficulty", "--seed", "--out", "--bpm", "--offset", "--model", "--author", "--temp"));
        var path = parsed.FirstPositional();
        if (path is null)
            return Error("generate requires an audio file path.");

        var stars = parsed.GetDouble("--difficulty");
        if (stars is null)
            return Error("generate requires --difficulty <stars>.");

        var modelPath = parsed.GetString("--model");
        if (modelPath is null)
            return Error("generate requires --model <model.dat> (train one with `train`). Omit --author for a generic style.");

        var seed = parsed.GetInt("--seed") ?? 0;
        var bpm = parsed.GetDouble("--bpm");
        var offset = parsed.GetDouble("--offset");
        var outPath = parsed.GetString("--out") ?? Path.GetFileNameWithoutExtension(path) + ".osz";

        return GenerateWithModel(path, modelPath, parsed.GetString("--author"), stars.Value, parsed.GetDouble("--temp") ?? 0.8, seed, bpm, offset, outPath);
    }

    // ---- dataset (build / inspect a training corpus) --------------------------

    private static int RunDataset(string[] args)
    {
        if (args.Length > 0 && args[0] == "stats")
            return RunDatasetStats(args[1..]);
        if (args.Length == 0 || args[0] != "build")
            return Error("usage: dataset build <folder> --out <dir> [--ticks <n>] [--jobs <n>]  |  dataset stats <dir>");

        var parsed = new ArgParser(args[1..], ValueOptions("--out", "--ticks", "--jobs"));
        var folder = parsed.FirstPositional();
        if (folder is null)
            return Error("dataset build requires a folder of taiko maps + audio.");
        if (!Directory.Exists(folder))
            return Error($"folder not found: {folder}");

        var outDir = parsed.GetString("--out") ?? "dataset";
        var ticks = parsed.GetInt("--ticks") ?? MapTokenizer.DefaultTicksPerBeat;
        var jobs = parsed.GetInt("--jobs");

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var metas = new CorpusBuilder(ticks)
            .BuildDirectory(folder, outDir, line => Console.WriteLine("  " + line), jobs);

        Console.WriteLine($"dataset: {metas.Count} examples → {outDir}  ({stopwatch.Elapsed.TotalSeconds:F1}s, {jobs?.ToString() ?? "all"} jobs)");
        return 0;
    }

    private static int RunDatasetStats(string[] args)
    {
        var parsed = new ArgParser(args, ValueOptions("--min-maps"));
        var dir = parsed.FirstPositional();
        if (dir is null)
            return Error("usage: dataset stats <dir> [--min-maps <n>]");
        if (!Directory.Exists(dir))
            return Error($"folder not found: {dir}");

        var minMaps = parsed.GetInt("--min-maps") ?? 8; // below this, per-author style is hard to learn
        var stats = DatasetStats.Compute(DatasetStats.ReadManifest(dir));

        Console.WriteLine($"dataset stats: {dir}");
        Console.WriteLine($"  examples : {stats.TotalExamples}   authors : {stats.TotalAuthors}");

        Console.WriteLine("  difficulty histogram (by ★):");
        foreach (var (star, count) in stats.StarHistogram)
            Console.WriteLine($"    {star,2}★ : {new string('#', Math.Min(count, 50))} {count}");

        Console.WriteLine($"  per-author (authors with < {minMaps} maps are flagged — thin for style learning):");
        foreach (var a in stats.Authors)
        {
            var flag = a.Count < minMaps ? "  ⚠ thin" : string.Empty;
            Console.WriteLine($"    {a.Author,-24} {a.Count,3} maps  ★{a.MinStars:F1}–{a.MaxStars:F1} (mean {a.MeanStars:F1}){flag}");
        }

        var thin = stats.Authors.Count(a => a.Count < minMaps);
        Console.WriteLine($"  {thin}/{stats.TotalAuthors} authors are thin (< {minMaps} maps).");
        return 0;
    }

    // ---- timing-eval (score the detector vs corpus ground truth) --------------

    private static int RunTimingEval(string[] args)
    {
        var parsed = new ArgParser(args, ValueOptions("--jobs"));
        var folder = parsed.FirstPositional();
        if (folder is null)
            return Error("timing-eval requires a folder of taiko maps + audio.");
        if (!Directory.Exists(folder))
            return Error($"folder not found: {folder}");

        var results = new TimingEvaluator()
            .Evaluate(folder, line => Console.WriteLine("  " + line), parsed.GetInt("--jobs"));
        if (results.Count == 0)
            return Error("no taiko maps with audio found.");

        var offsetErrors = results.Select(r => r.OffsetPhaseErrorMs).OrderBy(x => x).ToArray();
        var segments = results.Select(r => r.DetectedSegments).OrderBy(x => x).ToArray();

        Console.WriteLine($"timing-eval: {results.Count} maps");
        Console.WriteLine($"  offset phase error : median {offsetErrors[offsetErrors.Length / 2]:F0} ms  mean {offsetErrors.Average():F0} ms  (<=20 ms: {results.Count(r => r.OffsetPhaseErrorMs <= 20)}/{results.Count})");
        Console.WriteLine($"  detected segments  : median {segments[segments.Length / 2]}  (==1: {results.Count(r => r.DetectedSegments == 1)}/{results.Count}; human ==1: {results.Count(r => r.HumanTimingPoints == 1)}/{results.Count})");
        Console.WriteLine($"  BPM (octave-aware) : {results.Count(r => OctaveClose(r.DetectedBpm, r.HumanBpm))}/{results.Count} within 2 BPM");
        return 0;
    }

    private static bool OctaveClose(double a, double b)
    {
        foreach (var f in new[] { 1.0, 2.0, 0.5, 3.0, 1.0 / 3.0 })
            if (Math.Abs(a * f - b) <= 2.0)
                return true;
        return false;
    }

    // ---- train ----------------------------------------------------------------

    private static int RunTrain(string[] args)
    {
        var parsed = new ArgParser(args, ValueOptions("--out", "--epochs", "--window", "--stride", "--seed", "--batch"));
        var dir = parsed.FirstPositional();
        if (dir is null)
            return Error("train requires a dataset directory (from `dataset build`).");
        if (!Directory.Exists(dir))
            return Error($"dataset not found: {dir}");

        var outPath = parsed.GetString("--out") ?? "model.dat";
        var window = parsed.GetInt("--window") ?? 512;
        var stride = parsed.GetInt("--stride") ?? 384;
        var options = new StyleTrainer.Options(
            Epochs: parsed.GetInt("--epochs") ?? 20,
            BatchSize: parsed.GetInt("--batch") ?? 8,
            Seed: parsed.GetInt("--seed") ?? 0);

        Console.WriteLine($"loading dataset from {dir} ...");
        var dataset = TaikoDataset.Load(dir, window, stride, Console.WriteLine);
        Console.WriteLine($"dataset: {dataset.Windows.Count} windows · {dataset.AuthorCount} authors · {dataset.FeatureCount} features");

        const int dModel = 128, dHidden = 128, layers = 1;
        var model = new TaikoStyleModel(dataset.FeatureCount, dataset.AuthorCount, dModel, dHidden, layers);
        var config = new ModelConfig(
            dataset.FeatureCount, dataset.TicksPerBeat, dModel, dHidden, layers,
            new Dictionary<string, int>(dataset.Authors), MapFeatureExtractor.FeatureNames);

        Console.WriteLine($"training: {options.Epochs} epochs · batch {options.BatchSize} · {dataset.Windows.Count / options.BatchSize + 1} batches/epoch · checkpoints → {outPath} ...");
        new StyleTrainer().Train(model, dataset, options, Console.WriteLine, onEpochEnd: epoch =>
        {
            StyleModelIo.Save(model, config, outPath);
            Console.WriteLine($"  checkpoint saved → {outPath} (epoch {epoch}) — safe to stop");
        });

        StyleModelIo.Save(model, config, outPath);
        Console.WriteLine($"saved model → {outPath} (+ {outPath}.json)");
        return 0;
    }

    private static int GenerateWithModel(
        string audioPath, string modelPath, string? authorName, double stars, double temperature, int seed, double? bpm, double? offset, string outPath)
    {
        if (!File.Exists(modelPath))
            return Error($"model not found: {modelPath}");

        var (model, config) = StyleModelIo.Load(modelPath);
        var authors = string.Join(", ", config.Authors.Keys);

        // --author is optional: omitting it generates in a generic, author-agnostic style
        // (the centroid of every learned author).
        var authorId = -1;
        if (authorName is not null && !config.Authors.TryGetValue(authorName, out authorId))
            return Error($"unknown author '{authorName}'. Trained authors: {authors}");

        var result = new StyleGenerator()
            .GenerateTargeted(audioPath, model, config, authorId, stars, bpm, offset, temperature, seed);
        var chart = result.Chart;
        var version = $"{authorName ?? "generic"} {result.StarRating:F1}";
        WriteChartToFile(chart, audioPath, version, outPath);

        Console.WriteLine($"Generated (model): {outPath}");
        Console.WriteLine($"  author   : {authorName ?? "generic (all authors averaged)"}");
        Console.WriteLine($"  target   : {stars:F2}★   achieved : {result.StarRating:F2}★  (temp {temperature:F2}, {result.Iterations} iters, conditioning {result.Conditioning:F2})");
        Console.WriteLine($"  notes    : {chart.NoteCount}  ({chart.NotesPerSecond:F1}/s)  {ColorBreakdown(chart)}");
        return 0;
    }

    // ---- difficulty -----------------------------------------------------------

    private static int RunDifficulty(string[] args)
    {
        var parsed = new ArgParser(args, ValueOptions());
        var path = parsed.FirstPositional();
        if (path is null)
            return Error("difficulty requires a .osu file path.");

        var stars = TaikoDifficulty.StarRating(path);
        Console.WriteLine($"{stars:F2}★  {path}");
        return 0;
    }

    // ---- shared ---------------------------------------------------------------

    /// <summary>Builds the osu! beatmap from a chart and writes it as a .osz (with audio) or a bare .osu.</summary>
    private static void WriteChartToFile(TaikoChart chart, string audioPath, string version, string outPath)
    {
        var title = Path.GetFileNameWithoutExtension(audioPath);
        var beatmap = TaikoBeatmapBuilder.Build(chart, title, Path.GetFileName(audioPath), version);

        if (outPath.EndsWith(".osz", StringComparison.OrdinalIgnoreCase))
        {
            var entryName = Sanitize($"{title} [{version}].osu");
            OszPackager.Write(outPath, BeatmapIo.Encode(beatmap), entryName, audioPath);
        }
        else
        {
            BeatmapIo.Save(beatmap, outPath);
        }
    }

    private static string Sanitize(string fileName)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            fileName = fileName.Replace(c, '_');
        return fileName;
    }

    private static IReadOnlySet<string> ValueOptions(params string[] names) => new HashSet<string>(names, StringComparer.Ordinal);

    private static string ColorBreakdown(TaikoChart chart)
    {
        var don = chart.Notes.Count(n => n.Color == TaikoColor.Don);
        var kat = chart.Notes.Count(n => n.Color == TaikoColor.Kat);
        var finishers = chart.Notes.Count(n => n.IsFinisher);
        return $"({don} don, {kat} kat, {finishers} finishers)";
    }

    private static void DumpGrid(RhythmGrid grid)
    {
        Console.WriteLine($"  rhythm grid : {grid.Onsets.Count} onsets, {grid.OnTickFraction:P0} on a tick");
        Console.WriteLine($"  divisors    : {string.Join(", ", grid.SupportedDivisors.Select(Fraction))}");

        Console.WriteLine("  onsets per divisor:");
        foreach (var divisor in grid.SupportedDivisors)
        {
            var count = grid.Onsets.Count(o => o.Divisor == divisor);
            if (count > 0)
                Console.WriteLine($"    {Fraction(divisor),-4}: {count}");
        }

        var show = Math.Min(16, grid.Onsets.Count);
        if (show == 0)
            return;

        Console.WriteLine($"  first {show} onsets (raw time -> snapped tick):");
        for (var i = 0; i < show; i++)
        {
            var o = grid.Onsets[i];
            var residual = o.ResidualMs >= 0 ? $"+{o.ResidualMs:F0}" : o.ResidualMs.ToString("F0", CultureInfo.InvariantCulture);
            var tick = o.OnTick ? "on " : "off";
            Console.WriteLine(
                $"    {o.Onset.TimeMs,8:F0} ms  s={o.Onset.Strength:F2}  -> {o.SnappedMs,8:F0} ms  {Fraction(o.Divisor),-4} {tick} ({residual} ms)");
        }
    }

    private static void DumpDetail(RhythmAnalysis analysis)
    {
        var odf = analysis.Onsets;

        Console.WriteLine("  tempo candidates:");
        foreach (var c in analysis.Candidates)
            Console.WriteLine($"    {c.Bpm,7:F2} BPM   strength {c.Strength:F3}");

        double mean = 0, max = 0;
        foreach (var v in odf.Flux)
        {
            mean += v;
            if (v > max) max = v;
        }
        mean = odf.Count > 0 ? mean / odf.Count : 0;

        Console.WriteLine($"  onset envelope: {odf.Count} frames @ {odf.FrameRate:F2} fps, mean {mean:F4}, max {max:F4}");

        var strongest = StrongestFrames(odf.Flux, count: 12);
        Array.Sort(strongest);
        var times = string.Join(", ", strongest.Select(f => $"{odf.FrameToMs(f):F0}"));
        Console.WriteLine($"  strongest onsets (ms): {times}");
    }

    private static string Fraction(BeatDivisor divisor) => $"1/{(int)divisor}";

    private static string Source(bool overridden, double confidence) =>
        overridden ? "manual" : $"detected, confidence {confidence:F2}";

    private static int[] StrongestFrames(double[] flux, int count) =>
        Enumerable.Range(0, flux.Length)
            .OrderByDescending(i => flux[i])
            .Take(Math.Min(count, flux.Length))
            .ToArray();

    private static int Error(string message)
    {
        Console.Error.WriteLine($"error: {message}");
        return 1;
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"error: unknown command '{command}'.");
        PrintUsage();
        return 1;
    }

    private static int PrintUsageAnd(int code)
    {
        PrintUsage();
        return code;
    }

    private static void PrintUsage()
    {
        Console.WriteLine(
            """
            taikomapper — osu!taiko auto-mapper (ML)

            Generation is model-based: train a model on a corpus, then generate in an author's
            (or generic) style. Run `dataset build` → `train` → `generate --model`.

            Audio / timing:
              analyze <audio> [--bpm <v>] [--offset <ms>] [--dump] [--dump-grid] [--export <file.json>]
                  Decode audio and report detected BPM / offset / timing.
                  Timing is automatic: the offset is detected and re-anchored when the grid
                  drifts, and tempo changes split into multiple timing points. --bpm / --offset
                  override detection (--offset pins a single timing point); --dump prints tempo
                  candidates and an onset-envelope summary; --dump-grid prints the quantized
                  rhythm grid; --export writes the grid to JSON.

              timing-eval <folder> [--jobs <n>]
                  Score automatic timing against a folder of maps' human timing points:
                  offset-phase error, detected vs map BPM, detected vs human segment counts.

              difficulty <map.osu>
                  Print the official osu!taiko star rating for a beatmap.

            ML author-style model:
              dataset build <folder> --out <dir> [--ticks <n>] [--jobs <n>]
                  Build a training corpus from a folder of taiko beatmaps (.osu + audio):
                  tokens + per-tick features (incl. spectral bands) + metadata per map. Runs in
                  parallel (--jobs, default = CPU cores) and caches each song's audio analysis.

              dataset stats <dir> [--min-maps <n>]
                  Summarise a built dataset: per-author map counts + difficulty coverage and a
                  star histogram; flags authors with too few maps for style learning.

              train <dataset-dir> --out <model.dat> [--epochs N] [--window W] [--stride S] [--batch B] [--seed N]
                  Train the author-style model on a built dataset (TorchSharp, CPU). Writes
                  <model.dat> + <model.dat>.json (author map + config).

              generate <audio> --model <model.dat> --difficulty <stars> [--author <name>] [--temp <t>]
                       [--seed N] [--out map.osz] [--bpm <v>] [--offset <ms>]
                  Generate a map from audio with a trained model. Targets the requested
                  --difficulty (searches the model's conditioning) and applies playability
                  guards (rate + hand-balance caps). Omit --author for a generic style — the
                  average of all learned authors. Writes an importable .osz (audio + .osu) by
                  default; use --out map.osu for a bare .osu. --temp is sampling temperature (0.8).
            """);
    }
}
