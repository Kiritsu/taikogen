using osu.Game.Beatmaps;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Taiko;

namespace TaikoMapper.Beatmap.Difficulty;

/// <summary>
/// Computes the <b>official</b> osu!taiko star rating via the library's
/// <c>TaikoDifficultyCalculator</c>. We never reimplement the difficulty math: the
/// calculator carries an internal version and its constants change between osu!
/// releases, so the value is only meaningful relative to the pinned package
/// version.
/// </summary>
public static class TaikoDifficulty
{
    /// <summary>Computes the star rating for a .osu file (no-mod).</summary>
    public static double StarRating(string osuPath) => StarRating(HeadlessWorkingBeatmap.FromFile(osuPath));

    /// <summary>Computes the star rating for an in-memory beatmap (no-mod).</summary>
    public static double StarRating(IBeatmap beatmap) => StarRating(new HeadlessWorkingBeatmap(beatmap));

    /// <summary>Computes the star rating for a working beatmap (no-mod).</summary>
    public static double StarRating(IWorkingBeatmap working) => Calculate(working).StarRating;

    /// <summary>Runs the full difficulty calculation and returns all attributes (no-mod).</summary>
    public static DifficultyAttributes Calculate(IWorkingBeatmap working)
    {
        var ruleset = new TaikoRuleset();
        var calculator = ruleset.CreateDifficultyCalculator(working);
        return calculator.Calculate();
    }
}
