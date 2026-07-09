using System.Text.Json;
using static TorchSharp.torch;

namespace TaikoMapper.Ml.Model;

/// <summary>
/// Everything inference needs about a trained model besides its weights: the dimensions to
/// reconstruct it, the grid resolution it was trained at, the feature layout, and the
/// author → embedding-id map (so a generate command can resolve an author by name).
/// </summary>
public sealed record ModelConfig(
    int FeatureCount,
    int TicksPerBeat,
    int DModel,
    int DHidden,
    int Layers,
    Dictionary<string, int> Authors,
    string[] FeatureNames)
{
    public int AuthorCount => Authors.Count;
}

/// <summary>Saves/loads a <see cref="TaikoStyleModel"/> as weights (<c>.dat</c>) plus a <c>.json</c> config sidecar.</summary>
public static class StyleModelIo
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static void Save(TaikoStyleModel model, ModelConfig config, string path)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(config);
        model.save(path);
        File.WriteAllText(path + ".json", JsonSerializer.Serialize(config, JsonOptions));
    }

    public static (TaikoStyleModel model, ModelConfig config) Load(string path, Device? device = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var config = JsonSerializer.Deserialize<ModelConfig>(File.ReadAllText(path + ".json"))
                     ?? throw new InvalidDataException($"Missing/invalid model config: {path}.json");

        var model = new TaikoStyleModel(config.FeatureCount, config.AuthorCount, config.DModel, config.DHidden, config.Layers);
        model.load(path); // weights load onto CPU
        if (device is not null)
            model.MoveTo(device);
        return (model, config);
    }
}
