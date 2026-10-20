using System;
using System.Collections.Generic;
using System.Linq;
using Realm.Core.Geometry;
using Realm.Core.Ids;
using Realm.Core.Validation;
using Realm.Data.Content;

namespace Realm.Gen.Terrain;

/// <summary>
/// Rasterises a macro map's region polygons onto a grid of macro cells and validates that every cell
/// belongs to exactly one region (CLAUDE.md sections 6.1 and 7.1).
/// </summary>
/// <remarks>
/// A cell belongs to the region whose polygon contains the cell centre. Polygons that share an edge
/// give each centre exactly one owner (see <see cref="Polygon.Contains"/>). Regions narrower than a
/// cell can be missed; that is reported as <see cref="TerrainCodes.RegionOwnsNoCells"/>.
/// Determinism: no randomness; identified by <see cref="GeneratorId"/> and <see cref="GeneratorVersion"/>
/// plus the input map and cell size. Regions are numbered in ordinal ID order. Thread-safety: stateless.
/// </remarks>
public static class MacroRasterizer
{
    /// <summary>Identifies this generator in saved or baked data.</summary>
    public const string GeneratorId = "terrain.macro_raster";

    /// <summary>
    /// Bump when the output for the same input changes, and regenerate golden tests in the same change
    /// (CLAUDE.md section 7.4).
    /// </summary>
    public const int GeneratorVersion = 1;

    /// <summary>Proposed macro cell size in metres (ADR 0005 assumption, not yet a final decision).</summary>
    public const int DefaultCellMeters = 64;

    private const int ExampleLimit = 3;
    private const double CellCentreOffset = 0.5;
    private const double HalfDivisor = 2.0;

