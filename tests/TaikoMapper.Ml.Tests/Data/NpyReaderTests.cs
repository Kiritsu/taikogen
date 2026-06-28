using NUnit.Framework;
using TaikoMapper.Ml.Data;

namespace TaikoMapper.Ml.Tests.Data;

public class NpyReaderTests
{
    private static string Temp() => Path.Combine(Path.GetTempPath(), $"npyio_{Guid.NewGuid():N}.npy");

    [Test]
    public void UInt8_round_trips_through_writer_and_reader()
    {
        byte[] data = [0, 1, 2, 4, 3, 2, 0];
        var path = Temp();

        Npy.SaveUInt8(path, data);
        var read = NpyReader.ReadUInt8(path);

        Assert.That(read, Is.EqualTo(data));
        File.Delete(path);
    }

    [Test]
    public void Float32_matrix_round_trips_with_shape()
    {
        var rows = new[] { new[] { 1f, 2f, 3f }, new[] { 4f, 5f, 6f }, new[] { 7f, 8f, 9f } };
        var path = Temp();

        Npy.SaveFloat32(path, rows, cols: 3);
        var (data, r, c) = NpyReader.ReadFloat32Matrix(path);

        Assert.Multiple(() =>
        {
            Assert.That((r, c), Is.EqualTo((3, 3)));
            Assert.That(data, Is.EqualTo([1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f, 9f]));
        });
        File.Delete(path);
    }
}
