using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaikoMapper.Ml.Data;

/// <summary>
/// Serializes a <see cref="TrainingExample"/> to an inspectable on-disk form: one folder per
/// map containing <c>tokens.npy</c> (uint8 [T]), <c>features.npy</c> (float32 [T, F]) and
/// <c>meta.json</c>. Language-neutral so a TorchSharp (or NumPy) loader can read it directly.
/// </summary>
public static class DatasetWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>Writes <paramref name="example"/> into <paramref name="outputDir"/>/<paramref name="id"/>/. Returns the metadata written.</summary>
    public static ExampleMeta Write(TrainingExample example, string outputDir, string id)
    {
        ArgumentNullException.ThrowIfNull(example);
        ArgumentException.ThrowIfNullOrEmpty(outputDir);
        ArgumentException.ThrowIfNullOrEmpty(id);

        var dir = Path.Combine(outputDir, id);
        Directory.CreateDirectory(dir);

        var tokenBytes = new byte[example.Tokens.Count];
        for (var i = 0; i < tokenBytes.Length; i++)
            tokenBytes[i] = (byte)example.Tokens[i];
        Npy.SaveUInt8(Path.Combine(dir, "tokens.npy"), tokenBytes);

        Npy.SaveFloat32(Path.Combine(dir, "features.npy"), example.Features, example.FeatureNames.Count);

        var meta = new ExampleMeta(
            id,
            example.AuthorId,
            example.TicksPerBeat,
            example.Bpm,
            example.OffsetMs,
            example.Stars,
            example.Length,
            example.FeatureNames.Count,
            [.. example.FeatureNames]);
        File.WriteAllText(Path.Combine(dir, "meta.json"), JsonSerializer.Serialize(meta, JsonOptions));
        return meta;
    }

    /// <summary>Writes the dataset-level <c>manifest.json</c> (all examples) and <c>authors.json</c> (author → id).</summary>
    public static void WriteManifest(string outputDir, IReadOnlyList<ExampleMeta> examples)
    {
        ArgumentException.ThrowIfNullOrEmpty(outputDir);
        ArgumentNullException.ThrowIfNull(examples);
        Directory.CreateDirectory(outputDir);

        var authorIds = examples
            .Select(e => e.AuthorId)
            .Distinct()
            .OrderBy(a => a, StringComparer.Ordinal)
            .Select((author, id) => (author, id))
            .ToDictionary(x => x.author, x => x.id);

        File.WriteAllText(Path.Combine(outputDir, "authors.json"), JsonSerializer.Serialize(authorIds, JsonOptions));
        File.WriteAllText(Path.Combine(outputDir, "manifest.json"), JsonSerializer.Serialize(examples, JsonOptions));
    }
}

/// <summary>JSON metadata sidecar for one serialized example.</summary>
public sealed record ExampleMeta(
    string Id,
    string AuthorId,
    int TicksPerBeat,
    double Bpm,
    double OffsetMs,
    double Stars,
    int Length,
    int FeatureCount,
    IReadOnlyList<string> FeatureNames);
