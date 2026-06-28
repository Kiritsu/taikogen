using TaikoMapper.Audio.Decoding;
using TaikoMapper.Audio.Grid;
using TaikoMapper.Beatmap.Conversion;
using TaikoMapper.Beatmap.Difficulty;
using TaikoMapper.Domain.Chart;
using TaikoMapper.Domain.Timing;
using TaikoMapper.Ml.Model;
using TaikoMapper.Ml.Representation;
using static TorchSharp.torch;

namespace TaikoMapper.Ml.Inference;

/// <summary>Outcome of difficulty targeting: the chosen chart, its official star rating, and how it was reached.</summary>
public sealed record StyleTargetingResult(TaikoChart Chart, double StarRating, double Conditioning, int Iterations);

/// <summary>
/// Generate a <see cref="TaikoChart"/> from audio in a learned author's style.
/// Analyses the audio on the same grid the model was trained on, then decodes tokens
/// <b>autoregressively</b> — one tick at a time, feeding each emitted token back in — with
/// temperature sampling from a seeded RNG (deterministic given the seed).
/// </summary>
/// <remarks>
/// The model only <i>conditions</i> on a target difficulty, so its raw output overshoots/undershoots the
/// request. <see cref="GenerateTargeted"/> closes the loop: it analyses the audio once, then binary-searches
/// the conditioning value until the <b>official</b> star rating of the decoded chart hits the target.
/// </remarks>
public sealed class StyleGenerator
{
    private readonly Func<TaikoChart, double> _rate;
    private readonly PlayabilityGuard _guard;

    /// <param name="rate">Chart → star rating. Defaults to the official calculator; tests inject a cheap function.</param>
    /// <param name="guard">Post-decode playability net (rate cap + mono-streak cap). Defaults applied if null.</param>
    public StyleGenerator(Func<TaikoChart, double>? rate = null, PlayabilityGuard? guard = null)
    {
        _rate = rate ?? (chart => TaikoDifficulty.StarRating(TaikoBeatmapBuilder.Build(chart)));
        _guard = guard ?? new PlayabilityGuard();
    }

    /// <summary>Single-shot generation: the conditioning is <paramref name="targetStars"/> as-is (no targeting loop).</summary>
    public TaikoChart Generate(
        string audioPath,
        TaikoStyleModel model,
        ModelConfig config,
        int authorId,
        double targetStars,
        double? bpm = null,
        double? offset = null,
        double temperature = 0.8,
        int seed = 0)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(config);

