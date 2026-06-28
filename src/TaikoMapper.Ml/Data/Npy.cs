using System.Text;

namespace TaikoMapper.Ml.Data;

/// <summary>
/// Minimal writer for the NumPy <c>.npy</c> format (format version 1.0) — a self-describing, inspectable
/// array file that both NumPy and a C#/TorchSharp loader can read. Used to serialize the
/// training dataset (token sequences as uint8, feature matrices as float32).
/// </summary>
/// <remarks>Format reference: a 6-byte magic, version, a 2-byte little-endian header length,
/// an ASCII header dict (64-byte aligned, newline-terminated), then raw little-endian data.</remarks>
public static class Npy
{
    /// <summary>Saves a 1-D unsigned-byte array (e.g. token ids).</summary>
    public static void SaveUInt8(string path, ReadOnlySpan<byte> data)
    {
        using var fs = File.Create(path);
        WriteHeader(fs, "|u1", [data.Length]);
        fs.Write(data);
    }

    /// <summary>Saves a 2-D row-major float32 matrix with <paramref name="cols"/> columns.</summary>
    public static void SaveFloat32(string path, IReadOnlyList<float[]> rows, int cols)
    {
        ArgumentNullException.ThrowIfNull(rows);

        using var fs = File.Create(path);
        WriteHeader(fs, "<f4", [rows.Count, cols]);

        var bytes = new byte[cols * sizeof(float)];
        foreach (var row in rows)
        {
            if (row.Length != cols)
                throw new ArgumentException($"Row length {row.Length} != declared cols {cols}.", nameof(rows));
            Buffer.BlockCopy(row, 0, bytes, 0, bytes.Length);
            fs.Write(bytes);
        }
    }

    private static void WriteHeader(Stream stream, string descr, int[] shape)
    {
        var shapeStr = shape.Length == 1 ? $"({shape[0]},)" : "(" + string.Join(", ", shape) + ")";
        var dict = $"{{'descr': '{descr}', 'fortran_order': False, 'shape': {shapeStr}, }}";

        const int prefix = 6 + 2 + 2; // magic + version + 2-byte length field
        var unpadded = prefix + dict.Length + 1; // +1 for the trailing '\n'
        var pad = (64 - unpadded % 64) % 64;
        dict += new string(' ', pad) + "\n";

        stream.WriteByte(0x93);
        stream.Write("NUMPY"u8);
        stream.WriteByte(1); // major
        stream.WriteByte(0); // minor
        var headerLen = (ushort)dict.Length;
        stream.WriteByte((byte)(headerLen & 0xFF));
        stream.WriteByte((byte)(headerLen >> 8));
        stream.Write(Encoding.ASCII.GetBytes(dict));
    }
}
