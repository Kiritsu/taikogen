using NUnit.Framework;
using TaikoMapper.Beatmap.Difficulty;

namespace TaikoMapper.Beatmap.Tests.Difficulty;

public class TaikoDifficultyTests
{
    // Pinned baseline for ppy.osu.Game.Rulesets.Taiko 2026.601.0.
    private const double BaselineStarRating = 1.9627;

    [Test]
    public void StarRating_for_fixture_matches_pinned_baseline()
    {
        var stars = TaikoDifficulty.StarRating(Fixtures.Path(Fixtures.BasicTaiko));

        TestContext.Out.WriteLine($"taiko-basic.osu star rating = {stars:F4}");

        Assert.Multiple(() =>
        {
            Assert.That(double.IsFinite(stars), Is.True, "star rating must be finite");
            Assert.That(stars, Is.EqualTo(BaselineStarRating).Within(0.01),
                "official taiko star rating drifted from the pinned baseline — re-baseline only on a deliberate package bump");
        });
    }

    [Test]
    public void Difficulty_attributes_report_max_combo()
    {
        var attributes =
            TaikoDifficulty.Calculate(HeadlessWorkingBeatmap.FromFile(Fixtures.Path(Fixtures.BasicTaiko)));

        // 29 hits in the fixture; max combo should at least be positive.
        Assert.That(attributes.MaxCombo, Is.GreaterThan(0));
    }
}
