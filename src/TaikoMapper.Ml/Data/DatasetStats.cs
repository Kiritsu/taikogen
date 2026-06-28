using System.Text.Json;

namespace TaikoMapper.Ml.Data;

/// <summary>Per-author corpus stats: how many maps and what difficulty range — style fidelity needs enough per author.</summary>
public sealed record AuthorStat(string Author, int Count, double MinStars, double MaxStars, double MeanStars);

/// <summary>Aggregate view of a dataset's coverage: totals, per-author counts/difficulty, and a star histogram.</summary>
public sealed record DatasetStatsResult(
    int TotalExamples,
    int TotalAuthors,
    IReadOnlyList<AuthorStat> Authors,
    IReadOnlyList<(int Star, int Count)> StarHistogram);

/// <summary>
/// Summarises a built dataset (from its <c>manifest.json</c> of <see cref="ExampleMeta"/>): how many maps
/// each author contributes and over what difficulty range — the data needed to answer "do I have enough
/// per author?" and "is my difficulty coverage even?". <see cref="Compute"/> is pure and unit-tested.
/// </summary>
public static class DatasetStats
{
    /// <summary>Reads a dataset's <c>manifest.json</c> into example metadata.</summary>
    public static IReadOnlyList<ExampleMeta> ReadManifest(string datasetDir)
    {
        ArgumentException.ThrowIfNullOrEmpty(datasetDir);
        var path = Path.Combine(datasetDir, "manifest.json");
        if (!File.Exists(path))
            throw new FileNotFoundException($"dataset manifest not found: {path}");

        return JsonSerializer.Deserialize<List<ExampleMeta>>(File.ReadAllText(path)) ?? [];
    }

    public static DatasetStatsResult Compute(IReadOnlyList<ExampleMeta> metas)
    {
        ArgumentNullException.ThrowIfNull(metas);

        var authors = metas
            .GroupBy(m => m.AuthorId)
            .Select(g => new AuthorStat(g.Key, g.Count(), g.Min(m => m.Stars), g.Max(m => m.Stars), g.Average(m => m.Stars)))
            .OrderByDescending(a => a.Count)
            .ThenBy(a => a.Author, StringComparer.Ordinal)
            .ToList();

        var histogram = metas
            .GroupBy(m => (int)Math.Floor(m.Stars))
            .Select(g => (Star: g.Key, Count: g.Count()))
            .OrderBy(b => b.Star)
            .ToList();

        return new DatasetStatsResult(metas.Count, authors.Count, authors, histogram);
    }
}
