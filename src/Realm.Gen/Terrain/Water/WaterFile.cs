using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Realm.Gen.Terrain.Water;

/// <summary>The baked water file (<c>water.bin</c>): the tiles of <see cref="WaterMesher"/>, little-endian, version 1.</summary>
/// <remarks>
/// Layout: magic <c>RWTR</c>, int version, int tile count, then per tile: int tileX, int tileZ, int vertex count, int index count,
/// vertex count x 3 position floats, x 2 uv floats, x 4 colour floats, then the indices as ints.
/// Determinism: the same tiles give the same bytes. Thread-safety: pure.
/// </remarks>
public static class WaterFile
{
    /// <summary>File name inside the baked terrain directory.</summary>
    public const string FileName = "water.bin";

    /// <summary>Current format version.</summary>
    public const int Version = 1;

    private const string Magic = "RWTR";

    /// <summary>Serialises tiles.</summary>
    /// <param name="tiles">The tiles.</param>
    /// <returns>The file bytes.</returns>
    public static byte[] ToBytes(IReadOnlyList<WaterTile> tiles)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
        {
            w.Write(Encoding.ASCII.GetBytes(Magic));
            w.Write(Version);
            w.Write(tiles.Count);
            foreach (WaterTile t in tiles)
            {
                w.Write(t.TileX);
                w.Write(t.TileZ);
                w.Write(t.VertexCount);
                w.Write(t.Indices.Length);
                foreach (float f in t.Positions) { w.Write(f); }
                foreach (float f in t.Uvs) { w.Write(f); }
                foreach (float f in t.Colours) { w.Write(f); }
                foreach (int i in t.Indices) { w.Write(i); }
            }
        }

        return stream.ToArray();
    }

    /// <summary>Reads tiles.</summary>
    /// <param name="bytes">The file bytes.</param>
    /// <returns>The tiles.</returns>
    /// <exception cref="InvalidDataException">The magic or version is wrong, or the data is cut short.</exception>
    public static IReadOnlyList<WaterTile> FromBytes(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        try
        {
            using var r = new BinaryReader(new MemoryStream(bytes), Encoding.ASCII);
            if (Encoding.ASCII.GetString(r.ReadBytes(Magic.Length)) != Magic)
            {
                throw new InvalidDataException("Not a water file.");
            }

            int version = r.ReadInt32();
            if (version != Version)
            {
                throw new InvalidDataException($"Unsupported water file version {version}.");
            }

            int count = r.ReadInt32();
            var tiles = new List<WaterTile>(count);
            for (int k = 0; k < count; k++)
            {
                int tx = r.ReadInt32();
                int tz = r.ReadInt32();
                int vertices = r.ReadInt32();
                int indices = r.ReadInt32();
                float[] positions = ReadFloats(r, vertices * 3);
                float[] uvs = ReadFloats(r, vertices * 2);
                float[] colours = ReadFloats(r, vertices * 4);
                var idx = new int[indices];
                for (int i = 0; i < indices; i++) { idx[i] = r.ReadInt32(); }
                tiles.Add(new WaterTile(tx, tz, positions, uvs, colours, idx));
            }

            return tiles;
        }
        catch (EndOfStreamException e)
        {
            throw new InvalidDataException("The water file is cut short.", e);
        }
    }

    private static float[] ReadFloats(BinaryReader r, int count)
    {
        var a = new float[count];
        for (int i = 0; i < count; i++) { a[i] = r.ReadSingle(); }
        return a;
    }
}
