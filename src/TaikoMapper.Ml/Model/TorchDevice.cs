using static TorchSharp.torch;

namespace TaikoMapper.Ml.Model;

/// <summary>
/// Resolves a device spec (<c>auto</c> | <c>cpu</c> | <c>cuda</c>) to a torch <see cref="Device"/>.
/// <c>auto</c> picks CUDA when a CUDA runtime is available, else CPU. AMD on Linux uses ROCm, which
/// PyTorch/TorchSharp expose as the <b>CUDA</b> device type — so <c>cuda</c> covers it too (with a
/// ROCm-built libtorch). AMD/Intel on Windows are handled outside TorchSharp via ONNX Runtime +
/// DirectML, so DirectML is not a device here.
/// </summary>
public static class TorchDevice
{
    /// <summary>Resolves <paramref name="spec"/> to a device, and reports a human-readable label.</summary>
    public static Device Resolve(string? spec, out string label)
    {
        var s = (spec ?? "auto").Trim().ToLowerInvariant();
        switch (s)
        {
            case "cpu":
                label = "cpu";
                return CPU;

            case "cuda" or "gpu":
                if (!cuda.is_available())
                    throw new InvalidOperationException(
                        "A CUDA device was requested but no CUDA/ROCm runtime is available. Build with " +
                        "-p:TorchBackend=cuda (NVIDIA), or provide a ROCm-built libtorch on Linux/AMD.");
                label = DescribeCuda();
                return CUDA;

            case "auto" or "":
                if (cuda.is_available())
                {
                    label = DescribeCuda();
                    return CUDA;
                }
                label = "cpu";
                return CPU;

            default:
                throw new ArgumentException($"Unknown device '{spec}'. Use auto, cpu, or cuda.", nameof(spec));
        }
    }

    private static string DescribeCuda()
    {
        var count = cuda.device_count();
        return count > 1 ? $"cuda (×{count})" : "cuda";
    }
}
