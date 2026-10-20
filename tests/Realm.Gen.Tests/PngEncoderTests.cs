using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Realm.Gen.Terrain;
using Xunit;

namespace Realm.Gen.Tests;

public class PngEncoderTests
{
    private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    private sealed record Chunk(string Type, byte[] Data, uint Crc, uint ComputedCrc);

    private static List<Chunk> ReadChunks(byte[] png)
    {
        Assert.True(png.AsSpan(0, 8).SequenceEqual(Signature), "PNG signature");
        var chunks = new List<Chunk>();
        int pos = 8;
        while (pos < png.Length)
        {
            int length = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(pos, 4));
            string type = Encoding.ASCII.GetString(png, pos + 4, 4);
            byte[] data = png.AsSpan(pos + 8, length).ToArray();
            uint crc = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(pos + 8 + length, 4));
            uint computed = PngEncoder.Crc32(png.AsSpan(pos + 4, 4 + length));
            chunks.Add(new Chunk(type, data, crc, computed));
            pos += 12 + length;
        }

        return chunks;
    }

    [Fact]
    public void Crc32_MatchesTheStandardCheckValue()
    {
        // The well-known CRC-32 check value for the ASCII bytes "123456789".
        Assert.Equal(0xCBF43926u, PngEncoder.Crc32(Encoding.ASCII.GetBytes("123456789")));
        Assert.Equal(0u, PngEncoder.Crc32(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void Encode_ProducesAWellFormedPng()
    {
        byte[] rgb = { 255, 0, 0, 0, 255, 0, 0, 0, 255, 10, 20, 30 };
        byte[] png = PngEncoder.Encode(2, 2, rgb);

        List<Chunk> chunks = ReadChunks(png);

        Assert.Equal(new[] { "IHDR", "IDAT", "IEND" }, chunks.ConvertAll(c => c.Type));
        Assert.All(chunks, c => Assert.Equal(c.Crc, c.ComputedCrc));
        Assert.Empty(chunks[2].Data);

        byte[] header = chunks[0].Data;
        Assert.Equal(13, header.Length);
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(0, 4)));
        Assert.Equal(2u, BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(4, 4)));
        Assert.Equal(8, header[8]);  // bit depth
        Assert.Equal(2, header[9]);  // colour type: RGB
        Assert.Equal(0, header[12]); // no interlace
    }

    [Fact]
    public void Encode_PixelDataRoundTripsThroughZlib()
    {
        byte[] rgb = { 255, 0, 0, 0, 255, 0, 0, 0, 255, 10, 20, 30 };
        byte[] png = PngEncoder.Encode(2, 2, rgb);
        byte[] idat = ReadChunks(png)[1].Data;

        using var inflated = new MemoryStream();
        using (var zlib = new ZLibStream(new MemoryStream(idat), CompressionMode.Decompress))
        {
            zlib.CopyTo(inflated);
        }

        // Each scanline: one filter byte (0 = none) then the row's RGB bytes.
        Assert.Equal(
            new byte[] { 0, 255, 0, 0, 0, 255, 0, 0, 0, 0, 255, 10, 20, 30 },
            inflated.ToArray());
    }

    [Fact]
    public void Encode_IsDeterministic()
    {
        byte[] rgb = new byte[10 * 7 * 3];
        for (int i = 0; i < rgb.Length; i++)
        {
            rgb[i] = (byte)(i * 7);
        }

        Assert.Equal(PngEncoder.Encode(10, 7, rgb), PngEncoder.Encode(10, 7, rgb));
    }

    [Fact]
    public void Encode_RejectsBadInput()
    {
        Assert.Throws<ArgumentException>(() => PngEncoder.Encode(2, 2, new byte[11]));
        Assert.Throws<ArgumentOutOfRangeException>(() => PngEncoder.Encode(0, 2, ReadOnlySpan<byte>.Empty));
        Assert.Throws<ArgumentOutOfRangeException>(() => PngEncoder.Encode(2, -1, new byte[0]));
    }
}
