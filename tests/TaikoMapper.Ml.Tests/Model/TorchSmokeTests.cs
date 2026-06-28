using NUnit.Framework;
using TorchSharp;

namespace TaikoMapper.Ml.Tests.Model;

/// <summary>Confirms the native libtorch (CPU) backend restores and runs in this environment.</summary>
public class TorchSmokeTests
{
    [Test]
    public void Libtorch_loads_and_runs_a_tensor_op()
    {
        using var a = torch.tensor(new float[] { 1, 2, 3 });
        using var b = torch.tensor(new float[] { 4, 5, 6 });
        using var c = a + b;

        Assert.That(c.data<float>().ToArray(), Is.EqualTo(new float[] { 5, 7, 9 }));
    }
}
