using System;
using System.Collections.Generic;
using Realm.Core.Random;

namespace Realm.Gen.Foliage;

/// <summary>One tuft of grass.</summary>
/// <param name="X">World east in metres.</param>
/// <param name="Z">World south in metres.</param>
/// <param name="YawRadians">Turn about the vertical axis.</param>
/// <param name="Scale">Size multiplier.</param>
public readonly record struct Tuft(double X, double Z, double YawRadians, double Scale);

/// <summary>
/// Scatters the tufts of one grass cell at run time. The baked file only holds how many tufts a cell gets; where they stand comes from
/// here, so the same cell always grows the same tufts and the game needs no per-tuft data on disk.
/// </summary>
/// <remarks>Determinism: seeded by the world seed and the cell. Thread-safety: pure.</remarks>
public static class GrassScatter
{
    private const double MinScale = 0.7;
    private const double MaxScale = 1.3;
    private const ulong CellMixX = 0x9E3779B97F4A7C15UL;
    private const ulong CellMixZ = 0xC2B2AE3D27D4EB4FUL;

    /// <summary>Scatters the tufts of one cell.</summary>
    /// <param name="seed">World seed.</param>
    /// <param name="cellX">Cell column, 0 at the world's west edge.</param>
    /// <param name="cellZ">Cell row, 0 at the world's north edge.</param>
    /// <param name="cellMeters">Side of a cell in metres.</param>
    /// <param name="worldSizeMeters">World side length in metres.</param>
    /// <param name="count">Number of tufts.</param>
    /// <returns>The tufts, all inside the cell.</returns>
    public static IReadOnlyList<Tuft> Cell(ulong seed, int cellX, int cellZ, int cellMeters, int worldSizeMeters, int count)
    {
        var tufts = new List<Tuft>(Math.Max(0, count));
        if (count <= 0)
        {
            return tufts;
        }

        ulong key = Rng.ForKey(seed, "foliage", "grass").NextUInt64();
        var rng = new Rng(key ^ ((ulong)(uint)cellX * CellMixX) ^ ((ulong)(uint)cellZ * CellMixZ));
        double west = -worldSizeMeters / 2.0 + (cellX * (double)cellMeters);
        double north = -worldSizeMeters / 2.0 + (cellZ * (double)cellMeters);
        for (int i = 0; i < count; i++)
        {
            tufts.Add(new Tuft(
                west + (rng.NextDouble() * cellMeters),
                north + (rng.NextDouble() * cellMeters),
                rng.NextDouble() * 2 * Math.PI,
                MinScale + (rng.NextDouble() * (MaxScale - MinScale))));
        }

        return tufts;
    }
}
