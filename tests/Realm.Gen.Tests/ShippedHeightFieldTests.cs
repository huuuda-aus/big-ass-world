using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Realm.Core.Ids;
using Realm.Core.Validation;
using Realm.Data.Content;
using Realm.Gen.Terrain;
using Xunit;

namespace Realm.Gen.Tests;

public class ShippedHeightFieldTests
{
    private const int TestSpacing = 128;

    // Interior means: every macro cell within this many cells (8 x 64 m = 512 m) belongs to the same region, which is
    // beyond the blend distance (384 m by default), so the biome values apply unmixed.
    private const int InteriorRadiusCells = 8;

    private sealed record Loaded(ContentDatabase Db, MacroMapDefinition Map, RegionField Regions, HeightField Field);

    private static Loaded Load(ulong seed, ValidationReport report)
    {
        ContentDatabase db = ContentLoader.Load(Path.Combine(RepoRoot(), "content"), report);
        MacroMapDefinition map = db.MacroMaps[ContentId.Parse("world.main")];
        RegionField regions = MacroRasterizer.Rasterize(map, report);
        HeightField? field = HeightFieldGenerator.Generate(
            map, regions, db.Biomes, seed, new HeightFieldSettings { SpacingMeters = TestSpacing }, report);
        Assert.NotNull(field);
        return new Loaded(db, map, regions, field!);
    }

    // The height range a biome allows on its own: base +- relief (ocean: minus the water depth).
    private static (double Min, double Max) Range(BiomeDefinition biome)
    {
        BiomeTerrainParameters p = BiomeTerrainParameters.TryFrom(biome, new ValidationReport())!;
        return (p.BaseHeightMeters - p.ReliefMeters, p.BaseHeightMeters + p.ReliefMeters);
    }

    private static bool IsInterior(RegionField regions, int cellX, int cellZ)
    {
        if (cellX < InteriorRadiusCells || cellZ < InteriorRadiusCells
            || cellX >= regions.CellsPerSide - InteriorRadiusCells || cellZ >= regions.CellsPerSide - InteriorRadiusCells)
        {
            return false;
        }

        ushort here = regions.CellValue(cellX, cellZ);
        for (int dz = -InteriorRadiusCells; dz <= InteriorRadiusCells; dz++)
        {
            for (int dx = -InteriorRadiusCells; dx <= InteriorRadiusCells; dx++)
            {
                if (regions.CellValue(cellX + dx, cellZ + dz) != here)
                {
                    return false;
                }
            }
        }

        return true;
    }

    [Fact]
    public void ShippedWorld_GeneratesWithoutIssues()
    {
        var report = new ValidationReport();
        Loaded world = Load(1, report);

        Assert.Empty(report.Issues);
        Assert.Equal((20480 / TestSpacing) + 1, world.Field.VerticesPerSide);
    }

    [Fact]
    public void InteriorHeights_StayWithinEachRegionsBiomeRange_AndEveryRegionHasInterior()
    {
        var report = new ValidationReport();
        Loaded w = Load(1, report);
        double half = w.Field.WorldSizeMeters / 2.0;
        var checkedPerRegion = new Dictionary<ContentId, int>();

        for (int vz = 0; vz < w.Field.VerticesPerSide; vz++)
        {
            for (int vx = 0; vx < w.Field.VerticesPerSide; vx++)
            {
                double x = -half + (vx * (double)TestSpacing);
                double z = -half + (vz * (double)TestSpacing);
                int cellX = (int)Math.Floor((x + half) / w.Regions.CellMeters);
                int cellZ = (int)Math.Floor((z + half) / w.Regions.CellMeters);
                if (cellX >= w.Regions.CellsPerSide || cellZ >= w.Regions.CellsPerSide || !IsInterior(w.Regions, cellX, cellZ))
                {
                    continue;
                }

                ContentId region = w.Regions.RegionAtCell(cellX, cellZ);
                BiomeDefinition biome = w.Db.Biomes[w.Db.Regions[region].Biome];
                (double min, double max) = Range(biome);
                double h = w.Field.At(vx, vz);
                Assert.True(h >= min - 1e-3 && h <= max + 1e-3, $"{region} at ({x}, {z}): {h} m is outside {min} to {max} m");
                checkedPerRegion[region] = checkedPerRegion.GetValueOrDefault(region) + 1;
            }
        }

        foreach (RegionDefinition region in w.Map.Regions)
        {
            Assert.True(checkedPerRegion.GetValueOrDefault(region.Id) > 0, $"{region.Id} has no interior vertex to test");
        }
    }

    [Fact]
    public void WholeWorld_StaysInsideTheExtremesOfTheBiomes()
    {
        var report = new ValidationReport();
        Loaded w = Load(1, report);
        var ranges = w.Map.Regions.Select(r => Range(w.Db.Biomes[r.Biome])).ToList();

        Assert.True(w.Field.MinHeight >= ranges.Min(r => r.Min) - 1e-3, $"min {w.Field.MinHeight}");
        Assert.True(w.Field.MaxHeight <= ranges.Max(r => r.Max) + 1e-3, $"max {w.Field.MaxHeight}");
        Assert.True(w.Field.MinHeight < 0 && w.Field.MaxHeight > 0, "the world should span sea floor to land");
    }

    [Fact]
    public void Coast_RisesFromSeaFloorToLand_WithoutASeam()
    {
        var report = new ValidationReport();
        Loaded w = Load(1, report);

        // Walk north to south across the north coast at x = 2000 m (Ashen Highlands behind the sea).
        double previous = w.Field.Sample(2000, -9500);
        double worstStep = 0;
        for (double z = -9500 + TestSpacing; z <= -6500; z += TestSpacing)
        {
            double h = w.Field.Sample(2000, z);
            worstStep = Math.Max(worstStep, Math.Abs(h - previous));
            previous = h;
        }

        Assert.True(previous > 0, "should end on land");
        Assert.True(worstStep / TestSpacing <= HeightFieldValidator.DefaultMaxBorderSlope, $"worst slope {worstStep / TestSpacing}");
    }

    [Fact]
    public void DifferentSeeds_GiveDifferentWorlds_SameSeedTheSame()
    {
        ulong a = Load(1, new ValidationReport()).Field.Checksum();

        Assert.Equal(a, Load(1, new ValidationReport()).Field.Checksum());
        Assert.NotEqual(a, Load(2, new ValidationReport()).Field.Checksum());
    }

    [Fact]
    public void ShippedWorld_Checksum_IsStableGolden()
    {
        // Golden: a change means the height-field output changed. If intended, bump
        // HeightFieldGenerator.GeneratorVersion, update this value and note it in the CHANGELOG.
        Assert.Equal(GoldenChecksum, Load(1, new ValidationReport()).Field.Checksum());
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

    // Captured from the accepted implementation (generator version 1, spacing 128 m, seed 1, six-region sample world).
    private const ulong GoldenChecksum = 10432179646867385957UL;
}
