using NUnit.Framework;
using TaikoMapper.Ml.Inference;

namespace TaikoMapper.Ml.Tests.Inference;

public class StyleTargetingTests
{
    [Test]
    public void Converges_to_the_conditioning_that_hits_the_target()
    {
        // A monotone model where star rating = 12 × conditioning ⇒ 6★ at c = 0.5.
        var (conditioning, sr, _) = StyleGenerator.SearchConditioning(
            c => 12.0 * c, targetStars: 6.0, tolerance: 0.1, maxIterations: 12);

        Assert.Multiple(() =>
        {
            Assert.That(sr, Is.EqualTo(6.0).Within(0.1));
            Assert.That(conditioning, Is.EqualTo(0.5).Within(0.02));
        });
    }

    [Test]
    public void Finds_a_lower_conditioning_when_the_model_overshoots()
    {
        // The reported failure: requesting 6★ from a model that overshoots. Here c = 0.6 ⇒ 12★,
        // so to actually land 6★ the search must pick a much lower conditioning (~0.3).
        var (conditioning, sr, _) = StyleGenerator.SearchConditioning(
            c => 20.0 * c, targetStars: 6.0, tolerance: 0.1, maxIterations: 14);

        Assert.Multiple(() =>
        {
            Assert.That(sr, Is.EqualTo(6.0).Within(0.2));
            Assert.That(conditioning, Is.LessThan(0.45), "must condition well below the nominal 0.6");
        });
    }

    [Test]
    public void Returns_the_closest_candidate_when_the_target_is_unreachable()
    {
        // Model maxes out at 5★; asking for 8★ should return the best (highest) it can do, not throw.
        var (conditioning, sr, _) = StyleGenerator.SearchConditioning(
            c => 5.0 * c, targetStars: 8.0, tolerance: 0.1, maxIterations: 8);

        Assert.Multiple(() =>
        {
            Assert.That(sr, Is.EqualTo(5.0).Within(0.1), "best effort is the top of the achievable range");
            Assert.That(conditioning, Is.GreaterThan(0.9));
        });
    }
}
