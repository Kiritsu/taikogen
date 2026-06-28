using System.IO.Compression;
using NUnit.Framework;
using TaikoMapper.Beatmap.Conversion;
using TaikoMapper.Beatmap.IO;
using TaikoMapper.Cli.Support;
using TaikoMapper.Domain.Chart;
using TaikoMapper.Domain.Timing;

namespace TaikoMapper.Cli.Tests.Support;

public class OszPackagerTests
{
    private readonly List<string> _temp = [];

    [TearDown]
    public void Cleanup()
    {
        foreach (var f in _temp)
            try { File.Delete(f); } catch { /* best effort */ }
        _temp.Clear();
    }

    private string Temp(string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + extension);
        _temp.Add(path);
        return path;
    }

    [Test]
    public void Bundles_the_audio_and_the_osu_into_an_importable_archive()
    {
        // A stand-in "audio" file (contents don't matter to packaging).
        var audioPath = Temp(".mp3");
        File.WriteAllBytes(audioPath, [1, 2, 3, 4]);

        var chart = new TaikoChart([new TimingSegment(0.0, 150.0)],
            [new NoteEvent(0, TaikoColor.Don), new NoteEvent(200, TaikoColor.Kat)]);
        var beatmap = TaikoBeatmapBuilder.Build(chart, "Song", Path.GetFileName(audioPath), "taiko 3.0");

        var osz = Temp(".osz");
        OszPackager.Write(osz, BeatmapIo.Encode(beatmap), "Song [taiko 3.0].osu", audioPath);

        using var zip = ZipFile.OpenRead(osz);
        string[] names = [.. zip.Entries.Select(e => e.Name)];

        Assert.Multiple(() =>
        {
            Assert.That(names, Does.Contain(Path.GetFileName(audioPath)), "audio bundled");
            Assert.That(names, Does.Contain("Song [taiko 3.0].osu"), ".osu bundled");

            var osuEntry = zip.Entries.First(e => e.Name.EndsWith(".osu", StringComparison.Ordinal));
            using var reader = new StreamReader(osuEntry.Open());
            var text = reader.ReadToEnd();
            Assert.That(text, Does.StartWith("osu file format"));
            Assert.That(text, Does.Contain("[HitObjects]"));
        });
    }
}
