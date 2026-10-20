using System;
using System.Collections.Generic;
using System.Linq;
using Realm.Core.Ids;
using Realm.Core.Validation;
using Realm.Data.Content;
using Realm.Gen.Terrain;
using Realm.Gen.Terrain.Erosion;
using Xunit;
using static Realm.Gen.Tests.TerrainTestHelpers;

namespace Realm.Gen.Tests.Erosion;

public class ErosionGeneratorTests
{
    // A 1,024 m world (16 macro cells): rugged land in the west half, sea in the east half. Field 16 m, working grid 32 m.
    private const int FieldSpacing = 16;
    private const int WorkingSpacing = 32;

    private static BiomeDefinition RuggedLand(
        double rainfall = 0.8, double erodibility = 0.6, double maxSlope = 45, double loss = 0.0, double relief = 60) =>
        Biome(
            "biome.land",
            false,
            ("base_height_m", 120),
            ("relief_m", relief),
            ("roughness", 0.5),
            ("rainfall", rainfall),
            ("erodibility", erodibility),
            ("max_slope_deg", maxSlope),
            ("river_loss", loss));

    private static readonly BiomeDefinition Sea = TerrainTestHelpers.Sea("biome.sea", 80, 0, 0.3);

    private static ErosionSettings Settings(Func<ErosionSettings, ErosionSettings>? tweak = null)
    {
        var s = new ErosionSettings
        {
            WorkingSpacingMeters = WorkingSpacing,
            Iterations = 12,
            Strength = 2,
            ThermalIterations = 10,
            RiverThresholdKm2 = 0.004,
            MinLakeAreaKm2 = 0.02,
            BlendRadiusMeters = 256,
        };
        return tweak is null ? s : tweak(s);
    }

    private static (HeightField Source, RegionField Regions, MacroMapDefinition Map, IReadOnlyDictionary<ContentId, BiomeDefinition> Biomes) World(
        BiomeDefinition land, ulong seed = 3, double wavelength = HeightFieldSettings.DefaultFeatureWavelengthMeters)
    {
        MacroMapDefinition map = LandAndSea();
        var biomes = Biomes(land, Sea);
        RegionField regions = Rasterize(map);
        var report = new ValidationReport();
        HeightField? source = HeightFieldGenerator.Generate(
            map, regions, biomes, seed, new HeightFieldSettings { SpacingMeters = FieldSpacing, BlendRadiusMeters = 256, FeatureWavelengthMeters = wavelength }, report);
        Assert.NotNull(source);
        return (source!, regions, map, biomes);
    }

    private static ErodedTerrain Erode(
        BiomeDefinition land, ErosionSettings? settings = null, ValidationReport? report = null, ulong seed = 3, double wavelength = HeightFieldSettings.DefaultFeatureWavelengthMeters)
    {
        var (source, regions, map, biomes) = World(land, seed, wavelength);
        ErodedTerrain? result = ErosionGenerator.Apply(map, regions, biomes, source, settings ?? Settings(), report ?? new ValidationReport());
        Assert.NotNull(result);
        return result!;
    }

    // Short noise wavelength and large relief: slopes well beyond any stable angle used in the tests.
    private const double SteepWavelength = 160;

    private static double MaxSlope(HeightField f)
    {
        double worst = 0;
        for (int z = 0; z < f.VerticesPerSide - 1; z++)
        {
            for (int x = 0; x < f.VerticesPerSide - 1; x++)
            {
                worst = Math.Max(worst, Math.Abs(f.At(x + 1, z) - f.At(x, z)) / f.SpacingMeters);
                worst = Math.Max(worst, Math.Abs(f.At(x, z + 1) - f.At(x, z)) / f.SpacingMeters);
            }
        }

        return worst;
    }

    [Fact]
    public void NoIterations_NoThermal_ReturnsTheInputHeights_OnTerrainWithoutBasins()
    {
        var flat = RuggedLand(relief: 0); // no noise, so no depressions to fill
        var (source, _, _, _) = World(flat);

        ErodedTerrain result = Erode(flat, Settings(s => s with { Iterations = 0, ThermalIterations = 0 }));

        Assert.Equal(source.Heights.ToArray(), result.Field.Heights.ToArray());
        Assert.Equal(0, result.MaxCarveDepthMeters);
    }

