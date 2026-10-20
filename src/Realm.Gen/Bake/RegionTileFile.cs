using System;
using System.IO;

namespace Realm.Gen.Bake;

/// <summary>
/// Reads and writes <see cref="RegionTile"/> files. Layout (little endian): magic <c>RTIL</c>, format version (uint),
/// region X (int), region Z (int), size (int), spacing (float), <c>size x size</c> heights (float), the same number of
/// colours (uint), and a 64-bit FNV-1a checksum of everything before it.
/// </summary>
/// <remarks>
/// Determinism: the same tile always produces the same bytes. Thread-safety: stateless. Files are written to a
/// temporary name and moved into place, so a crash never leaves a half-written tile (CLAUDE.md section 8.3).
/// </remarks>
public static class RegionTileFile
{
    /// <summary>The current format version; bump it and keep a reader for the old one when the layout changes.</summary>
    public const uint FormatVersion = 1;

    private const uint Magic = 0x4C495452; // "RTIL" read as little-endian bytes R, T, I, L
    private const int HeaderBytes = 4 + 4 + 4 + 4 + 4 + 4;
    private const int ChecksumBytes = 8;
    private const int MaxSize = 4096;
    private const ulong Fnv64Offset = 14695981039346656037UL;
    private const ulong Fnv64Prime = 1099511628211UL;

    /// <summary>Serialises a tile.</summary>
    /// <param name="tile">The tile.</param>
    /// <returns>The file bytes.</returns>
    public static byte[] ToBytes(RegionTile tile)
    {
        ArgumentNullException.ThrowIfNull(tile);
        int count = tile.Size * tile.Size;
        var bytes = new byte[HeaderBytes + (count * 8) + ChecksumBytes];
        var writer = new Cursor(bytes);
        writer.WriteUInt(Magic);
        writer.WriteUInt(FormatVersion);
        writer.WriteInt(tile.RegionX);
        writer.WriteInt(tile.RegionZ);
        writer.WriteInt(tile.Size);
        writer.WriteFloat(tile.SpacingMeters);
        foreach (float h in tile.Heights)
        {
            writer.WriteFloat(h);
        }

        foreach (uint c in tile.Colours)
        {
            writer.WriteUInt(c);
        }

        writer.WriteULong(Fnv(bytes.AsSpan(0, bytes.Length - ChecksumBytes)));
        return bytes;
    }

    /// <summary>Parses a tile.</summary>
    /// <param name="bytes">The file bytes.</param>
    /// <returns>The tile.</returns>
    /// <exception cref="InvalidDataException">The bytes are not a valid, complete, unmodified tile of a known version.</exception>
    public static RegionTile FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < HeaderBytes + ChecksumBytes)
        {
            throw new InvalidDataException("The file is too short to be a region tile.");
        }

        var reader = new Cursor(bytes.ToArray());
        if (reader.ReadUInt() != Magic)
        {
            throw new InvalidDataException("The file is not a region tile (bad magic bytes).");
        }

        uint version = reader.ReadUInt();
        if (version != FormatVersion)
        {
            throw new InvalidDataException($"Region tile format version {version} is not supported (expected {FormatVersion}).");
        }

        int regionX = reader.ReadInt();
        int regionZ = reader.ReadInt();
        int size = reader.ReadInt();
        float spacing = reader.ReadFloat();
        if (size < 2 || size > MaxSize)
        {
            throw new InvalidDataException($"Region size {size} is out of range.");
        }

        long expected = HeaderBytes + ((long)size * size * 8) + ChecksumBytes;
        if (bytes.Length != expected)
        {
            throw new InvalidDataException($"The file is {bytes.Length} bytes but a region of size {size} needs {expected}.");
        }

        ulong stored = BitConverter.ToUInt64(bytes[^ChecksumBytes..]);
        if (stored != Fnv(bytes[..^ChecksumBytes]))
        {
            throw new InvalidDataException("The region tile is damaged (checksum mismatch).");
        }

        int count = size * size;
        var heights = new float[count];
        var colours = new uint[count];
        for (int i = 0; i < count; i++)
        {
            heights[i] = reader.ReadFloat();
        }

        for (int i = 0; i < count; i++)
        {
            colours[i] = reader.ReadUInt();
        }

        return new RegionTile(regionX, regionZ, size, spacing, heights, colours);
    }

    /// <summary>Writes a tile to a file atomically (temporary file, then move).</summary>
    /// <param name="directory">Target directory; created when missing.</param>
    /// <param name="tile">The tile.</param>
    /// <returns>The path written.</returns>
    public static string Write(string directory, RegionTile tile)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(tile);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, RegionTile.FileName(tile.RegionX, tile.RegionZ));
        string temporary = path + ".tmp";
        File.WriteAllBytes(temporary, ToBytes(tile));
        File.Move(temporary, path, overwrite: true);
        return path;
    }

    /// <summary>Reads a tile file.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The tile.</returns>
    /// <exception cref="InvalidDataException">The file is not a valid tile.</exception>
    public static RegionTile Read(string path) => FromBytes(File.ReadAllBytes(path));

    private static ulong Fnv(ReadOnlySpan<byte> data)
    {
        ulong hash = Fnv64Offset;
        foreach (byte b in data)
        {
            hash = unchecked((hash ^ b) * Fnv64Prime);
        }

        return hash;
    }

    private sealed class Cursor
    {
        private readonly byte[] _bytes;
        private int _position;

        public Cursor(byte[] bytes) => _bytes = bytes;

        public uint ReadUInt()
        {
            uint v = BitConverter.ToUInt32(_bytes, _position);
            _position += 4;
            return v;
        }

        public int ReadInt()
        {
            int v = BitConverter.ToInt32(_bytes, _position);
            _position += 4;
            return v;
        }

        public float ReadFloat()
        {
            float v = BitConverter.ToSingle(_bytes, _position);
            _position += 4;
            return v;
        }

        public void WriteUInt(uint v)
        {
            BitConverter.TryWriteBytes(_bytes.AsSpan(_position), v);
            _position += 4;
        }

        public void WriteInt(int v)
        {
            BitConverter.TryWriteBytes(_bytes.AsSpan(_position), v);
            _position += 4;
        }

        public void WriteFloat(float v)
        {
            BitConverter.TryWriteBytes(_bytes.AsSpan(_position), v);
            _position += 4;
        }

        public void WriteULong(ulong v)
        {
            BitConverter.TryWriteBytes(_bytes.AsSpan(_position), v);
            _position += 8;
        }
    }
}
