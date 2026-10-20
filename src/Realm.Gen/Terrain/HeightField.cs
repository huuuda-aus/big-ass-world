using System;

namespace Realm.Gen.Terrain;

/// <summary>
/// Terrain heights in metres on a square grid of vertices. Vertex (0, 0) is the north-west corner of the
/// world; X grows east and Z grows south; sea level is 0. Adjacent chunks share their edge vertices.
/// </summary>
/// <remarks>Determinism: see <see cref="Checksum"/>. Thread-safety: immutable after generation.</remarks>
public sealed class HeightField
{
    private const ulong Fnv64Offset = 14695981039346656037UL;
    private const ulong Fnv64Prime = 1099511628211UL;
    private const double HalfDivisor = 2.0;

    private readonly float[] _heights;

    internal HeightField(int worldSizeMeters, int spacingMeters, float[] heights)
    {
        WorldSizeMeters = worldSizeMeters;
        SpacingMeters = spacingMeters;
        VerticesPerSide = (worldSizeMeters / spacingMeters) + 1;
        _heights = heights;

        float min = float.PositiveInfinity;
        float max = float.NegativeInfinity;
        foreach (float h in heights)
        {
            if (h < min)
            {
                min = h;
            }

            if (h > max)
            {
                max = h;
            }
        }

        MinHeight = min;
        MaxHeight = max;
    }

    /// <summary>World side length in metres.</summary>
    public int WorldSizeMeters { get; }

    /// <summary>Distance between neighbouring vertices in metres.</summary>
    public int SpacingMeters { get; }

    /// <summary>Number of vertices along each side (world size / spacing + 1).</summary>
    public int VerticesPerSide { get; }

    /// <summary>Lowest height in metres.</summary>
    public float MinHeight { get; }

    /// <summary>Highest height in metres.</summary>
    public float MaxHeight { get; }

    /// <summary>All heights, row by row from north to south, each row west to east.</summary>
    public ReadOnlySpan<float> Heights => _heights;

    /// <summary>Reads one vertex.</summary>
    /// <param name="vertexX">Column, 0 at the west edge.</param>
    /// <param name="vertexZ">Row, 0 at the north edge.</param>
    /// <returns>The height in metres.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The vertex is outside the grid.</exception>
    public float At(int vertexX, int vertexZ)
    {
        if ((uint)vertexX >= (uint)VerticesPerSide || (uint)vertexZ >= (uint)VerticesPerSide)
        {
            throw new ArgumentOutOfRangeException(nameof(vertexX), $"Vertex ({vertexX}, {vertexZ}) is outside the grid.");
        }

        return _heights[(vertexZ * VerticesPerSide) + vertexX];
    }

    /// <summary>Bilinearly interpolated height at a world position.</summary>
    /// <param name="x">East coordinate in metres, 0 at the world centre.</param>
    /// <param name="z">South coordinate in metres, 0 at the world centre.</param>
    /// <returns>The height in metres.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The position is outside the world.</exception>
    public double Sample(double x, double z)
    {
        double half = WorldSizeMeters / HalfDivisor;
        if (x < -half || x > half || z < -half || z > half)
        {
            throw new ArgumentOutOfRangeException(nameof(x), $"Position ({x}, {z}) is outside the world.");
        }

        double gx = (x + half) / SpacingMeters;
        double gz = (z + half) / SpacingMeters;
        int x0 = Math.Min((int)Math.Floor(gx), VerticesPerSide - 2);
        int z0 = Math.Min((int)Math.Floor(gz), VerticesPerSide - 2);
        double tx = gx - x0;
        double tz = gz - z0;

        double h00 = _heights[(z0 * VerticesPerSide) + x0];
        double h10 = _heights[(z0 * VerticesPerSide) + x0 + 1];
        double h01 = _heights[((z0 + 1) * VerticesPerSide) + x0];
        double h11 = _heights[((z0 + 1) * VerticesPerSide) + x0 + 1];
        double top = h00 + (tx * (h10 - h00));
        double bottom = h01 + (tx * (h11 - h01));
        return top + (tz * (bottom - top));
    }

    /// <summary>
    /// A 64-bit fingerprint of the generator version, sizes and every height (exact float bits), for golden
    /// tests. Equal fields give equal checksums on every machine.
    /// </summary>
    /// <returns>The FNV-1a checksum.</returns>
    public ulong Checksum()
    {
        ulong hash = Fnv64Offset;
        hash = Mix(hash, (ulong)HeightFieldGenerator.GeneratorVersion);
        hash = Mix(hash, (ulong)WorldSizeMeters);
        hash = Mix(hash, (ulong)SpacingMeters);
        foreach (float h in _heights)
        {
            uint bits = unchecked((uint)BitConverter.SingleToInt32Bits(h));
            for (int shift = 0; shift < 32; shift += 8)
            {
                hash = unchecked((hash ^ ((bits >> shift) & 0xFFU)) * Fnv64Prime);
            }
        }

        return hash;
    }

    private static ulong Mix(ulong hash, ulong value)
    {
        for (int shift = 0; shift < 64; shift += 8)
        {
            hash = unchecked((hash ^ ((value >> shift) & 0xFFUL)) * Fnv64Prime);
        }

        return hash;
    }
}
