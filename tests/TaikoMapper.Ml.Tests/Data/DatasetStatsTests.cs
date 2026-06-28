using NUnit.Framework;
using TaikoMapper.Ml.Data;

namespace TaikoMapper.Ml.Tests.Data;

public class DatasetStatsTests
{
    private static ExampleMeta Meta(string author, double stars) =>
        new($"id_{author}_{stars}", author, 48, 180.0, 0.0, stars, 100, 13, ["onset_strength"]);

    [Test]
    public void Aggregates_per_author_counts_difficulty_and_histogram()
    {
        var metas = new[]
        {
            Meta("axer", 6.6), Meta("axer", 5.2), Meta("axer", 1.1),
            Meta("grumd", 4.9),
            Meta("grumd", 5.4),
        };

        var stats = DatasetStats.Compute(metas);

        Assert.Multiple(() =>
        {
            Assert.That(stats.TotalExamples, Is.EqualTo(5));
            Assert.That(stats.TotalAuthors, Is.EqualTo(2));

            // sorted by count desc → axer (3) first.
            var axer = stats.Authors[0];
            Assert.That(axer.Author, Is.EqualTo("axer"));
            Assert.That(axer.Count, Is.EqualTo(3));
            Assert.That(axer.MinStars, Is.EqualTo(1.1).Within(1e-9));
            Assert.That(axer.MaxStars, Is.EqualTo(6.6).Within(1e-9));

            // histogram buckets by floor(stars): 1★×1, 4★×1, 5★×2, 6★×1.
            var hist = stats.StarHistogram.ToDictionary(b => b.Star, b => b.Count);
            Assert.That(hist[1], Is.EqualTo(1));
            Assert.That(hist[5], Is.EqualTo(2));
            Assert.That(hist[6], Is.EqualTo(1));
            Assert.That(hist.ContainsKey(2), Is.False, "empty difficulty buckets are omitted");
        });
    }
}
