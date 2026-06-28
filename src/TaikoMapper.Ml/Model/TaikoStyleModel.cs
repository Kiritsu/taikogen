using TaikoMapper.Ml.Representation;
using TorchSharp.Modules;
using static TorchSharp.torch;
using static TorchSharp.torch.nn;

namespace TaikoMapper.Ml.Model;

/// <summary>
/// The sequence model. Autoregressive over tokens: each tick's distribution is
/// predicted from the per-tick conditioning features, the author embedding (style), AND an
/// embedding of the <i>previously emitted</i> token — so the model can learn colour-pattern
/// dependencies (don/kat runs, inversions) that audio alone doesn't determine. A GRU gives
/// left-to-right context; a linear head predicts the 5-token vocabulary. CPU libtorch backend.
/// </summary>
public sealed class TaikoStyleModel : Module<Tensor, Tensor, Tensor, Tensor>
{
    /// <summary>Vocabulary size = number of <see cref="TaikoToken"/> values.</summary>
    public const int Vocab = 5;

    /// <summary>"Previous token" id fed at the first tick (start-of-sequence).</summary>
    public const long Bos = Vocab;

    private readonly Linear _featProj;
    private readonly Embedding _authorEmb;
    private readonly Embedding _tokenEmb;
    private readonly GRU _gru;
    private readonly Linear _head;

    public TaikoStyleModel(int featureCount, int numAuthors, int dModel = 128, int dHidden = 128, int layers = 1)
        : base(nameof(TaikoStyleModel))
    {
        _featProj = Linear(featureCount, dModel);
        _authorEmb = Embedding(numAuthors, dModel);
        _tokenEmb = Embedding(Vocab + 1, dModel); // +1 for BOS
        _gru = GRU(dModel, dHidden, layers, batchFirst: true);
        _head = Linear(dHidden, Vocab);
        RegisterComponents();
    }

    /// <param name="features">[B, T, F] per-tick features.</param>
    /// <param name="author">[B] int64 author ids.</param>
    /// <param name="prevTokens">[B, T] int64 previous tokens (BOS at t=0, ground truth shifted right in training).</param>
    /// <returns>[B, T, Vocab] token logits.</returns>
    public override Tensor forward(Tensor features, Tensor author, Tensor prevTokens) =>
        RunSequence(features, author, prevTokens, null).logits;

    /// <summary>
    /// Runs the sequence and returns the final GRU hidden state, so inference can step one tick at a
    /// time (pass <paramref name="h0"/> back in) instead of re-running the whole prefix each step.
    /// </summary>
    public (Tensor logits, Tensor hN) RunSequence(Tensor features, Tensor author, Tensor prevTokens, Tensor? h0) =>
        RunWithStyle(features, _authorEmb.forward(author).unsqueeze(1), prevTokens, h0); // [B,1,d] broadcast over T

    /// <summary>
    /// Like <see cref="RunSequence"/> but conditions on a precomputed style vector ([dModel], broadcast
    /// over batch and time) instead of an author id — used for author-agnostic ("no author") generation.
    /// </summary>
    public (Tensor logits, Tensor hN) RunSequenceWithStyle(Tensor features, Tensor styleVector, Tensor prevTokens, Tensor? h0) =>
        RunWithStyle(features, styleVector.reshape(1, 1, -1), prevTokens, h0);

    private (Tensor logits, Tensor hN) RunWithStyle(Tensor features, Tensor styleContribution, Tensor prevTokens, Tensor? h0)
    {
        var x = _featProj.forward(features) + styleContribution + _tokenEmb.forward(prevTokens); // [B,T,d]
        var (output, hN) = h0 is null ? _gru.forward(x) : _gru.forward(x, h0);
        return (_head.forward(output), hN);                                                     // [B,T,Vocab]
    }

    /// <summary>The learned style embedding for one author id ([dModel]).</summary>
    public Tensor StyleVector(long authorId)
    {
        using var id = tensor([authorId]);
        return _authorEmb.forward(id).reshape(-1);
    }

    /// <summary>The mean of all author embeddings ([dModel]) — a generic, author-agnostic style (the centroid).</summary>
    public Tensor GenericStyleVector() => _authorEmb.weight!.mean([0]).reshape(-1);
}
