using System.Text;
using NUnit.Framework;
using TaikoMapper.Ml.Data;

namespace TaikoMapper.Ml.Tests.Data;

public class NpyTests
{
    private static string TempPath() => Path.Combine(Path.GetTempPath(), $"npy_{Guid.NewGuid():N}.npy");

    [Test]
    public void Float32_matrix_writes_a_valid_npy()
    {
        var rows = new[] { new[] { 1f, 2f, 3f }, new[] { 4f, 5f, 6f } };
        var path = TempPath();

        Npy.SaveFloat32(path, rows, cols: 3);
        var bytes = File.ReadAllBytes(path);

        var headerLen = bytes[8] | (bytes[9] << 8);
        var header = Encoding.ASCII.GetString(bytes, 10, headerLen);
        var dataOffset = 10 + headerLen;

        Assert.Multiple(() =>
        {
            Assert.That(bytes[0], Is.EqualTo(0x93));
            Assert.That(Encoding.ASCII.GetString(bytes, 1, 5), Is.EqualTo("NUMPY"));
            Assert.That((10 + headerLen) % 64, Is.Zero, "header is 64-byte aligned");
            Assert.That(header, Does.Contain("'<f4'").And.Contain("(2, 3)"));
            Assert.That(bytes.Length - dataOffset, Is.EqualTo(2 * 3 * sizeof(float)));
            Assert.That(BitConverter.ToSingle(bytes, dataOffset + 4 * sizeof(float)), Is.EqualTo(5f), "row-major element [1,1] = 5");
        });

        File.Delete(path);
    }

    [Test]
    public void UInt8_vector_writes_a_valid_npy()
    {
        byte[] data = [0, 1, 2, 4, 3];
        var path = TempPath();

        Npy.SaveUInt8(path, data);
        var bytes = File.ReadAllBytes(path);

        var headerLen = bytes[8] | (bytes[9] << 8);
        var header = Encoding.ASCII.GetString(bytes, 10, headerLen);
        var dataOffset = 10 + headerLen;

        Assert.Multiple(() =>
        {
            Assert.That(header, Does.Contain("'|u1'").And.Contain("(5,)"));
            Assert.That(bytes[dataOffset..], Is.EqualTo(data));
        });

        File.Delete(path);
    }
}