        var ctx = Analyze(audioPath, config, bpm, offset);
        return ctx.IsEmpty ? ctx.EmptyChart : DecodeChart(ctx, model, config, authorId, targetStars / 10.0, temperature, seed);
    }

    /// <summary>
    /// Generates and <b>difficulty-targets</b>: binary-searches the conditioning value so the decoded chart's
    /// official star rating approaches <paramref name="targetStars"/>. The audio is analysed once; only the
    /// (cheap) re-decode runs per iteration.
    /// </summary>
    public StyleTargetingResult GenerateTargeted(
        string audioPath,
        TaikoStyleModel model,
        ModelConfig config,
        int authorId,
        double targetStars,
        double? bpm = null,
        double? offset = null,
        double temperature = 0.8,
        int seed = 0,
        double tolerance = 0.25,
        int maxIterations = 6)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(config);

        var ctx = Analyze(audioPath, config, bpm, offset);
        if (ctx.IsEmpty)
            return new StyleTargetingResult(ctx.EmptyChart, 0.0, 0.0, 0);

        var charts = new Dictionary<double, TaikoChart>();
        var (conditioning, starRating, iterations) = SearchConditioning(
            c =>
            {
                var chart = DecodeChart(ctx, model, config, authorId, c, temperature, seed);
                charts[c] = chart;
                return _rate(chart);
            },
            targetStars, tolerance, maxIterations);

        return new StyleTargetingResult(charts[conditioning], starRating, conditioning, iterations);
    }

    /// <summary>
    /// Binary-searches a conditioning value in [0, 1] so <paramref name="starRatingOf"/> approaches
    /// <paramref name="targetStars"/>, assuming star rating rises with the conditioning. Returns the best
    /// candidate seen (conditioning, its rating, the iteration it was found on). Pure — unit-testable.
    /// </summary>
    public static (double Conditioning, double StarRating, int Iterations) SearchConditioning(
        Func<double, double> starRatingOf, double targetStars, double tolerance, int maxIterations)
    {
        double lo = 0.0, hi = 1.0;
        double bestC = 0.5, bestSr = 0.0, bestErr = double.PositiveInfinity;
        var bestIter = 0;

        for (var iteration = 1; iteration <= maxIterations; iteration++)
        {
            var c = 0.5 * (lo + hi);
            var sr = starRatingOf(c);
            var err = Math.Abs(sr - targetStars);

            if (err < bestErr)
            {
                bestErr = err;
                bestC = c;
                bestSr = sr;
                bestIter = iteration;
            }

            if (err <= tolerance)
                break;

            if (sr < targetStars)
                lo = c; // harder needed ⇒ raise the conditioning
            else
                hi = c; // too hard ⇒ lower it
        }

        return (bestC, bestSr, bestIter);
    }

    private static GenContext Analyze(string audioPath, ModelConfig config, double? bpm, double? offset)
    {
        var audio = AudioDecoder.Decode(audioPath);
        var analysis = new RhythmAnalyzer().Analyze(audio, bpm, offset);
        var grid = new GridAnalyzer().Build(analysis);
        var segments = analysis.Segments;
        var ticksPerBeat = config.TicksPerBeat;

        var onsets = grid.Onsets.Where(o => o.OnTick).OrderBy(o => o.SnappedMs).ToList();
        if (onsets.Count == 0)
            return GenContext.Empty(new TaikoChart(segments, []));

        var tokenGrid = TokenGrid.Build(segments, ticksPerBeat, onsets[^1].SnappedMs);
        var features = new MapFeatureExtractor().Extract(tokenGrid, grid.Onsets, analysis.Onsets, 0.0);

        var tickTimes = new double[tokenGrid.Count];
        for (var t = 0; t < tickTimes.Length; t++)
            tickTimes[t] = tokenGrid.TimeMsOf(t);

        return new GenContext(segments, ticksPerBeat, tokenGrid, features, tickTimes);
    }

    private TaikoChart DecodeChart(GenContext ctx, TaikoStyleModel model, ModelConfig config, int authorId, double conditioning, double temperature, int seed)
    {
        var difficulty = (float)Math.Clamp(conditioning, 0.0, 1.0);
        foreach (var row in ctx.Features)
            row[DifficultyIndex] = difficulty;

        var tokens = Decode(model, ctx.Features, authorId, config.FeatureCount, temperature, seed);
        tokens = _guard.Apply(tokens, ctx.TickTimesMs);

        var authorName = authorId < 0
            ? "generic"
            : config.Authors.FirstOrDefault(kv => kv.Value == authorId).Key ?? "model";
        var tokenized = new TokenizedMap(authorName, ctx.Segments, ctx.TicksPerBeat, ctx.TokenGrid.SegmentCounts, tokens);
        return new MapTokenizer(ctx.TicksPerBeat).Decode(tokenized);
    }

    private static readonly int DifficultyIndex = Array.IndexOf(MapFeatureExtractor.FeatureNames, "target_difficulty");

    private static TaikoToken[] Decode(TaikoStyleModel model, float[][] features, int authorId, int featureCount, double temperature, int seed)
    {
        model.eval();
        var rng = new Random(seed);
        var temp = Math.Max(temperature, 1e-3);
        var length = features.Length;
        var tokens = new TaikoToken[length];

        using var _ = no_grad();
        using var outer = NewDisposeScope();
        // authorId < 0 ⇒ no author given ⇒ condition on the centroid of all learned styles.
        var style = authorId < 0 ? model.GenericStyleVector() : model.StyleVector(authorId);

        Tensor? h = null;
        var prev = TaikoStyleModel.Bos;
        for (var t = 0; t < length; t++)
        {
            using var scope = NewDisposeScope();
            using var featT = tensor(features[t]).reshape(1, 1, featureCount);
            using var prevT = tensor([prev]).reshape(1, 1);

            var (logits, hN) = model.RunSequenceWithStyle(featT, style, prevT, h);
            var probs = (logits.reshape(TaikoStyleModel.Vocab) / temp).softmax(0).data<float>().ToArray();

            var token = Sample(probs, rng);
            tokens[t] = (TaikoToken)token;
            prev = token;

            h?.Dispose();
            h = hN.MoveToOuterDisposeScope();
        }
        h?.Dispose();
        return tokens;
    }

    private static int Sample(float[] probs, Random rng)
    {
        var r = rng.NextDouble();
        var acc = 0.0;
        for (var i = 0; i < probs.Length; i++)
        {
            acc += probs[i];
            if (r <= acc)
                return i;
        }
        return probs.Length - 1;
    }

    /// <summary>The reusable per-song analysis (everything but the conditioning column, which varies per candidate).</summary>
    private sealed class GenContext(
        IReadOnlyList<TimingSegment> segments,
        int ticksPerBeat,
        TokenGrid tokenGrid,
        float[][] features,
        double[] tickTimesMs)
    {
        public IReadOnlyList<TimingSegment> Segments { get; } = segments;
        public int TicksPerBeat { get; } = ticksPerBeat;
        public TokenGrid TokenGrid { get; } = tokenGrid;
        public float[][] Features { get; } = features;
        public double[] TickTimesMs { get; } = tickTimesMs;
        public bool IsEmpty { get; private init; }
        public TaikoChart EmptyChart { get; private init; } = null!;

        public static GenContext Empty(TaikoChart chart) =>
            new([], 0, null!, [], []) { IsEmpty = true, EmptyChart = chart };
    }
}