    [Fact]
    public void NoErodibility_LeavesTheTerrainAlone_ButStillFindsRivers()
    {
        var flat = RuggedLand(erodibility: 0, relief: 0);
        var (source, _, _, _) = World(flat);

        ErodedTerrain result = Erode(flat, Settings(s => s with { ThermalIterations = 0 }));

        Assert.Equal(source.Heights.ToArray(), result.Field.Heights.ToArray());
        Assert.NotEmpty(result.Network.Rivers);
    }

    [Fact]
    public void Erosion_LowersTheTerrain_AndCarvesValleys()
    {
        var land = RuggedLand(relief: 150);
        var (source, _, _, _) = World(land, wavelength: SteepWavelength);
        // Catchments in this 1 km test world are tiny (carving scales with strength times the square root of the flow), so use a strong setting.
        ErosionSettings noThermal = Settings(s => s with { ThermalIterations = 0, MinLakeAreaKm2 = 0, LakeMinDepthMeters = 2, Strength = 60 });

        // Keep every basin deeper than the lake depth; only shallower dents are filled in, so nothing rises by more than that.
        ErodedTerrain result = Erode(land, noThermal, wavelength: SteepWavelength);

        // Filling shallow dents adds material, so compare with the same run without erodibility (identical fills,
        // no carving): the difference is the erosion.
        ErodedTerrain uncarved = Erode(RuggedLand(erodibility: 0, relief: 150), noThermal, wavelength: SteepWavelength);
        double carved = result.Field.Heights.ToArray().Sum(v => (double)v);
        double fillOnly = uncarved.Field.Heights.ToArray().Sum(v => (double)v);
        Assert.True(carved < fillOnly, $"erosion should remove material: {carved} vs {fillOnly}");
        Assert.True(result.MaxCarveDepthMeters > 5, $"carve {result.MaxCarveDepthMeters}");
        for (int i = 0; i < source.Heights.Length; i++)
        {
            Assert.True(result.Field.Heights[i] <= source.Heights[i] + 2.0f + 1e-3f, $"vertex {i} rose by more than the lake depth");
        }
    }

    [Fact]
    public void SeaFloor_IsNeverTouched()
    {
        var land = RuggedLand();
        var (source, _, _, _) = World(land);
        ErodedTerrain result = Erode(land);

        // Well inside the sea (east of x = 256 m, past the blend) nothing may change.
        for (int vz = 0; vz < source.VerticesPerSide; vz++)
        {
            for (int vx = 0; vx < source.VerticesPerSide; vx++)
            {
                double x = -512 + (vx * FieldSpacing);
                if (x >= 300)
                {
                    Assert.Equal(source.At(vx, vz), result.Field.At(vx, vz));
                }
            }
        }
    }

