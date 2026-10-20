using System;
using System.IO;
using System.IO.Compression;

namespace Realm.Gen.Terrain;

/// <summary>Minimal PNG writer (8-bit RGB, no interlace) for preview images; no external dependency.</summary>
/// <remarks>Determinism: same pixels give the same bytes. Thread-safety: stateless.</remarks>
public static class PngEncoder
{
    private const int BytesPerPixel = 3;
    private const byte ColourTypeRgb = 2;
    private const byte BitDepth = 8;
    private const uint CrcPolynomial = 0xEDB88320u;

    private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <summary>Encodes an RGB image.</summary>
    /// <param name="width">Width in pixels; positive.</param>
    /// <param name="height">Height in pixels; positive.</param>
    /// <param name="rgb">Pixels row by row, 3 bytes (red, green, blue) each; length must be width x height x 3.</param>
    /// <returns>The PNG file bytes.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is not positive.</exception>
    /// <exception cref="ArgumentException">The pixel array has the wrong length.</exception>
    public static byte[] Encode(int width, int height, ReadOnlySpan<byte> rgb)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (rgb.Length != checked(width * height * BytesPerPixel))
        {
            throw new ArgumentException($"Expected {width * height * BytesPerPixel} bytes but got {rgb.Length}.", nameof(rgb));
        }

        using var output = new MemoryStream();
        output.Write(Signature);

        Span<byte> header = stackalloc byte[13];
        WriteBigEndian(header[..4], (uint)width);
        WriteBigEndian(header.Slice(4, 4), (uint)height);
        header[8] = BitDepth;
        header[9] = ColourTypeRgb;
        header[10] = 0; // compression method
        header[11] = 0; // filter method
        header[12] = 0; // no interlace
        WriteChunk(output, "IHDR", header);

        using var raw = new MemoryStream();
        using (var zlib = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
        {
            int stride = width * BytesPerPixel;
            for (int row = 0; row < height; row++)
            {
                zlib.WriteByte(0); // filter type: none
                zlib.Write(rgb.Slice(row * stride, stride));
            }
        }

        WriteChunk(output, "IDAT", raw.ToArray());
        WriteChunk(output, "IEND", ReadOnlySpan<byte>.Empty);
        return output.ToArray();
    }

    /// <summary>CRC-32 as used by PNG chunks (exposed for tests).</summary>
    /// <param name="data">The bytes.</param>
    /// <returns>The checksum.</returns>
    public static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (byte b in data)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFFu;
    }

    private static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        WriteBigEndian(length, (uint)data.Length);
        output.Write(length);

        var body = new byte[4 + data.Length];
        for (int i = 0; i < 4; i++)
        {
            body[i] = (byte)type[i];
        }

        data.CopyTo(body.AsSpan(4));
        output.Write(body);

        Span<byte> crc = stackalloc byte[4];
        WriteBigEndian(crc, Crc32(body));
        output.Write(crc);
    }

    private static void WriteBigEndian(Span<byte> target, uint value)
    {
        target[0] = (byte)(value >> 24);
        target[1] = (byte)(value >> 16);
        target[2] = (byte)(value >> 8);
        target[3] = (byte)value;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < table.Length; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? CrcPolynomial ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
