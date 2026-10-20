using System;
using Realm.Core.Validation;

namespace Realm.Gen.Terrain;

/// <summary>
/// Terrain validators for the height field (CLAUDE.md section 7.1): finite and not extreme heights, and no
/// seams where regions meet.
/// </summary>
/// <remarks>Determinism: scans in fixed order. Thread-safety: stateless.</remarks>
public static class HeightFieldValidator
{
    /// <summary>Default largest allowed height or depth in metres. Placeholder, to be set by the developer.</summary>
    public const double DefaultMaxAbsHeightMeters = 4000.0;

    /// <summary>Default steepest allowed slope (rise over run) between neighbouring vertices across a region border. Placeholder.</summary>
    public const double DefaultMaxBorderSlope = 2.0;

    private const double HalfDivisor = 2.0;

    /// <summary>Checks a height field and reports every problem class once, with a count and an example.</summary>
    /// <param name="field">The heights.</param>
    /// <param name="regions">The regions, used to find region borders.</param>
    /// <param name="report">Receives issues.</param>
    /// <param name="maxAbsHeightMeters">Largest allowed absolute height.</param>
    /// <param name="maxBorderSlope">Steepest allowed slope across a region border.</param>
    public static void Validate(
        HeightField field,
        RegionField regions,
        ValidationReport report,
        double maxAbsHeightMeters = DefaultMaxAbsHeightMeters,
        double maxBorderSlope = DefaultMaxBorderSlope)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(regions);
        ArgumentNullException.ThrowIfNull(report);

        int side = field.VerticesPerSide;
        ReadOnlySpan<float> heights = field.Heights;
        string where = "height field";
        double half = field.WorldSizeMeters / HalfDivisor;

        int nonFinite = 0;
        int extreme = 0;
        string nonFiniteExample = string.Empty;
        string extremeExample = string.Empty;
        for (int i = 0; i < heights.Length; i++)
        {
            float h = heights[i];
            if (!float.IsFinite(h))
            {
                if (nonFinite++ == 0)
                {
                    nonFiniteExample = Describe(i, side, field.SpacingMeters, half);
                }
            }
            else if (Math.Abs(h) > maxAbsHeightMeters && extreme++ == 0)
            {
                extremeExample = $"{Describe(i, side, field.SpacingMeters, half)} = {h} m";
            }
        }

        if (nonFinite > 0)
        {
            report.Error(
                TerrainCodes.HeightNonFinite,
                where,
                $"{nonFinite} height(s) are not finite numbers, for example {nonFiniteExample}.",
                "Check the biome properties and the generator settings.");
        }

        if (extreme > 0)
        {
            report.Error(
                TerrainCodes.HeightOutOfRange,
                where,
                $"{extreme} height(s) exceed +-{maxAbsHeightMeters} m, for example {extremeExample}.",
                "Reduce base_height_m, relief_m or water_depth_m of the biomes involved.");
        }

        if (nonFinite == 0)
        {
            CheckBorders(field, regions, report, maxBorderSlope, half, where);
        }
    }

    private static void CheckBorders(
        HeightField field, RegionField regions, ValidationReport report, double maxSlope, double half, string where)
    {
        int side = field.VerticesPerSide;
        int spacing = field.SpacingMeters;
        ReadOnlySpan<float> heights = field.Heights;
        var stats = new BorderStats();

        for (int z = 0; z < side; z++)
        {
            for (int x = 0; x < side; x++)
            {
                int index = (z * side) + x;
                if (x + 1 < side)
                {
                    CheckPair(heights, regions, index, index + 1, (x, z), (x + 1, z), side, spacing, half, maxSlope, ref stats);
                }

                if (z + 1 < side)
                {
                    CheckPair(heights, regions, index, index + side, (x, z), (x, z + 1), side, spacing, half, maxSlope, ref stats);
                }
            }
        }

        if (stats.Steep > 0)
        {
            report.Error(
                TerrainCodes.BorderTooSteep,
                where,
                $"{stats.Steep} step(s) across region borders are steeper than {maxSlope} (rise over run); the worst is " +
                $"{stats.Worst:F2}, for example {stats.Example}.",
                "Increase the blend radius, or reduce the height difference between neighbouring biomes.");
        }
    }

    private static void CheckPair(
        ReadOnlySpan<float> heights,
        RegionField regions,
        int a,
        int b,
        (int X, int Z) vertexA,
        (int X, int Z) vertexB,
        int side,
        int spacing,
        double half,
        double maxSlope,
        ref BorderStats stats)
    {
        if (RegionAtVertex(regions, vertexA.X, vertexA.Z, spacing, half)
            == RegionAtVertex(regions, vertexB.X, vertexB.Z, spacing, half))
        {
            return;
        }

        double slope = Math.Abs(heights[b] - heights[a]) / spacing;
        if (slope <= maxSlope)
        {
            return;
        }

        if (stats.Steep++ == 0)
        {
            stats.Example = Describe(a, side, spacing, half);
        }

        stats.Worst = Math.Max(stats.Worst, slope);
    }

    private static ushort RegionAtVertex(RegionField regions, int vx, int vz, int spacing, double half)
    {
        double x = -half + (vx * (double)spacing);
        double z = -half + (vz * (double)spacing);
        int cx = Math.Clamp((int)Math.Floor((x + half) / regions.CellMeters), 0, regions.CellsPerSide - 1);
        int cz = Math.Clamp((int)Math.Floor((z + half) / regions.CellMeters), 0, regions.CellsPerSide - 1);
        return regions.CellValue(cx, cz);
    }

    private static string Describe(int index, int side, int spacing, double half)
    {
        double x = -half + ((index % side) * (double)spacing);
        double z = -half + ((index / side) * (double)spacing);
        return $"({x}, {z})";
    }

    private struct BorderStats
    {
        public int Steep;
        public double Worst;
        public string Example;
    }
}
