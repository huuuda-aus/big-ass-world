using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Realm.Core.Geometry;
using Realm.Core.Ids;
using Realm.Core.Validation;
using Realm.Data.Content;
using Realm.Gen.Terrain;
using Xunit;

namespace Realm.Gen.Tests;

public class MacroRasterizerTests
{
    private const int Cell = 64;

    private static Polygon Rect(double x0, double z0, double x1, double z1)
    {
        var pts = new[] { new Point2D(x0, z0), new Point2D(x1, z0), new Point2D(x1, z1), new Point2D(x0, z1) };
        Assert.True(Polygon.TryCreate(pts, out Polygon? p, out string? problem), problem);
        return p!;
    }

    private static RegionDefinition Region(string id, params Polygon[] polygons) =>
        new(ContentId.Parse(id), id, ContentId.Parse("biome.any"), polygons);

    private static MacroMapDefinition Map(int size, params RegionDefinition[] regions) =>
        new(ContentId.Parse("world.test"), "Test", size, regions);

    private static (RegionField Field, ValidationReport Report) Run(MacroMapDefinition map, int cell = Cell)
    {
        var report = new ValidationReport();
        return (MacroRasterizer.Rasterize(map, report, cell), report);
    }

    // A 640 m world (10 x 10 cells) split down the middle at x = 0.
    private static MacroMapDefinition TwoHalves() => Map(
        640,
        Region("region.west", Rect(-320, -320, 0, 320)),
        Region("region.east", Rect(0, -320, 320, 320)));

    [Fact]
    public void FullCoverage_HasNoIssues_AndCountsAreExact()
    {
        var (field, report) = Run(TwoHalves());

        Assert.Empty(report.Issues);
        Assert.Equal(10, field.CellsPerSide);
        Assert.Equal(50, field.CellCount(ContentId.Parse("region.west")));
        Assert.Equal(50, field.CellCount(ContentId.Parse("region.east")));
        Assert.Equal(0, field.UnassignedCount());
    }

    [Fact]
    public void RegionAt_ReturnsTheOwner()
    {
        var (field, _) = Run(TwoHalves());

        Assert.Equal("region.west", field.RegionAt(-300, -300).Value);
        Assert.Equal("region.east", field.RegionAt(300, 300).Value);
        Assert.Equal("region.west", field.RegionAtCell(0, 0).Value);
        Assert.Equal("region.east", field.RegionAtCell(9, 9).Value);
    }

