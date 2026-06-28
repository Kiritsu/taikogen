using System.Text.Json;
using TaikoMapper.Ml.Representation;

namespace TaikoMapper.Ml.Data;

/// <summary>One fixed-length training window: features [<see cref="Length"/>×F], target tokens, author id.</summary>
public sealed class TrainingWindow
{
    public required long AuthorId { get; init; }
    public required int Length { get; init; }
    public required float[] Features { get; init; } // row-major [Length, F]
    public required byte[] Tokens { get; init; }    // [Length]
}

/// <summary>
/// Loads a dataset written by <see cref="DatasetWriter"/> and slices each map into fixed-length
/// windows for training (long sequences are chunked; the trailing partial window is dropped).
/// </summary>
public sealed class TaikoDataset
{
    public int FeatureCount { get; }
    public int TicksPerBeat { get; }
    public int AuthorCount { get; }
    public IReadOnlyDictionary<string, int> Authors { get; }
    public IReadOnlyList<TrainingWindow> Windows { get; }

    private TaikoDataset(int featureCount, int ticksPerBeat, IReadOnlyDictionary<string, int> authors, List<TrainingWindow> windows)
    {
        FeatureCount = featureCount;
        TicksPerBeat = ticksPerBeat;
        Authors = authors;
        AuthorCount = authors.Count;
        Windows = windows;
    }

    public static TaikoDataset Load(string datasetDir, int window = 512, int stride = 384, Action<string>? log = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(datasetDir);
        if (window <= 0 || stride <= 0) throw new ArgumentOutOfRangeException(nameof(window));

        var authors = JsonSerializer.Deserialize<Dictionary<string, int>>(
            File.ReadAllText(Path.Combine(datasetDir, "authors.json"))) ?? throw new InvalidDataException("authors.json missing/empty.");
        var manifest = JsonSerializer.Deserialize<List<ExampleMeta>>(
            File.ReadAllText(Path.Combine(datasetDir, "manifest.json"))) ?? throw new InvalidDataException("manifest.json missing/empty.");

        var featureCount = 0;
        var ticksPerBeat = manifest.Count > 0 ? manifest[0].TicksPerBeat : MapTokenizer.DefaultTicksPerBeat;
        var windows = new List<TrainingWindow>();

        for (var m = 0; m < manifest.Count; m++)
        {
            var meta = manifest[m];
            var dir = Path.Combine(datasetDir, meta.Id);
            var tokens = NpyReader.ReadUInt8(Path.Combine(dir, "tokens.npy"));
            var (features, rows, cols) = NpyReader.ReadFloat32Matrix(Path.Combine(dir, "features.npy"));
            featureCount = cols;
            var author = authors[meta.AuthorId];

            for (var start = 0; start + window <= rows; start += stride)
            {
                var wf = new float[window * cols];
                Array.Copy(features, start * cols, wf, 0, wf.Length);
                var wt = new byte[window];
                Array.Copy(tokens, start, wt, 0, window);

                windows.Add(new TrainingWindow { AuthorId = author, Length = window, Features = wf, Tokens = wt });
            }

            if ((m + 1) % 25 == 0 || m + 1 == manifest.Count)
                log?.Invoke($"  loaded {m + 1}/{manifest.Count} maps ({windows.Count} windows)");
        }

        if (windows.Count == 0)
            throw new InvalidDataException("Dataset produced no windows (maps shorter than the window?).");

        return new TaikoDataset(featureCount, ticksPerBeat, authors, windows);
    }
}
