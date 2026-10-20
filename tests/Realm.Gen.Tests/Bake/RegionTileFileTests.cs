using System;
using System.IO;
using System.Linq;
using Realm.Gen.Bake;
using Xunit;

namespace Realm.Gen.Tests.Bake;

public class RegionTileFileTests
{
    private static RegionTile Tile(int size = 4, int rx = -3, int rz = 2)
    {
        var heights = Enumerable.Range(0, size * size).Select(i => (i * 1.5f) - 7f).ToArray();
        var colours = Enumerable.Range(0, size * size).Select(i => 0xFF000000u | (uint)(i * 977)).ToArray();
        return new RegionTile(rx, rz, size, 4f, heights, colours);
    }

    [Fact]
    public void RoundTrip_PreservesEverything()
    {
        RegionTile original = Tile();

        RegionTile back = RegionTileFile.FromBytes(RegionTileFile.ToBytes(original));

        Assert.Equal(original.RegionX, back.RegionX);
        Assert.Equal(original.RegionZ, back.RegionZ);
        Assert.Equal(original.Size, back.Size);
        Assert.Equal(original.SpacingMeters, back.SpacingMeters);
        Assert.Equal(original.Heights, back.Heights);
        Assert.Equal(original.Colours, back.Colours);
        Assert.Equal(16f, back.ExtentMeters);
    }

    [Fact]
    public void Bytes_AreDeterministic_AndSizedExactly()
    {
        byte[] a = RegionTileFile.ToBytes(Tile());

        Assert.Equal(a, RegionTileFile.ToBytes(Tile()));
        Assert.Equal(24 + (4 * 4 * 8) + 8, a.Length); // header, heights and colours, checksum
        Assert.Equal(new byte[] { (byte)'R', (byte)'T', (byte)'I', (byte)'L' }, a[..4]);
    }

    [Fact]
    public void ExtremeValues_SurviveTheRoundTrip()
    {
        var heights = new[] { float.MinValue, float.MaxValue, 0f, -0f, 1e-30f, -1e30f, float.Epsilon, 123.456f, 0, 0, 0, 0, 0, 0, 0, 0 };
        var tile = new RegionTile(0, 0, 4, 0.25f, heights, new uint[16]);

        Assert.Equal(heights, RegionTileFile.FromBytes(RegionTileFile.ToBytes(tile)).Heights);
    }

    [Fact]
    public void FlippedByte_IsDetectedByTheChecksum()
    {
        byte[] bytes = RegionTileFile.ToBytes(Tile());
        bytes[40] ^= 0x01;

        InvalidDataException e = Assert.Throws<InvalidDataException>(() => RegionTileFile.FromBytes(bytes));
        Assert.Contains("checksum", e.Message);
    }

    [Fact]
    public void BadFiles_AreRejectedWithClearMessages()
    {
        byte[] good = RegionTileFile.ToBytes(Tile());

        Assert.Contains("too short", Assert.Throws<InvalidDataException>(() => RegionTileFile.FromBytes(new byte[10])).Message);

        byte[] badMagic = (byte[])good.Clone();
        badMagic[0] = (byte)'X';
        Assert.Contains("magic", Assert.Throws<InvalidDataException>(() => RegionTileFile.FromBytes(badMagic)).Message);

        byte[] badVersion = (byte[])good.Clone();
        badVersion[4] = 99;
        Assert.Contains("version 99", Assert.Throws<InvalidDataException>(() => RegionTileFile.FromBytes(badVersion)).Message);

        Assert.Contains("bytes", Assert.Throws<InvalidDataException>(() => RegionTileFile.FromBytes(good[..^9])).Message);
        Assert.Contains("bytes", Assert.Throws<InvalidDataException>(() => RegionTileFile.FromBytes(good.Concat(new byte[] { 0 }).ToArray())).Message);

        byte[] hugeSize = (byte[])good.Clone();
        BitConverter.TryWriteBytes(hugeSize.AsSpan(16), 1_000_000);
        Assert.Contains("out of range", Assert.Throws<InvalidDataException>(() => RegionTileFile.FromBytes(hugeSize)).Message);
    }

    [Fact]
    public void Write_ThenRead_ThroughTheFileSystem_LeavesNoTemporaryFile()
    {
        string dir = Path.Combine(Path.GetTempPath(), "realm-tiles-" + Guid.NewGuid().ToString("N"));
        try
        {
            RegionTile tile = Tile(rx: -3, rz: 2);

            string path = RegionTileFile.Write(dir, tile);
            RegionTileFile.Write(dir, tile); // overwriting is fine

            Assert.Equal(Path.Combine(dir, "region_-3_2.tile"), path);
            Assert.Equal(new[] { "region_-3_2.tile" }, Directory.GetFiles(dir).Select(Path.GetFileName).ToArray());
            Assert.Equal(tile.Heights, RegionTileFile.Read(path).Heights);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void Constructor_RejectsBadArrays()
    {
        Assert.Throws<ArgumentException>(() => new RegionTile(0, 0, 4, 1f, new float[15], new uint[16]));
        Assert.Throws<ArgumentException>(() => new RegionTile(0, 0, 4, 1f, new float[16], new uint[15]));
        Assert.Throws<ArgumentException>(() => new RegionTile(0, 0, 1, 1f, new float[1], new uint[1]));
        Assert.Throws<ArgumentNullException>(() => new RegionTile(0, 0, 4, 1f, null!, new uint[16]));
    }
}
