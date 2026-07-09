using NUnit.Framework;
using TaikoMapper.Ml.Model;
using TorchSharp;
using static TorchSharp.torch;

namespace TaikoMapper.Ml.Tests.Model;

public class TorchDeviceTests
{
    [Test]
    public void Resolve_cpu_and_auto_without_a_gpu_give_cpu()
    {
        var cpu = TorchDevice.Resolve("cpu", out var cpuLabel);
        var auto = TorchDevice.Resolve(null, out var autoLabel);

        Assert.Multiple(() =>
        {
            Assert.That(cpu.type, Is.EqualTo(DeviceType.CPU));
            Assert.That(cpuLabel, Is.EqualTo("cpu"));
            if (!cuda.is_available()) // CI has no GPU, so auto falls back to CPU
            {
                Assert.That(auto.type, Is.EqualTo(DeviceType.CPU));
                Assert.That(autoLabel, Is.EqualTo("cpu"));
            }
        });
    }

    [Test]
    public void Resolve_rejects_an_unknown_device() =>
        Assert.Throws<ArgumentException>(() => TorchDevice.Resolve("banana", out _));

    [Test]
    public void Resolve_cuda_without_a_runtime_throws()
    {
        if (cuda.is_available())
            Assert.Ignore("CUDA is available here; the not-available path can't be exercised.");
        Assert.Throws<InvalidOperationException>(() => TorchDevice.Resolve("cuda", out _));
    }
}
