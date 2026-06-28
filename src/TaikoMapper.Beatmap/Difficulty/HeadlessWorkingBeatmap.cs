using osu.Framework.Audio.Track;
using osu.Framework.Graphics.Textures;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Taiko;
using osu.Game.Skinning;
using TaikoMapper.Beatmap.IO;

namespace TaikoMapper.Beatmap.Difficulty;

/// <summary>
/// Minimal in-memory <see cref="WorkingBeatmap"/> for running osu! difficulty
/// calculation headlessly — no game host, audio device, or graphics context.
/// </summary>
/// <remarks>
/// All audio/visual resources return null; only the beatmap data and a real, instantiable
/// <see cref="osu.Game.Rulesets.RulesetInfo"/> are needed for the difficulty calculator to
/// convert and analyse the map.
/// </remarks>
public sealed class HeadlessWorkingBeatmap : WorkingBeatmap
{
    private readonly IBeatmap _beatmap;

    public HeadlessWorkingBeatmap(IBeatmap beatmap)
        : base(beatmap.BeatmapInfo, audioManager: null)
    {
        _beatmap = beatmap;

        // We only reference the taiko ruleset (this is a taiko mapper), so only
        // native taiko maps are supported here. The decoder leaves
        // BeatmapInfo.Ruleset as a bare RulesetInfo that cannot be instantiated;
        // swap in a concrete TaikoRuleset so CreateInstance() works during
        // difficulty calculation. (ppy's loader switches over all four modes
        // because it references every ruleset; we deliberately don't.)
        var rulesetId = beatmap.BeatmapInfo.Ruleset.OnlineID;
        if (rulesetId != TaikoRulesetId)
        {
            throw new NotSupportedException(
                $"Expected an osu!taiko beatmap (ruleset id {TaikoRulesetId}) but got ruleset id {rulesetId}. " +
                "Cross-mode conversion is out of scope; reference the source ruleset package to add it.");
        }

        beatmap.BeatmapInfo.Ruleset = new TaikoRuleset().RulesetInfo;
    }

    private const int TaikoRulesetId = 1;

    /// <summary>Loads a working beatmap from a .osu file path.</summary>
    public static HeadlessWorkingBeatmap FromFile(string path) => new(BeatmapIo.Load(path));

    protected override IBeatmap GetBeatmap() => _beatmap;
    public override Texture? GetBackground() => null;
    protected override Track? GetBeatmapTrack() => null;
    protected override ISkin? GetSkin() => null;
    public override Stream? GetStream(string storagePath) => null;
}
