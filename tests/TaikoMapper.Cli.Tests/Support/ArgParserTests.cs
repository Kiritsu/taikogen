using NUnit.Framework;
using TaikoMapper.Cli.Support;

namespace TaikoMapper.Cli.Tests.Support;

public class ArgParserTests
{
    private static IReadOnlySet<string> Values(params string[] names) => new HashSet<string>(names, StringComparer.Ordinal);

    [Test]
    public void Reads_key_value_options_and_positional()
    {
        var p = new ArgParser(["song.mp3", "--difficulty", "3.0", "--seed", "7"], Values("--difficulty", "--seed"));

        Assert.Multiple(() =>
        {
            Assert.That(p.FirstPositional(), Is.EqualTo("song.mp3"));
            Assert.That(p.GetDouble("--difficulty"), Is.EqualTo(3.0));
            Assert.That(p.GetInt("--seed"), Is.EqualTo(7));
        });
    }

    [Test]
    public void Reads_key_equals_value_form()
    {
        var p = new ArgParser(["--difficulty=2.5", "--out=map.osu"], Values("--difficulty", "--out"));

        Assert.Multiple(() =>
        {
            Assert.That(p.GetDouble("--difficulty"), Is.EqualTo(2.5));
            Assert.That(p.GetString("--out"), Is.EqualTo("map.osu"));
        });
    }

    [Test]
    public void A_flag_before_a_positional_does_not_swallow_it()
    {
        // --dump is not a value option, so the path must still be found.
        var p = new ArgParser(["--dump", "song.mp3"], Values("--bpm", "--offset"));

        Assert.Multiple(() =>
        {
            Assert.That(p.GetFlag("--dump"), Is.True);
            Assert.That(p.FirstPositional(), Is.EqualTo("song.mp3"));
        });
    }

    [Test]
    public void Absent_options_are_null_and_absent_flags_false()
    {
        var p = new ArgParser(["song.mp3"], Values("--bpm"));

        Assert.Multiple(() =>
        {
            Assert.That(p.GetDouble("--bpm"), Is.Null);
            Assert.That(p.GetString("--out"), Is.Null);
            Assert.That(p.GetFlag("--dump"), Is.False);
        });
    }

    [Test]
    public void Invalid_number_throws()
    {
        var p = new ArgParser(["--difficulty", "hard"], Values("--difficulty"));

        Assert.That(() => p.GetDouble("--difficulty"), Throws.TypeOf<ArgumentException>());
    }
}
