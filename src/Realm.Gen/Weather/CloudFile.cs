using System;
using System.IO;
using System.Text;

namespace Realm.Gen.Weather;

/// <summary>The baked cloud climate file (<c>clouds.bin</c>), little-endian, version 1: magic <c>RCLD</c>, int version, int world size, int side, then <c>side * side</c> cover bytes and <c>side * side</c> thickness bytes.</summary>
/// <remarks>Determinism: the same climate gives the same bytes. Thread-safety: pure.</remarks>
public static class CloudFile
{
    /// <summary>File name inside the baked terrain directory.</summary>
    public const string FileName = "clouds.bin";

    /// <summary>Current format version.</summary>
    public const int Version = 1;

    private const string Magic = "RCLD";

    /// <summary>Serialises a climate.</summary>
    /// <param name="climate">The climate.</param>
    /// <returns>The file bytes.</returns>
    public static byte[] ToBytes(CloudClimate climate)
    {
        ArgumentNullException.ThrowIfNull(climate);
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
        {
            w.Write(Encoding.ASCII.GetBytes(Magic));
            w.Write(Version);
            w.Write(climate.WorldSizeMeters);
            w.Write(climate.Side);
            w.Write(climate.Cover);
            w.Write(climate.Thickness);
        }

        return stream.ToArray();
    }

    /// <summary>Reads a climate.</summary>
    /// <param name="bytes">The file bytes.</param>
    /// <returns>The climate.</returns>
    /// <exception cref="InvalidDataException">The magic or version is wrong, or the data is cut short.</exception>
    public static CloudClimate FromBytes(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        try
        {
            using var r = new BinaryReader(new MemoryStream(bytes), Encoding.ASCII);
            if (Encoding.ASCII.GetString(r.ReadBytes(Magic.Length)) != Magic)
            {
                throw new InvalidDataException("Not a cloud file.");
            }

            int version = r.ReadInt32();
            if (version != Version)
            {
                throw new InvalidDataException($"Unsupported cloud file version {version}.");
            }

            int world = r.ReadInt32();
            int side = r.ReadInt32();
            if (side < 2 || side > 4096)
            {
                throw new InvalidDataException($"Implausible cloud map size {side}.");
            }

            byte[] cover = r.ReadBytes(side * side);
            byte[] thickness = r.ReadBytes(side * side);
            if (cover.Length != side * side || thickness.Length != side * side)
            {
                throw new EndOfStreamException();
            }

            return new CloudClimate(world, side, cover, thickness);
        }
        catch (EndOfStreamException e)
        {
            throw new InvalidDataException("The cloud file is cut short.", e);
        }
    }
}
