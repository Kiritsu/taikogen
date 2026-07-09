using TaikoMapper.Ml.Data;
using TaikoMapper.Ml.Representation;
using static TorchSharp.torch;

namespace TaikoMapper.Ml.Model;

/// <summary>
/// Trains a <see cref="TaikoStyleModel"/> on a <see cref="TaikoDataset"/> with class-weighted
/// cross-entropy (the empty <see cref="TaikoToken.None"/> dominates, so it is down-weighted) and
/// Adam, on the CPU backend. Saves a checkpoint when done. Deterministic given the seed.
/// </summary>
public sealed class StyleTrainer
{
    public sealed record Options(int Epochs = 20, int BatchSize = 8, double LearningRate = 1e-3, int Seed = 0);

    /// <summary>
    /// Trains <paramref name="model"/> in place. <paramref name="onEpochEnd"/> is invoked after each
    /// epoch (e.g. to checkpoint), so long CPU runs can be stopped early without losing progress.
    /// </summary>
    public void Train(TaikoStyleModel model, TaikoDataset dataset, Options options, Action<string>? log = null, Action<int>? onEpochEnd = null, Device? device = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(options);

        manual_seed(options.Seed); // seeds CPU and CUDA generators
        var rng = new Random(options.Seed);

        var dev = device ?? CPU;
        model.MoveTo(dev);

        using var classWeights = ClassWeights(dataset, dev);
        var loss = nn.CrossEntropyLoss(weight: classWeights);
        var optimizer = optim.Adam(model.parameters(), lr: options.LearningRate);

        var f = dataset.FeatureCount;
        var batch = options.BatchSize;
        model.train();

        var totalBatches = (dataset.Windows.Count + batch - 1) / batch;
        var progressEvery = Math.Max(1, totalBatches / 10); // ~10 progress lines per epoch
        var clock = System.Diagnostics.Stopwatch.StartNew();

        for (var epoch = 1; epoch <= options.Epochs; epoch++)
        {
            var order = Enumerable.Range(0, dataset.Windows.Count).OrderBy(_ => rng.Next()).ToArray();
            var epochLoss = 0.0;
            long correct = 0, placed = 0;
            var batches = 0;

            for (var b = 0; b < order.Length; b += batch)
            {
                using IDisposable scope = NewDisposeScope();
                var n = Math.Min(batch, order.Length - b);
                var w = dataset.Windows[order[b]].Length;

                var (features, authors, targets, prevTokens) = Stack(dataset, order, b, n, w, f, dev);

                var logits = model.forward(features, authors, prevTokens); // [n,w,V]
                var flatLogits = logits.reshape(n * w, TaikoStyleModel.Vocab);
                var flatTargets = targets.reshape(n * w);

                var batchLoss = loss.forward(flatLogits, flatTargets);
                optimizer.zero_grad();
                batchLoss.backward();
                optimizer.step();

                epochLoss += batchLoss.item<float>();
                batches++;
                Accumulate(flatLogits, flatTargets, ref correct, ref placed);

                if (batches % progressEvery == 0 && batches < totalBatches)
                    log?.Invoke($"  epoch {epoch}/{options.Epochs}  batch {batches}/{totalBatches}  loss {epochLoss / batches:F4}");
            }

            var placedAcc = placed == 0 ? 0.0 : (double)correct / placed;
            log?.Invoke($"epoch {epoch,3}/{options.Epochs}  loss {epochLoss / batches:F4}  placed-acc {placedAcc:P1}  ({clock.Elapsed.TotalSeconds:F0}s elapsed)");
            onEpochEnd?.Invoke(epoch);
        }
    }

    /// <summary>
    /// Stacks <paramref name="n"/> windows into batch tensors, including the teacher-forcing
    /// "previous token" input: <see cref="TaikoStyleModel.Bos"/> at each window's first tick, then
    /// the target tokens shifted right by one.
    /// </summary>
    private static (Tensor features, Tensor authors, Tensor targets, Tensor prevTokens) Stack(
        TaikoDataset dataset, int[] order, int b, int n, int w, int f, Device dev)
    {
        var featBuf = new float[n * w * f];
        var tokBuf = new long[n * w];
        var prevBuf = new long[n * w];
        var authBuf = new long[n];

        for (var i = 0; i < n; i++)
        {
            var win = dataset.Windows[order[b + i]];
            Array.Copy(win.Features, 0, featBuf, i * w * f, w * f);
            for (var t = 0; t < w; t++)
            {
                tokBuf[i * w + t] = win.Tokens[t];
                prevBuf[i * w + t] = t == 0 ? TaikoStyleModel.Bos : win.Tokens[t - 1];
            }
            authBuf[i] = win.AuthorId;
        }

        var features = tensor(featBuf).reshape(n, w, f).to(dev);
        var authors = tensor(authBuf).to(dev);
        var targets = tensor(tokBuf).reshape(n, w).to(dev);
        var prevTokens = tensor(prevBuf).reshape(n, w).to(dev);
        return (features, authors, targets, prevTokens);
    }

    /// <summary>
    /// Down-weights ONLY the dominant empty token; every placed token (don/kat/large) keeps weight 1,
    /// so the model reproduces their <i>natural</i> relative rates — finishers stay rare. (Inverse-
    /// frequency over all classes instead up-weights the rarest token, i.e. finishers, into a flood.)
    /// The empty-token weight balances placed-vs-empty so the map isn't all rests nor a wall of notes.
    /// </summary>
    private static Tensor ClassWeights(TaikoDataset dataset, Device dev)
    {
        var counts = new double[TaikoStyleModel.Vocab];
        foreach (var window in dataset.Windows)
            foreach (var token in window.Tokens)
                counts[token]++;

        var none = counts[(int)TaikoToken.None];
        var placed = counts.Sum() - none;

        var weights = new float[TaikoStyleModel.Vocab];
        Array.Fill(weights, 1.0f);
        weights[(int)TaikoToken.None] = (float)Math.Clamp(placed / Math.Max(1.0, none), 0.2, 1.0);

        return tensor(weights).to(dev);
    }

    /// <summary>Counts correct predictions on placed (non-None) target ticks, for a quick quality read.</summary>
    private static void Accumulate(Tensor flatLogits, Tensor flatTargets, ref long correct, ref long placed)
    {
        using var _ = no_grad();
        using var pred = flatLogits.argmax(dim: 1);
        using var placedMask = flatTargets.ne(0); // != None
        using var hit = pred.eq(flatTargets).logical_and(placedMask);
        correct += hit.sum().item<long>();
        placed += placedMask.sum().item<long>();
    }
}
