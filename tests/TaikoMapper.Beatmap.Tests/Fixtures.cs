namespace TaikoMapper.Beatmap.Tests;

/// <summary>Locates golden .osu fixtures copied next to the test assembly.</summary>
internal static class Fixtures
{
    public const string BasicTaiko = "taiko-basic.osu";

    public static string Path(string name) =>
        System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
}