    /// <summary>Builds the region field and reports every coverage problem.</summary>
    /// <param name="map">The macro map.</param>
    /// <param name="report">Receives all issues. The field is complete only when there are no errors.</param>
    /// <param name="cellMeters">Cell side length in metres; must be positive.</param>
    /// <returns>
    /// The field. When the world size is not a multiple of the cell size, or there are too many regions,
    /// an empty field with no cells is returned together with an error.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="cellMeters"/> is not positive.</exception>
    public static RegionField Rasterize(MacroMapDefinition map, ValidationReport report, int cellMeters = DefaultCellMeters)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(report);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cellMeters);

        string where = map.Id.Value;
        ContentId[] regions = map.Regions.Select(r => r.Id).OrderBy(id => id).ToArray();

        if (map.WorldSizeMeters % cellMeters != 0)
        {
            report.Error(
                TerrainCodes.WorldSizeNotCellMultiple,
                where,
                $"World size {map.WorldSizeMeters} m is not a multiple of the {cellMeters} m macro cell size.",
                $"Use a world_size_m that is a multiple of {cellMeters} (for example {(map.WorldSizeMeters / cellMeters) * cellMeters}).");
            return new RegionField(0, cellMeters, regions, Array.Empty<ushort>());
        }

        if (regions.Length >= ushort.MaxValue)
        {
            report.Error(
                TerrainCodes.TooManyRegions,
                where,
                $"{regions.Length} regions exceed the maximum of {ushort.MaxValue - 1}.");
            return new RegionField(0, cellMeters, regions, Array.Empty<ushort>());
        }

        int side = map.WorldSizeMeters / cellMeters;
        double half = map.WorldSizeMeters / HalfDivisor;
        var cells = new ushort[side * side];
        var overlaps = new SortedDictionary<(int First, int Second), OverlapInfo>();

        // Visit regions in sorted order so "first claim wins" is independent of file order.
        foreach (RegionDefinition region in map.Regions.OrderBy(r => r.Id))
        {
            int index = Array.BinarySearch(regions, region.Id);
            ushort value = (ushort)(index + 1);
            foreach (Polygon polygon in region.Polygons)
            {
                ClaimCells(polygon, value, side, cellMeters, half, cells, overlaps);
            }
        }

        ReportOverlaps(overlaps, regions, where, report);
        ReportGaps(cells, side, cellMeters, half, where, report);
        ReportEmptyRegions(cells, regions, where, report);

        return new RegionField(map.WorldSizeMeters, cellMeters, regions, cells);
    }

    private static void ClaimCells(
        Polygon polygon,
        ushort value,
        int side,
        int cellMeters,
        double half,
        ushort[] cells,
        SortedDictionary<(int First, int Second), OverlapInfo> overlaps)
    {
        int xStart = Math.Max(0, (int)Math.Floor((polygon.MinX + half) / cellMeters));
        int xEnd = Math.Min(side - 1, (int)Math.Floor((polygon.MaxX + half) / cellMeters));
        int zStart = Math.Max(0, (int)Math.Floor((polygon.MinZ + half) / cellMeters));
        int zEnd = Math.Min(side - 1, (int)Math.Floor((polygon.MaxZ + half) / cellMeters));

        for (int cz = zStart; cz <= zEnd; cz++)
        {
            for (int cx = xStart; cx <= xEnd; cx++)
            {
                var centre = new Point2D(
                    -half + ((cx + CellCentreOffset) * cellMeters),
                    -half + ((cz + CellCentreOffset) * cellMeters));
                if (!polygon.Contains(centre))
                {
                    continue;
                }

                int slot = (cz * side) + cx;
                ushort owner = cells[slot];
                if (owner == RegionField.Unassigned)
                {
                    cells[slot] = value;
                }
                else if (owner != value)
                {
                    (int, int) pair = owner < value ? (owner, value) : (value, owner);
                    overlaps.TryGetValue(pair, out OverlapInfo info);
                    overlaps[pair] = info.Count == 0 ? new OverlapInfo(1, centre) : info with { Count = info.Count + 1 };
                }
            }
        }
    }

    private static void ReportOverlaps(
        SortedDictionary<(int First, int Second), OverlapInfo> overlaps,
        ContentId[] regions,
        string where,
        ValidationReport report)
    {
        foreach (((int first, int second), OverlapInfo info) in overlaps)
        {
            report.Error(
                TerrainCodes.Overlap,
                where,
                $"Regions '{regions[first - 1]}' and '{regions[second - 1]}' overlap on {info.Count} cell(s), " +
                $"for example around ({info.Example.X}, {info.Example.Z}).",
                "Move the shared border so each area belongs to one region.");
        }
    }

    private static void ReportGaps(
        ushort[] cells, int side, int cellMeters, double half, string where, ValidationReport report)
    {
        int count = 0;
        var examples = new List<string>();
        for (int slot = 0; slot < cells.Length; slot++)
        {
            if (cells[slot] != RegionField.Unassigned)
            {
                continue;
            }

            count++;
            if (examples.Count < ExampleLimit)
            {
                double x = -half + (((slot % side) + CellCentreOffset) * cellMeters);
                double z = -half + (((slot / side) + CellCentreOffset) * cellMeters);
                examples.Add($"({x}, {z})");
            }
        }

        if (count > 0)
        {
            report.Error(
                TerrainCodes.Gap,
                where,
                $"{count} cell(s) belong to no region, for example around {string.Join(", ", examples)}.",
                "Extend a region (an ocean region counts) so that every part of the world is covered.");
        }
    }

    private static void ReportEmptyRegions(ushort[] cells, ContentId[] regions, string where, ValidationReport report)
    {
        var owned = new bool[regions.Length + 1];
        foreach (ushort cell in cells)
        {
            owned[cell] = true;
        }

        for (int i = 0; i < regions.Length; i++)
        {
            if (!owned[i + 1])
            {
                report.Warning(
                    TerrainCodes.RegionOwnsNoCells,
                    where,
                    $"Region '{regions[i]}' owns no cells; it is smaller than the macro cell size or fully overlapped.",
                    "Enlarge the region or remove it.");
            }
        }
    }

    private readonly record struct OverlapInfo(int Count, Point2D Example);
}
