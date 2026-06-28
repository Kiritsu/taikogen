using osu.Framework.Logging;

namespace TaikoMapper.Beatmap.IO;

/// <summary>
/// Controls osu!framework's global logger. Running rulesets headlessly emits
/// framework log lines (e.g. the "RulesetStore was not provided" notice) to the
/// console/stderr and a Logs folder; a CLI tool wants that silenced.
/// </summary>
public static class OsuLogging
{
    /// <summary>Disables all osu!framework logging output.</summary>
    public static void Silence() => Logger.Enabled = false;
}
