using System.Text;

namespace TaikoMapper.Ml.Data;

/// <summary>Reads back the <c>.npy</c> arrays written by <see cref="Npy"/> (uint8 vectors, float32 matrices).</summary>
public static class NpyReader
{
    /// <summary>Reads a 1-D <c>|u1</c> array.</summary>
    public static byte[] ReadUInt8(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var (descr, _, offset) = ReadHeader(bytes);
        if (descr != "|u1")
            throw new InvalidDataException($"Expected |u1, got {descr} in {path}.");
        return bytes[offset..];
    }

    /// <summary>Reads a 2-D row-major <c>&lt;f4</c> matrix as a flat array plus its shape.</summary>
    public static (float[] data, int rows, int cols) ReadFloat32Matrix(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var (descr, shape, offset) = ReadHeader(bytes);
        if (descr != "<f4")
            throw new InvalidDataException($"Expected <f4, got {descr} in {path}.");
        if (shape.Length != 2)
            throw new InvalidDataException($"Expected a 2-D matrix, got rank {shape.Length} in {path}.");

        var count = shape[0] * shape[1];
        var data = new float[count];
        Buffer.BlockCopy(bytes, offset, data, 0, count * sizeof(float));
        return (data, shape[0], shape[1]);
    }

    private static (string descr, int[] shape, int dataOffset) ReadHeader(byte[] bytes)
    {
        if (bytes.Length < 10 || bytes[0] != 0x93)
            throw new InvalidDataException("Not a .npy file.");

        var headerLen = bytes[8] | (bytes[9] << 8);
        var header = Encoding.ASCII.GetString(bytes, 10, headerLen);
        return (Quoted(header, "'descr':"), ParseShape(header), 10 + headerLen);
    }

    private static string Quoted(string header, string key)
    {
        var k = header.IndexOf(key, StringComparison.Ordinal);
        var open = header.IndexOf('\'', k + key.Length);
        var close = header.IndexOf('\'', open + 1);
        return header[(open + 1)..close];
    }

    private static int[] ParseShape(string header)
    {
        var k = header.IndexOf("'shape':", StringComparison.Ordinal);
        var open = header.IndexOf('(', k);
        var close = header.IndexOf(')', open);
        return header[(open + 1)..close]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(int.Parse)
            .ToArray();
    }
}