    [Fact]
    public void CellAccess_OutsideGrid_Throws()
    {
        var (field, _) = Run(TwoHalves());

        Assert.Throws<ArgumentOutOfRangeException>(() => field.RegionAtCell(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => field.RegionAtCell(0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => field.RegionAt(320.1, 0));
    }

    [Fact]
    public void Gap_IsReported_WithCountAndExamples()
    {
        var map = Map(640, Region("region.west", Rect(-320, -320, 0, 320)));

        var (field, report) = Run(map);

        ValidationIssue issue = Assert.Single(report.Issues);
        Assert.Equal(TerrainCodes.Gap, issue.Code);
        Assert.Equal(Severity.Error, issue.Severity);
        Assert.Contains("50 cell(s)", issue.Message);
        Assert.Equal(50, field.UnassignedCount());
    }

    [Fact]
    public void Overlap_IsReported_ForTheRegionPair()
    {
        var map = Map(
            640,
            Region("region.west", Rect(-320, -320, 64, 320)),
            Region("region.east", Rect(0, -320, 320, 320)));

        var (_, report) = Run(map);

        ValidationIssue issue = Assert.Single(report.Issues);
        Assert.Equal(TerrainCodes.Overlap, issue.Code);
        Assert.Contains("region.east", issue.Message);
        Assert.Contains("region.west", issue.Message);
        Assert.Contains("10 cell(s)", issue.Message); // one overlapping column of 10 cells
    }

    [Fact]
    public void SharedBorderThroughCellCentres_GivesNoGapAndNoOverlap()
    {
        // x = -32 is the centre line of cell column 4 (centres are at -288 + 64k).
        var map = Map(
            640,
            Region("region.west", Rect(-320, -320, -32, 320)),
            Region("region.east", Rect(-32, -320, 320, 320)));

        var (field, report) = Run(map);

        Assert.Empty(report.Issues);
        Assert.Equal(40, field.CellCount(ContentId.Parse("region.west")));
        Assert.Equal(60, field.CellCount(ContentId.Parse("region.east"))); // half-open rule: the border column goes east
    }

    [Fact]
    public void RegionWithTwoPolygons_OwnsBoth()
    {
        var map = Map(
            640,
            Region("region.ring", Rect(-320, -320, -192, 320), Rect(192, -320, 320, 320)),
            Region("region.middle", Rect(-192, -320, 192, 320)));

        var (field, report) = Run(map);

        Assert.Empty(report.Issues);
        Assert.Equal(40, field.CellCount(ContentId.Parse("region.ring")));
        Assert.Equal(60, field.CellCount(ContentId.Parse("region.middle")));
    }

    [Fact]
    public void RegionOverlappingItself_IsNotAnOverlapIssue()
    {
        var map = Map(640, Region("region.all", Rect(-320, -320, 320, 320), Rect(-100, -100, 100, 100)));

        var (field, report) = Run(map);

        Assert.Empty(report.Issues);
        Assert.Equal(100, field.CellCount(ContentId.Parse("region.all")));
    }

    [Fact]
    public void TinyRegion_OwnsNoCells_IsAWarning()
    {
        var map = Map(
            640,
            Region("region.big", Rect(-320, -320, 320, 320)),
            Region("region.speck", Rect(10, 10, 20, 20)));

        var (_, report) = Run(map);

        Assert.Contains(report.Issues, i => i.Code == TerrainCodes.RegionOwnsNoCells && i.Severity == Severity.Warning);
        Assert.DoesNotContain(report.Issues, i => i.Code == TerrainCodes.Gap);
        Assert.DoesNotContain(report.Issues, i => i.Code == TerrainCodes.Overlap);
    }

    [Fact]
    public void WorldSizeNotMultipleOfCell_IsAnError_AndFieldIsEmpty()
    {
        var map = Map(650, Region("region.all", Rect(-325, -325, 325, 325)));

        var (field, report) = Run(map);

        Assert.Equal(TerrainCodes.WorldSizeNotCellMultiple, Assert.Single(report.Issues).Code);
        Assert.Equal(0, field.CellsPerSide);
        Assert.True(field.Cells.IsEmpty);
    }

    [Fact]
    public void NonPositiveCellSize_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Run(TwoHalves(), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Run(TwoHalves(), -64));
    }

    [Fact]
    public void RegionNumbering_FollowsSortedIdOrder_NotFileOrder()
    {
        MacroMapDefinition a = TwoHalves();
        var b = new MacroMapDefinition(a.Id, a.Name, a.WorldSizeMeters, a.Regions.Reverse().ToArray());

        var (fieldA, _) = Run(a);
        var (fieldB, _) = Run(b);

        Assert.Equal(new[] { "region.east", "region.west" }, fieldA.Regions.Select(r => r.Value).ToArray());
        Assert.Equal(fieldA.Checksum(), fieldB.Checksum());
        Assert.True(fieldA.Cells.SequenceEqual(fieldB.Cells));
    }

    [Fact]
    public void SameInput_GivesSameChecksum_DifferentInput_Differs()
    {
        var (a, _) = Run(TwoHalves());
        var (b, _) = Run(TwoHalves());
        var (c, _) = Run(Map(
            640,
            Region("region.west", Rect(-320, -320, 64, 320)),
            Region("region.east", Rect(64, -320, 320, 320))));

        Assert.Equal(a.Checksum(), b.Checksum());
        Assert.NotEqual(a.Checksum(), c.Checksum());
    }

    [Fact]
    public void CellSize_ChangesResolution()
    {
        var (coarse, _) = Run(TwoHalves(), 128);
        var (fine, report) = Run(TwoHalves(), 32);

        Assert.Equal(5, coarse.CellsPerSide);
        Assert.Equal(20, fine.CellsPerSide);
        Assert.Empty(report.Issues);
        Assert.Equal(200, fine.CellCount(ContentId.Parse("region.west")));
    }

    [Fact]
    public void ShippedWorld_IsFullyCovered_AndMatchesIndependentOracles()
    {
        var report = new ValidationReport();
        ContentDatabase db = ContentLoader.Load(Path.Combine(RepoRoot(), "content"), report);
        Assert.Empty(report.Issues);

        MacroMapDefinition map = db.MacroMaps[ContentId.Parse("world.main")];
        var rasterReport = new ValidationReport();
        RegionField field = MacroRasterizer.Rasterize(map, rasterReport);

        Assert.Empty(rasterReport.Issues);
        Assert.Equal(320, field.CellsPerSide);
        Assert.Equal(0, field.UnassignedCount());

        // Oracle 1: every region owns about as much area as its polygons enclose (shoelace area). A boundary can
        // misplace at most one cell of width, so the error is bounded by perimeter x cell size.
        long cellsTotal = 0;
        foreach (RegionDefinition region in map.Regions)
        {
            double polygonArea = region.Polygons.Sum(p => p.Area);
            double perimeter = region.Polygons.Sum(p => Perimeter(p));
            double cellArea = (double)field.CellCount(region.Id) * field.CellMeters * field.CellMeters;
            Assert.True(
                Math.Abs(cellArea - polygonArea) <= perimeter * field.CellMeters,
                $"{region.Id}: cells cover {cellArea} m2 but the polygons enclose {polygonArea} m2");
            cellsTotal += field.CellCount(region.Id);
        }

        Assert.Equal(320L * 320L, cellsTotal);

        // Oracle 2 (worked by hand): the land is the square from -8000 m to 8000 m. 8000 / 64 = 125 whole cells on
        // each side of the centre, so land owns 250 x 250 = 62,500 cells and the sea owns 102,400 - 62,500 = 39,900.
        int ocean = field.CellCount(ContentId.Parse("region.outer_sea"));
        Assert.Equal(39_900, ocean);
        int land = map.Regions.Where(r => r.Id.Value != "region.outer_sea").Sum(r => field.CellCount(r.Id));
        Assert.Equal(62_500, land);
    }

    private static double Perimeter(Polygon polygon)
    {
        double total = 0;
        for (int i = 0; i < polygon.Points.Count; i++)
        {
            Point2D a = polygon.Points[i];
            Point2D b = polygon.Points[(i + 1) % polygon.Points.Count];
            total += Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Z - b.Z) * (a.Z - b.Z)));
        }

        return total;
    }

    [Fact]
    public void ShippedWorld_Checksum_IsStableGolden()
    {
        // Golden: a change here means the rasteriser output changed. If intended, bump
        // MacroRasterizer.GeneratorVersion, update this value and note it in the CHANGELOG.
        var report = new ValidationReport();
        ContentDatabase db = ContentLoader.Load(Path.Combine(RepoRoot(), "content"), report);
        RegionField field = MacroRasterizer.Rasterize(db.MacroMaps[ContentId.Parse("world.main")], new ValidationReport());

        Assert.Equal(GoldenShippedChecksum, field.Checksum());
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Realm.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Realm.sln not found above the test binary.");
    }

    // Captured from the first accepted implementation (generator version 1); see ShippedWorld_Checksum_IsStableGolden.
    private const ulong GoldenShippedChecksum = 910542702045882542UL;
}
