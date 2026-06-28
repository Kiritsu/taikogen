using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Formats;
using osu.Game.IO;
using osu.Game.Skinning;
using OsuBeatmap = osu.Game.Beatmaps.Beatmap;

namespace TaikoMapper.Beatmap.IO;

/// <summary>
/// Thin wrapper over the official osu! beatmap decoder/encoder. ALL .osu parsing
/// and serialization goes through here — we never hand-roll the format.
/// </summary>
/// <remarks>
/// Decode uses <see cref="Decoder.GetDecoder{T}"/> + <see cref="LineBufferedReader"/>; encode
/// uses <see cref="LegacyBeatmapEncoder"/>, which writes legacy (.osu v14) files.
/// </remarks>
public static class BeatmapIo
{
    /// <summary>Decodes a beatmap from an open stream. Does not take ownership of <paramref name="stream"/>.</summary>
    public static OsuBeatmap Decode(Stream stream)
    {
        // GetDecoder peeks the "osu file format vN" header line to pick the right
        // decoder, then Decode(reader) continues from the same reader.
        var reader = new LineBufferedReader(stream);
        var decoder = Decoder.GetDecoder<OsuBeatmap>(reader);
        return decoder.Decode(reader);
    }

    /// <summary>Loads and decodes a beatmap from a .osu file path.</summary>
    public static OsuBeatmap Load(string path)
    {
        using var stream = File.OpenRead(path);
        return Decode(stream);
    }

    /// <summary>Encodes a beatmap to a legacy .osu string.</summary>
    public static string Encode(IBeatmap beatmap, ISkin? skin = null)
    {
        using var writer = new StringWriter();
        new LegacyBeatmapEncoder(beatmap, skin, storyboard: null).Encode(writer);
        return writer.ToString();
    }

    /// <summary>Encodes a beatmap and writes it to a .osu file path (UTF-8, no BOM).</summary>
    public static void Save(IBeatmap beatmap, string path, ISkin? skin = null)
    {
        using var writer = new StreamWriter(File.Create(path));
        new LegacyBeatmapEncoder(beatmap, skin, storyboard: null).Encode(writer);
    }
}
