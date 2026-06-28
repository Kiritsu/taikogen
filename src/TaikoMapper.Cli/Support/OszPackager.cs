using System.IO.Compression;

namespace TaikoMapper.Cli.Support;

/// <summary>
/// Writes an <c>.osz</c> — the zip archive osu!lazer imports directly — bundling the
/// audio file together with the generated <c>.osu</c>. The audio is stored under its
/// own file name so it matches the beatmap's <c>AudioFilename</c>.
/// </summary>
internal static class OszPackager
{
    public static void Write(string oszPath, string osuText, string osuEntryName, string audioPath)
    {
        if (File.Exists(oszPath))
            File.Delete(oszPath);

        using var zip = ZipFile.Open(oszPath, ZipArchiveMode.Create);

        zip.CreateEntryFromFile(audioPath, Path.GetFileName(audioPath));

        var osu = zip.CreateEntry(osuEntryName);
        using var writer = new StreamWriter(osu.Open());
        writer.Write(osuText);
    }
}
