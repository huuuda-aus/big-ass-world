using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Realm.Gen.Foliage;

/// <summary>The baked foliage file (<c>foliage.bin</c>), little-endian, version 1.</summary>
/// <remarks>
/// Layout: magic <c>RFOL</c>, int version, int world size, int chunk size, int species count; per species: string id, string kind,
/// int tag count, strings, float trunk radius. Then one (int first, int count) pair per chunk, row by row from the north-west, then
/// the instance records (9 bytes: species, x and z within the chunk as 16-bit fractions, height in decimetres as int16, yaw and scale as bytes),
/// then int grass cell size, and 2 bytes per grass cell (species index, tufts).
/// Positions are quantised (about 4 mm, 0.1 m height), so a round trip is exact only to that.
/// Determinism: the same data gives the same bytes. Thread-safety: pure.
/// </remarks>
public static class FoliageFile
{
    /// <summary>File name inside the baked terrain directory.</summary>
    public const string FileName = "foliage.bin";

    /// <summary>Current format version.</summary>
    public const int Version = 1;

    /// <summary>Bytes per instance record.</summary>
    public const int RecordBytes = 9;

    /// <summary>Smallest scale the one-byte scale field can hold.</summary>
    public const double ScaleMin = 0.1;

    /// <summary>Largest scale the one-byte scale field can hold.</summary>
    public const double ScaleMax = 5;

    private const string Magic = "RFOL";
    private const double Fraction = 65535.0;
    private const double ByteMax = 255.0;
    private const double Decimetres = 10.0;

    /// <summary>Serialises foliage.</summary>
    /// <param name="data">The foliage.</param>
    /// <returns>The file bytes.</returns>
    public static byte[] ToBytes(FoliageData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(Encoding.ASCII.GetBytes(Magic));
            w.Write(Version);
            w.Write(data.WorldSizeMeters);
            w.Write(data.ChunkMeters);
            w.Write(data.Species.Count);
            foreach (FoliageSpecies s in data.Species)
            {
                w.Write(s.Id);
                w.Write(s.Kind);
                w.Write(s.Tags.Count);
                foreach (string tag in s.Tags)
                {
                    w.Write(tag);
                }

                w.Write((float)s.TrunkRadiusMeters);
            }

            int chunks = data.ChunksPerSide;
            var first = new int[chunks * chunks];
            var count = new int[chunks * chunks];
            Array.Fill(first, -1);
            double half = data.WorldSizeMeters / 2.0;
            for (int i = 0; i < data.Instances.Count; i++)
            {
                (int cx, int cz) = ChunkOf(data, data.Instances[i], half);
                int c = (cz * chunks) + cx;
                if (first[c] < 0)
                {
                    first[c] = i;
                }

                count[c]++;
            }

            int next = 0;
            for (int c = 0; c < first.Length; c++)
            {
                w.Write(first[c] < 0 ? next : first[c]);
                w.Write(count[c]);
                next = first[c] < 0 ? next : first[c] + count[c];
            }

            foreach (FoliageInstance inst in data.Instances)
            {
                (int cx, int cz) = ChunkOf(data, inst, half);
                w.Write((byte)inst.Species);
                w.Write((ushort)Math.Clamp(Math.Round((inst.X + half - (cx * (double)data.ChunkMeters)) / data.ChunkMeters * Fraction), 0, Fraction));
                w.Write((ushort)Math.Clamp(Math.Round((inst.Z + half - (cz * (double)data.ChunkMeters)) / data.ChunkMeters * Fraction), 0, Fraction));
                w.Write((short)Math.Clamp(Math.Round(inst.Y * Decimetres), short.MinValue, short.MaxValue));
                double turns = inst.YawRadians / (2 * Math.PI);
                w.Write((byte)Math.Round((turns - Math.Floor(turns)) * ByteMax));
                w.Write((byte)Math.Clamp(Math.Round((inst.Scale - ScaleMin) / (ScaleMax - ScaleMin) * ByteMax), 0, ByteMax));
            }

            w.Write(data.GrassCellMeters);
            for (int i = 0; i < data.GrassTufts.Length; i++)
            {
                w.Write(data.GrassSpecies[i]);
                w.Write(data.GrassTufts[i]);
            }
        }

        return stream.ToArray();
    }

    /// <summary>Reads foliage.</summary>
    /// <param name="bytes">The file bytes.</param>
    /// <returns>The foliage.</returns>
    /// <exception cref="InvalidDataException">The magic or version is wrong, or the data is cut short.</exception>
    public static FoliageData FromBytes(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        try
        {
            using var r = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8);
            if (Encoding.ASCII.GetString(r.ReadBytes(Magic.Length)) != Magic)
            {
                throw new InvalidDataException("Not a foliage file.");
            }

            int version = r.ReadInt32();
            if (version != Version)
            {
                throw new InvalidDataException($"Unsupported foliage file version {version}.");
            }

            int world = r.ReadInt32();
            int chunk = r.ReadInt32();
            int speciesCount = r.ReadInt32();
            var species = new List<FoliageSpecies>(speciesCount);
            for (int i = 0; i < speciesCount; i++)
            {
                string id = r.ReadString();
                string kind = r.ReadString();
                int tagCount = r.ReadInt32();
                var tags = new List<string>(tagCount);
                for (int t = 0; t < tagCount; t++)
                {
                    tags.Add(r.ReadString());
                }

                species.Add(new FoliageSpecies(id, kind, tags, r.ReadSingle()));
            }

            int chunks = (world + chunk - 1) / chunk;
            var firsts = new int[chunks * chunks];
            var counts = new int[chunks * chunks];
            int total = 0;
            for (int c = 0; c < firsts.Length; c++)
            {
                firsts[c] = r.ReadInt32();
                counts[c] = r.ReadInt32();
                total += counts[c];
            }

            double half = world / 2.0;
            var instances = new List<FoliageInstance>(total);
            for (int c = 0; c < firsts.Length; c++)
            {
                int cx = c % chunks;
                int cz = c / chunks;
                for (int k = 0; k < counts[c]; k++)
                {
                    int sp = r.ReadByte();
                    double x = -half + (cx * (double)chunk) + (r.ReadUInt16() / Fraction * chunk);
                    double z = -half + (cz * (double)chunk) + (r.ReadUInt16() / Fraction * chunk);
                    double y = r.ReadInt16() / Decimetres;
                    double yaw = r.ReadByte() / ByteMax * 2 * Math.PI;
                    double scale = ScaleMin + (r.ReadByte() / ByteMax * (ScaleMax - ScaleMin));
                    instances.Add(new FoliageInstance(sp, x, z, y, yaw, scale));
                }
            }

            int grassCell = r.ReadInt32();
            int side = (world + grassCell - 1) / grassCell;
            var grassSpecies = new byte[side * side];
            var grassTufts = new byte[side * side];
            for (int i = 0; i < grassTufts.Length; i++)
            {
                grassSpecies[i] = r.ReadByte();
                grassTufts[i] = r.ReadByte();
            }

            return new FoliageData(world, chunk, species, instances, grassCell, grassSpecies, grassTufts);
        }
        catch (EndOfStreamException e)
        {
            throw new InvalidDataException("The foliage file is cut short.", e);
        }
    }

    private static (int X, int Z) ChunkOf(FoliageData data, FoliageInstance inst, double half)
    {
        int chunks = data.ChunksPerSide;
        return (Math.Clamp((int)Math.Floor((inst.X + half) / data.ChunkMeters), 0, chunks - 1), Math.Clamp((int)Math.Floor((inst.Z + half) / data.ChunkMeters), 0, chunks - 1));
    }
}
