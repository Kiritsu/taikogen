using System.Text.Json;
using NUnit.Framework;
using TaikoMapper.Audio.Grid;
using TaikoMapper.Audio.Onsets;
using TaikoMapper.Audio.Timing;
using TaikoMapper.Cli.Support;
using TaikoMapper.Domain.Timing;

namespace TaikoMapper.Cli.Tests.Support;

public class GridExportTests
{
    private string _path = null!;

    [SetUp]
    public void SetUp() => _path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");

    [TearDown]
    public void TearDown()
    {
        try { File.Delete(_path); } catch { /* best effort */ }
    }

    [Test]
    public void Writes_valid_json_with_the_expected_fields()
    {
        var grid = GridFactory.Quarters(150, 8);
        var odf = new OnsetEnvelope([0.1, 0.5, 0.2], sampleRate: 44100, hopSize: 256, frameSize: 1024);
        var analysis = new RhythmAnalysis(
            [new TimingSegment(0.0, 150.0)],
            Confidence: 0.9,
            BpmOverridden: false,
            OffsetOverridden: false,
            Candidates: [new TempoCandidate(150.0, 0.9)],
            Onsets: odf);

        GridExport.Write(_path, "src.wav", analysis, grid);

        using var doc = JsonDocument.Parse(File.ReadAllText(_path));
        var root = doc.RootElement;

        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("Source").GetString(), Is.EqualTo("src.wav"));
            Assert.That(root.GetProperty("Segment").GetProperty("Bpm").GetDouble(), Is.EqualTo(150.0));
            Assert.That(root.GetProperty("Divisors").GetArrayLength(), Is.EqualTo(6));
            Assert.That(root.GetProperty("Onsets").GetArrayLength(), Is.EqualTo(8));
            Assert.That(root.GetProperty("Onsets")[0].GetProperty("Divisor").GetInt32(), Is.EqualTo(1)); // first onset → 1/1
        });
    }
}
