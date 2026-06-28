using NUnit.Framework;
using TaikoMapper.Ml.Inference;
using TaikoMapper.Ml.Representation;

namespace TaikoMapper.Ml.Tests.Inference;

public class PlayabilityGuardTests
{
    private static bool IsKat(TaikoToken t) => t is TaikoToken.Kat or TaikoToken.LargeKat;

    [Test]
    public void Caps_the_note_rate_by_dropping_notes_that_land_too_soon()
    {
        // Ten notes 10 ms apart = 100 notes/s. With a 40/s cap (25 ms min gap) the survivors thin out.
        var tokens = Enumerable.Repeat(TaikoToken.Don, 10).ToArray();
        var times = Enumerable.Range(0, 10).Select(i => (double)(i * 10)).ToArray(); // 0,10,...,90 ms

        var guarded = new PlayabilityGuard(maxNotesPerSecond: 40.0, maxMonoStreak: 99).Apply(tokens, times);

        var last = double.NegativeInfinity;
        for (var i = 0; i < guarded.Length; i++)
            if (guarded[i] != TaikoToken.None)
            {
                Assert.That(times[i] - last, Is.GreaterThanOrEqualTo(25.0 - 1e-6), "kept notes respect the min gap");
                last = times[i];
            }

        Assert.That(guarded.Count(t => t != TaikoToken.None), Is.LessThan(10), "over-dense notes were thinned");
    }

    [Test]
    public void Caps_mono_colour_streaks_for_hand_balance()
    {
        // 30 dons spaced well apart (no rate-cap interference); a max streak of 8 must break them up.
        var tokens = Enumerable.Repeat(TaikoToken.Don, 30).ToArray();
        var times = Enumerable.Range(0, 30).Select(i => (double)(i * 200)).ToArray();

        var guarded = new PlayabilityGuard(maxNotesPerSecond: 40.0, maxMonoStreak: 8).Apply(tokens, times);

        int longest = 0, streak = 0;
        bool? last = null;
        foreach (var t in guarded.Where(t => t != TaikoToken.None))
        {
            var isKat = IsKat(t);
            streak = isKat == last ? streak + 1 : 1;
            last = isKat;
            longest = Math.Max(longest, streak);
        }

        Assert.Multiple(() =>
        {
            Assert.That(longest, Is.LessThanOrEqualTo(8), "no mono streak longer than the cap");
            Assert.That(guarded.Count(t => t != TaikoToken.None), Is.EqualTo(30), "streak capping recolours, never drops");
            Assert.That(guarded.Any(IsKat), Is.True, "some dons were flipped to kats");
        });
    }
}
