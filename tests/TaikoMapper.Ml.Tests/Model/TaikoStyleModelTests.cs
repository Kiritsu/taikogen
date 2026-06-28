using NUnit.Framework;
using TaikoMapper.Ml.Model;
using static TorchSharp.torch;

namespace TaikoMapper.Ml.Tests.Model;

public class TaikoStyleModelTests
{
    [Test]
    public void Forward_returns_batch_time_vocab_logits()
    {
        var model = new TaikoStyleModel(featureCount: 7, numAuthors: 3);

        using var features = randn(2, 16, 7);
        using var authors = tensor(new long[] { 0, 2 });
        using var prev = randint(TaikoStyleModel.Vocab + 1, new long[] { 2, 16 }).to(ScalarType.Int64);
        using var logits = model.forward(features, authors, prev);

        Assert.That(logits.shape, Is.EqualTo(new long[] { 2, 16, TaikoStyleModel.Vocab }));
    }

    [Test]
    public void Stepping_one_tick_at_a_time_matches_the_full_sequence()
    {
        // Inference steps the GRU with a carried hidden state; that must equal running the whole
        // prefix at once (given the same previous tokens). Guards the autoregressive decode loop.
        manual_seed(1);
        var model = new TaikoStyleModel(featureCount: 7, numAuthors: 2, dModel: 16, dHidden: 16);
        model.eval();

        const int t = 6, f = 7;
        using var _ = no_grad();
        using var features = randn(1, t, f);
        using var author = tensor(new long[] { 1 });
        using var prev = randint(TaikoStyleModel.Vocab + 1, new long[] { 1, t }).to(ScalarType.Int64);

        using var full = model.forward(features, author, prev); // [1,t,V]

        Tensor? h = null;
        for (var i = 0; i < t; i++)
        {
            using var fi = features.narrow(1, i, 1);
            using var pi = prev.narrow(1, i, 1);
            var (stepLogits, hN) = model.RunSequence(fi, author, pi, h);
            using var expected = full.narrow(1, i, 1);

            Assert.That((stepLogits - expected).abs().max().item<float>(), Is.LessThan(1e-4f), $"tick {i} mismatch");

            stepLogits.Dispose();
            h?.Dispose();
            h = hN;
        }
        h?.Dispose();
    }

    [Test]
    public void Generic_style_is_the_centroid_and_runs_without_an_author()
    {
        manual_seed(2);
        var model = new TaikoStyleModel(featureCount: 7, numAuthors: 3, dModel: 16, dHidden: 16);
        model.eval();

        using var _ = no_grad();
        using var generic = model.GenericStyleVector();
        using var a0 = model.StyleVector(0);
        using var a1 = model.StyleVector(1);
        using var a2 = model.StyleVector(2);
        using var mean = (a0 + a1 + a2) / 3.0;

        using var features = randn(1, 5, 7);
        using var prev = randint(TaikoStyleModel.Vocab + 1, new long[] { 1, 5 }).to(ScalarType.Int64);
        var (logits, hN) = model.RunSequenceWithStyle(features, generic, prev, null);

        Assert.Multiple(() =>
        {
            Assert.That(generic.shape, Is.EqualTo(new long[] { 16 }), "style vector is [dModel]");
            Assert.That((generic - mean).abs().max().item<float>(), Is.LessThan(1e-5f), "generic = mean of author embeddings");
            Assert.That(logits.shape, Is.EqualTo(new long[] { 1, 5, TaikoStyleModel.Vocab }), "decodes without an author id");
        });

        logits.Dispose();
        hN.Dispose();
    }

    [Test]
    public void Training_step_reduces_loss_on_a_fixed_batch()
    {
        manual_seed(0);
        const int b = 4, w = 8, f = 7;

        using var features = randn(b, w, f);
        using var authors = tensor(new long[] { 0, 1, 0, 1 });
        using var targets = randint(TaikoStyleModel.Vocab, new long[] { b, w }).to(ScalarType.Int64);
        using var prev = randint(TaikoStyleModel.Vocab + 1, new long[] { b, w }).to(ScalarType.Int64);

        var model = new TaikoStyleModel(f, numAuthors: 2, dModel: 64, dHidden: 64);
        var optimizer = optim.Adam(model.parameters(), lr: 0.01);
        var loss = nn.CrossEntropyLoss();
        model.train();

        double first = double.NaN, last = double.NaN;
        for (var step = 0; step < 80; step++)
        {
            using IDisposable scope = NewDisposeScope();
            var logits = model.forward(features, authors, prev).reshape(b * w, TaikoStyleModel.Vocab);
            var l = loss.forward(logits, targets.reshape(b * w));
            optimizer.zero_grad();
            l.backward();
            optimizer.step();

            double v = l.item<float>();
            if (step == 0) first = v;
            last = v;
        }

        Assert.That(last, Is.LessThan(first * 0.5), "the model learns the fixed batch — the training step works");
    }
}
