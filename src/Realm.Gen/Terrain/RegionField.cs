using System;
using System.Collections.Generic;
using System.Text;
using Realm.Core.Ids;

namespace Realm.Gen.Terrain;

/// <summary>
/// The macro map rasterised onto a square grid of cells: which region owns each cell. Cells are
/// <see cref="CellMeters"/> wide; the grid is centred on the world origin; X grows east and Z south.
/// </summary>
/// <remarks>
/// Determinism: region indices follow ordinal ID order, never file order. Thread-safety: immutable
/// after <see cref="MacroRasterizer"/> builds it.
/// </remarks>
public sealed class RegionField
{
    /// <summary>The value stored for a cell that no region owns.</summary>
    public const ushort Unassigned = 0;

    private const ulong Fnv64Offset = 14695981039346656037UL;
    private const ulong Fnv64Prime = 1099511628211UL;

    private readonly ContentId[] _regions;
    private readonly ushort[] _cells;

    internal RegionField(int worldSizeMeters, int cellMeters, ContentId[] regionsSorted, ushort[] cells)
    {
        WorldSizeMeters = worldSizeMeters;
        CellMeters = cellMeters;
        CellsPerSide = worldSizeMeters / cellMeters;
        _regions = regionsSorted;
        _cells = cells;
    }

    /// <summary>World side length in metres.</summary>
    public int WorldSizeMeters { get; }

    /// <summary>Cell side length in metres.</summary>
    public int CellMeters { get; }

    /// <summary>Number of cells along each side.</summary>
    public int CellsPerSide { get; }

    /// <summary>Regions in ordinal ID order. A cell value of <c>n</c> means <c>Regions[n - 1]</c>.</summary>
    public IReadOnlyList<ContentId> Regions => _regions;

    /// <summary>Raw cell values, row by row from north to south (Z), each row west to east (X).</summary>
    public ReadOnlySpan<ushort> Cells => _cells;

    /// <summary>Reads the region index plus one at a cell, or <see cref="Unassigned"/>.</summary>
    /// <param name="cellX">Cell column, 0 at the west edge.</param>
    /// <param name="cellZ">Cell row, 0 at the north edge.</param>
    /// <returns>The stored cell value.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The cell is outside the grid.</exception>
    public ushort CellValue(int cellX, int cellZ)
    {
        if ((uint)cellX >= (uint)CellsPerSide || (uint)cellZ >= (uint)CellsPerSide)
        {
            throw new ArgumentOutOfRangeException(nameof(cellX), $"Cell ({cellX}, {cellZ}) is outside the grid.");
        }

        return _cells[(cellZ * CellsPerSide) + cellX];
    }

    /// <summary>Finds the region owning a cell.</summary>
    /// <param name="cellX">Cell column, 0 at the west edge.</param>
    /// <param name="cellZ">Cell row, 0 at the north edge.</param>
    /// <returns>The region ID, or the default ID when the cell is unassigned.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The cell is outside the grid.</exception>
    public ContentId RegionAtCell(int cellX, int cellZ)
    {
        ushort value = CellValue(cellX, cellZ);
        return value == Unassigned ? default : _regions[value - 1];
    }

    /// <summary>Finds the region owning a world position.</summary>
    /// <param name="x">East coordinate in metres, 0 at the world centre.</param>
    /// <param name="z">South coordinate in metres, 0 at the world centre.</param>
    /// <returns>The region ID, or the default ID when unassigned.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The position is outside the world.</exception>
    public ContentId RegionAt(double x, double z)
    {
        double half = WorldSizeMeters / 2.0;
        int cellX = (int)Math.Floor((x + half) / CellMeters);
        int cellZ = (int)Math.Floor((z + half) / CellMeters);
        return RegionAtCell(cellX, cellZ);
    }

    /// <summary>Counts the cells a region owns.</summary>
    /// <param name="region">The region ID.</param>
    /// <returns>The cell count; 0 for an unknown region.</returns>
    public int CellCount(ContentId region)
    {
        int index = Array.BinarySearch(_regions, region);
        if (index < 0)
        {
            return 0;
        }

        ushort wanted = (ushort)(index + 1);
        int count = 0;
        foreach (ushort cell in _cells)
        {
            if (cell == wanted)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Counts cells that no region owns.</summary>
    /// <returns>The number of unassigned cells.</returns>
    public int UnassignedCount()
    {
        int count = 0;
        foreach (ushort cell in _cells)
        {
            if (cell == Unassigned)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// A 64-bit fingerprint of the whole field (generator version, size, region IDs and every cell),
    /// for golden tests. Equal fields give equal checksums on every machine.
    /// </summary>
    /// <returns>The FNV-1a checksum.</returns>
    public ulong Checksum()
    {
        ulong hash = Fnv64Offset;
        hash = Mix(hash, (ulong)MacroRasterizer.GeneratorVersion);
        hash = Mix(hash, (ulong)WorldSizeMeters);
        hash = Mix(hash, (ulong)CellMeters);
        foreach (ContentId region in _regions)
        {
            foreach (byte b in Encoding.UTF8.GetBytes(region.Value))
            {
                hash = unchecked((hash ^ b) * Fnv64Prime);
            }

            hash = unchecked((hash ^ 0xFFUL) * Fnv64Prime);
        }

        foreach (ushort cell in _cells)
        {
            hash = unchecked((hash ^ (byte)(cell & 0xFF)) * Fnv64Prime);
            hash = unchecked((hash ^ (byte)(cell >> 8)) * Fnv64Prime);
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
