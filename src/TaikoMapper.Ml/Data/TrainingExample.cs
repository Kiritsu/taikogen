using TaikoMapper.Ml.Representation;

namespace TaikoMapper.Ml.Data;

/// <summary>
/// One supervised training example: a tokenized map plus its per-tick conditioning features and
/// the metadata needed to reproduce/condition it. Built from a <c>(.osu, audio)</c> pair by
/// <see cref="CorpusBuilder"/>; serialized by <see cref="DatasetWriter"/>.
/// </summary>
/// <param name="AuthorId">Mapper (style) id — the embedding key.</param>
/// <param name="TicksPerBeat">Grid resolution.</param>
/// <param name="Bpm">Map BPM (single segment).</param>
/// <param name="OffsetMs">Map offset (segment start).</param>
/// <param name="Stars">Official star rating of the source map.</param>
/// <param name="FeatureNames">Column names for <paramref name="Features"/>.</param>
/// <param name="Tokens">Per-tick tokens, length T.</param>
/// <param name="Features">T×|FeatureNames| conditioning matrix, aligned to <paramref name="Tokens"/>.</param>
public sealed record TrainingExample(
    string AuthorId,
    int TicksPerBeat,
    double Bpm,
    double OffsetMs,
    double Stars,
    IReadOnlyList<string> FeatureNames,
    IReadOnlyList<TaikoToken> Tokens,
    float[][] Features)
{
    public int Length => Tokens.Count;
}