    [Fact]
    public void SameInputs_GiveTheSameChecksum()
    {
        ulong first = Erode(RuggedLand()).Checksum();

        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(first, Erode(RuggedLand()).Checksum());
        }
    }

    [Fact]
    public void DifferentTerrainSeeds_GiveDifferentErodedTerrain()
    {
        Assert.NotEqual(Erode(RuggedLand(), seed: 1).Checksum(), Erode(RuggedLand(), seed: 2).Checksum());
    }

    [Fact]
    public void NoRainfall_MeansNoRiversAndNoCarving()
    {
        ErodedTerrain dry = Erode(RuggedLand(rainfall: 0), Settings(s => s with { ThermalIterations = 0 }));

        Assert.Empty(dry.Network.Rivers);
        Assert.Equal(0, dry.MaxCarveDepthMeters);
    }

    [Fact]
    public void MoreRainfall_GivesMoreRiver()
    {
        double Length(double rainfall) => Erode(RuggedLand(rainfall: rainfall)).Network.Rivers.Sum(r => r.LengthMeters);

        Assert.True(Length(1.0) > Length(0.3), "wetter land should have more river");
    }

    [Fact]
    public void HigherErodibility_CarvesDeeper()
    {
        double Carve(double erodibility) => Erode(RuggedLand(erodibility: erodibility), Settings(s => s with { ThermalIterations = 0 })).MaxCarveDepthMeters;

        Assert.True(Carve(1.0) > Carve(0.2), "softer ground should carve deeper");
    }

    [Fact]
    public void LowerStableSlope_GivesGentlerTerrain()
    {
        double Steepest(double degrees) => MaxSlope(
            Erode(RuggedLand(maxSlope: degrees, relief: 150), Settings(s => s with { ThermalIterations = 60 }), wavelength: SteepWavelength).Field);

        Assert.True(Steepest(15) < Steepest(80), "a low stable angle should flatten the steepest slopes");
    }

    [Fact]
    public void ThermalRelaxation_ReducesSlopesBeyondTheLimit()
    {
        var land = RuggedLand(maxSlope: 20, erodibility: 0, relief: 150);

        double without = MaxSlope(Erode(land, Settings(s => s with { ThermalIterations = 0 }), wavelength: SteepWavelength).Field);
        double with = MaxSlope(Erode(land, Settings(s => s with { ThermalIterations = 200 }), wavelength: SteepWavelength).Field);

        Assert.True(with < without, $"with {with} should be below without {without}");
    }

    [Fact]
    public void RiverLoss_CanDryRiversUp()
    {
        ErodedTerrain wet = Erode(RuggedLand(loss: 0));
        ErodedTerrain leaky = Erode(RuggedLand(loss: 1));

        // Water loss shortens rivers and weakens them. (Properties blend across the coast, so a few short rivers can still
        // survive right at the shore; they are not all "dried".)
        Assert.DoesNotContain(wet.Network.Rivers, r => r.Mouth == RiverMouth.Dried);
        Assert.True(leaky.Network.Rivers.Sum(r => r.LengthMeters) < wet.Network.Rivers.Sum(r => r.LengthMeters) / 2);
        Assert.True(leaky.Network.Rivers.Count == 0 || leaky.Network.Rivers.Max(r => r.MaxFlowKm2) < wet.Network.Rivers.Max(r => r.MaxFlowKm2));
    }

    [Fact]
    public void LargeLakesAreKept_AndSmallBasinsAreFilled()
    {
        // Keep every basin, then only big ones: the number of lakes must not increase and the filled terrain is higher.
        ErodedTerrain all = Erode(RuggedLand(), Settings(s => s with { MinLakeAreaKm2 = 0, Iterations = 1, ThermalIterations = 0 }));
        ErodedTerrain big = Erode(RuggedLand(), Settings(s => s with { MinLakeAreaKm2 = 0.5, Iterations = 1, ThermalIterations = 0 }));

        Assert.True(big.Network.Lakes.Count <= all.Network.Lakes.Count);
        Assert.Empty(big.Network.Lakes); // a 0.5 km2 lake would not fit in this 1 km2 world's land half
        Assert.True(big.Field.Heights.ToArray().Sum(v => (double)v) >= all.Field.Heights.ToArray().Sum(v => (double)v) - 1e-3);
    }

    [Fact]
    public void RiversAndLakes_PassTheirValidators_AndLakesSitAboveSeaLevel()
    {
        var report = new ValidationReport();
        ErodedTerrain result = Erode(RuggedLand(), Settings(s => s with { MinLakeAreaKm2 = 0 }), report);

        Assert.DoesNotContain(report.Issues, i => i.Code == TerrainCodes.RiverUphill || i.Code == TerrainCodes.RiverDeadEnd);
        Assert.NotEmpty(result.Network.Rivers);
        Assert.All(result.Network.Lakes, l => Assert.True(l.SurfaceHeight > 0));
        Assert.NotEqual(RiverMouth.Confluence, result.Network.Rivers[0].Mouth); // the first river traced is never a tributary
    }

    [Fact]
    public void RiverPoints_StayInsideTheWorld()
    {
        ErodedTerrain result = Erode(RuggedLand());

        foreach (River river in result.Network.Rivers)
        {
            Assert.All(river.Points, p => Assert.InRange(p.X, -512.0, 512.0));
            Assert.All(river.Points, p => Assert.InRange(p.Z, -512.0, 512.0));
        }
    }

    [Fact]
    public void InvalidSettings_AreRejected()
    {
        var (source, regions, map, biomes) = World(RuggedLand());
        foreach (ErosionSettings bad in new[]
        {
            Settings(s => s with { WorkingSpacingMeters = 24 }),    // not a multiple of the 16 m field spacing
            Settings(s => s with { WorkingSpacingMeters = 48 }),    // does not divide 1024
            Settings(s => s with { WorkingSpacingMeters = 8 }),     // finer than the field
            Settings(s => s with { Iterations = -1 }),
            Settings(s => s with { Iterations = 1001 }),
            Settings(s => s with { Strength = -1 }),
            Settings(s => s with { Strength = double.NaN }),
            Settings(s => s with { BlendRadiusMeters = -1 }),
            Settings(s => s with { ThermalIterations = -1 }),
            Settings(s => s with { RiverThresholdKm2 = 0 }),
            Settings(s => s with { LakeMinDepthMeters = -1 }),
            Settings(s => s with { LakeMinDepthMeters = 0 }),
            Settings(s => s with { MinLakeAreaKm2 = -1 }),
            Settings(s => s with { GorgeWallSlope = 0 }),
            Settings(s => s with { GorgeFullFlowKm2 = 0 }),
            Settings(s => s with { RiverBaseWidthMeters = -1 }),
            Settings(s => s with { GorgeFadeHeightMeters = double.NaN }),
        })
        {
            var report = new ValidationReport();
            ErodedTerrain? result = ErosionGenerator.Apply(map, regions, biomes, source, bad, report);

            Assert.Null(result);
            Assert.Equal(TerrainCodes.ErosionSettingsInvalid, Assert.Single(report.Issues).Code);
        }
    }

    [Fact]
    public void RegionFieldWithGaps_OrWrongWorldSize_CannotBeUsed()
    {
        var (source, _, map, biomes) = World(RuggedLand());
        var halfMap = new MacroMapDefinition(map.Id, map.Name, map.WorldSizeMeters, new[] { map.Regions[0] });
        RegionField gappy = MacroRasterizer.Rasterize(halfMap, new ValidationReport(), Cell);

        var report = new ValidationReport();
        Assert.Null(ErosionGenerator.Apply(halfMap, gappy, biomes, source, null, report));
        Assert.Equal(TerrainCodes.RegionFieldIncomplete, Assert.Single(report.Issues).Code);
    }

    [Fact]
    public void MissingBiome_OrMissingErosionProperties_AreReported()
    {
        var (source, regions, map, biomes) = World(RuggedLand());

        var noBiome = new ValidationReport();
        Assert.Null(ErosionGenerator.Apply(map, regions, Biomes(Sea), source, Settings(), noBiome));
        Assert.Equal(TerrainCodes.BiomeMissing, Assert.Single(noBiome.Issues).Code);

        var bareLand = Biome("biome.land", false, ("base_height_m", 100), ("relief_m", 10), ("roughness", 0.5));
        var noProps = new ValidationReport();
        Assert.Null(ErosionGenerator.Apply(map, regions, Biomes(bareLand, Sea), source, Settings(), noProps));
        Assert.Equal(4, noProps.Issues.Count(i => i.Code == TerrainCodes.BiomeMissingProperty));
    }

    [Fact]
    public void Apply_RejectsNullArguments()
    {
        var (source, regions, map, biomes) = World(RuggedLand());
        var report = new ValidationReport();

        Assert.Throws<ArgumentNullException>(() => ErosionGenerator.Apply(null!, regions, biomes, source, null, report));
        Assert.Throws<ArgumentNullException>(() => ErosionGenerator.Apply(map, null!, biomes, source, null, report));
        Assert.Throws<ArgumentNullException>(() => ErosionGenerator.Apply(map, regions, null!, source, null, report));
        Assert.Throws<ArgumentNullException>(() => ErosionGenerator.Apply(map, regions, biomes, null!, null, report));
        Assert.Throws<ArgumentNullException>(() => ErosionGenerator.Apply(map, regions, biomes, source, null, null!));
    }

    [Fact]
    public void ErosionDoesNotModifyTheInputField()
    {
        var land = RuggedLand();
        var (source, regions, map, biomes) = World(land);
        float[] before = source.Heights.ToArray();

        ErosionGenerator.Apply(map, regions, biomes, source, Settings(), new ValidationReport());

        Assert.Equal(before, source.Heights.ToArray());
    }

    [Fact]
    public void DefaultSettings_AreValid_ForTheShippedSizes()
    {
        Assert.True(new ErosionSettings().TryValidate(20480, 8, out string? problem), problem);
        Assert.True(new ErosionSettings().TryValidate(20480, 32, out problem), problem);
        Assert.False(new ErosionSettings().TryValidate(20480, 64, out _)); // field coarser than the 32 m working grid
    }
}
