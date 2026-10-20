using System;
using System.Linq;
using Realm.Core.Validation;
using Realm.Data.Content;
using Realm.Gen.Terrain;
using Xunit;
using static Realm.Gen.Tests.TerrainTestHelpers;

namespace Realm.Gen.Tests;

public class LandmarkTests
{
    private const int World = 8192;
    private const double Half = World / 2.0;

    // Land in the west half, sea in the east half; border at x = 0.
    private static MacroMapDefinition Map() => new(
        Realm.Core.Ids.ContentId.Parse("world.landmarks"),
        "Landmarks",
        World,
        new[]
        {
            Region("region.land", "biome.land", Rect(-Half, -Half, 0, Half)),
            Region("region.sea", "biome.sea", Rect(0, -Half, Half, Half)),
        });

    private static BiomeDefinition LandWith(params (string, double)[] extra) =>
        Biome("biome.land", false, new (string, double)[] { ("base_height_m", 100), ("relief_m", 0), ("roughness", 0.5) }.Concat(extra).ToArray());

    private static readonly (string, double)[] Hills =
        { ("hill_density", 50), ("hill_height_m", 100), ("hill_radius_m", 400) };

    private static (RegionField Regions, BiomeTerrainParameters?[] Parameters) Setup(BiomeDefinition land)
    {
        RegionField regions = Rasterize(Map());
        var report = new ValidationReport();
        var perRegion = regions.Regions
            .Select(id => id.Value == "region.land"
                ? BiomeTerrainParameters.TryFrom(land, report)
                : BiomeTerrainParameters.TryFrom(Sea("biome.sea", 50, 0, 0.5), report))
            .ToArray();
        Assert.Empty(report.Issues);
        return (regions, perRegion);
    }

    [Fact]
    public void Count_MatchesTheDensity_WithinWhatTheContainmentRuleAllows()
    {
        var (regions, parameters) = Setup(LandWith(Hills));

        var landmarks = LandmarkPlacer.Place(regions, parameters, 1);

        // 50 per 100 km2 over 33.5 km2 of land is about 17; landmarks whose footprint touches the region edge are dropped (about 35 percent).
        Assert.InRange(landmarks.Count, 5, 20);
        Assert.All(landmarks, l => Assert.Equal(LandmarkKind.Hill, l.Kind));
    }

    [Fact]
    public void NoDensity_NoLandmarks()
    {
        var (regions, parameters) = Setup(LandWith());

        Assert.Empty(LandmarkPlacer.Place(regions, parameters, 1));
    }

    [Fact]
    public void Landmarks_StayOnLand_AndTheirWholeFootprintInsideTheRegion()
    {
        var (regions, parameters) = Setup(LandWith(Hills));

        foreach (Landmark l in LandmarkPlacer.Place(regions, parameters, 3))
        {
            Assert.True(l.X < 0, $"landmark at x={l.X} is in the sea half");
            // Independent check at 1.3 x radius on 12 compass points.
            for (int i = 0; i < 12; i++)
            {
                double a = i * Math.PI / 6;
                double px = l.X + (1.3 * l.RadiusMeters * Math.Cos(a));
                double pz = l.Z + (1.3 * l.RadiusMeters * Math.Sin(a));
                Assert.Equal("region.land", regions.RegionAt(px, pz).Value);
            }
        }
    }

    [Fact]
    public void SameSeed_SameLandmarks_DifferentSeed_Different()
    {
        var (regions, parameters) = Setup(LandWith(Hills));

        Assert.Equal(LandmarkPlacer.Place(regions, parameters, 5), LandmarkPlacer.Place(regions, parameters, 5));
        Assert.NotEqual(LandmarkPlacer.Place(regions, parameters, 5), LandmarkPlacer.Place(regions, parameters, 6));
    }

    [Fact]
    public void SizesVaryByAtMostThirtyPercent()
    {
        var (regions, parameters) = Setup(LandWith(Hills));

        foreach (Landmark l in LandmarkPlacer.Place(regions, parameters, 2))
        {
            Assert.InRange(l.HeightMeters, 70 - 1e-6, 130 + 1e-6);
            Assert.InRange(l.RadiusMeters, 280 - 1e-6, 520 + 1e-6);
        }
    }

    [Fact]
    public void Hill_PeaksNearItsHeight_AndVanishesBeyondTwoRadii()
    {
        var noise = new GradientNoise(Realm.Core.Random.Rng.ForKey(1, "test"));
        var hill = new Landmark(LandmarkKind.Hill, 0, 0, 100, 400);

        Assert.InRange(LandmarkShaper.DeltaAt(hill, noise, 0, 0), 80, 125); // 100 m x (1 +- 12 percent bump), outline warp may offset the centre slightly
        Assert.Equal(0, LandmarkShaper.DeltaAt(hill, noise, 800, 0));
        Assert.Equal(0, LandmarkShaper.DeltaAt(hill, noise, 0, -801));
    }

    [Fact]
    public void Mountain_NeverDips_AndReachesAtLeastItsBase()
    {
        var noise = new GradientNoise(Realm.Core.Random.Rng.ForKey(1, "test"));
        var mountain = new Landmark(LandmarkKind.Mountain, 0, 0, 500, 1000);
        double max = 0;
        for (double x = -1000; x <= 1000; x += 20)
        {
            for (double z = -1000; z <= 1000; z += 20)
            {
                double d = LandmarkShaper.DeltaAt(mountain, noise, x, z);
                Assert.True(d >= 0 && d <= 500 + 1e-6, $"delta {d} at ({x}, {z})");
                max = Math.Max(max, d);
            }
        }

        Assert.True(max > 0.3 * 500, $"peak {max} should exceed the 30 percent base share");
    }

    [Fact]
    public void Basin_OnlyEverLowers_AndHasAFlatBottom()
    {
        var noise = new GradientNoise(Realm.Core.Random.Rng.ForKey(1, "test"));
        var basin = new Landmark(LandmarkKind.Basin, 0, 0, 30, 500);

        for (double x = -1000; x <= 1000; x += 25)
        {
            Assert.InRange(LandmarkShaper.DeltaAt(basin, noise, x, 0), -30 - 1e-9, 0);
        }

        // Within the inner part of the footprint the depth is the full 30 m.
        Assert.Equal(-30, LandmarkShaper.DeltaAt(basin, noise, 40, 40), 6);
    }

    [Fact]
    public void GeneratedField_RaisesTheGroundAtEachHill_ByTheShapersDelta()
    {
        BiomeDefinition land = LandWith(Hills);
        var (regions, parameters) = Setup(land);
        var map = Map();
        var biomes = Biomes(land, Sea("biome.sea", 50, 0, 0.5));
        var settings = new HeightFieldSettings { SpacingMeters = 16, BlendRadiusMeters = PinnedBlendMeters, WarpMeters = 0 };
        HeightField withHills = Generate(map, biomes, new ValidationReport(), 1, settings)!;
        HeightField flat = Generate(map, Biomes(LandWith(), Sea("biome.sea", 50, 0, 0.5)), new ValidationReport(), 1, settings)!;

        var shapeNoise = new GradientNoise(Realm.Core.Random.Rng.ForKey(1, "terrain", "landmark-shape"));
        Landmark hill = LandmarkPlacer.Place(regions, parameters, 1).First();
        double x = Math.Round(hill.X / 16) * 16;
        double z = Math.Round(hill.Z / 16) * 16;

        double raised = withHills.Sample(x, z) - flat.Sample(x, z);
        Assert.True(raised > 20, $"hill raised the ground only {raised} m");
        Assert.True(Math.Abs(raised - LandmarkShaper.DeltaAt(hill, shapeNoise, x, z)) < 5, "other landmarks may overlap, but the shaper's own delta should dominate");
    }

    [Fact]
    public void Terrace_AppliesTheStepFormula_ToTheUnterracedHeights()
    {
        HeightField Make(double terrace)
        {
            BiomeDefinition land = Biome("biome.land", false, ("base_height_m", 200), ("relief_m", 120), ("roughness", 0.5), ("terrace", terrace));
            var settings = new HeightFieldSettings { SpacingMeters = 16, BlendRadiusMeters = PinnedBlendMeters, FeatureWavelengthMeters = 600, TerraceStepMeters = 30 };
            return Generate(Map(), Biomes(land, Sea("biome.sea", 50, 0, 0.5)), new ValidationReport(), 1, settings)!;
        }

        HeightField plain = Make(0);
        HeightField terraced = Make(1);
        int onTread = 0;
        int total = 0;
        for (double x = -3488; x <= -800; x += 16) // on vertices: sampling between them would interpolate the steps away
        {
            for (double z = -3488; z <= 3488; z += 16)
            {
                // Independent form of the formula: flat tread for the outer 37.5 percent of a step at each end, a riser between.
                double u = plain.Sample(x, z) / 30;
                double fraction = u - Math.Floor(u);
                double expected = (Math.Floor(u) + Math.Clamp(((fraction - 0.5) * 4) + 0.5, 0, 1)) * 30;
                Assert.True(Math.Abs(terraced.Sample(x, z) - expected) < 0.05, $"({x}, {z}): {terraced.Sample(x, z)} vs {expected}");
                onTread += (fraction < 0.375 || fraction > 0.625) ? 1 : 0;
                total++;
            }
        }

        Assert.True(onTread > total / 3, "the test heights should include plenty of tread, or the check is vacuous");
    }

    [Fact]
    public void Ridge_MakesCrestsSharperThanSmoothNoise()
    {
        double Sharpness(double ridge)
        {
            BiomeDefinition land = Biome("biome.land", false, ("base_height_m", 300), ("relief_m", 400), ("roughness", 0.6), ("ridge", ridge));
            var settings = new HeightFieldSettings { SpacingMeters = 16, BlendRadiusMeters = PinnedBlendMeters, FeatureWavelengthMeters = 800 };
            HeightField f = Generate(Map(), Biomes(land, Sea("biome.sea", 50, 0, 0.5)), new ValidationReport(), 1, settings)!;
            double sum = 0;
            int count = 0;
            for (double x = -3500; x <= -800; x += 16)
            {
                for (double z = -3500; z <= 3500; z += 16)
                {
                    // Absolute second difference: large at crests and kinks.
                    sum += Math.Abs(f.Sample(x - 16, z) - (2 * f.Sample(x, z)) + f.Sample(x + 16, z));
                    count++;
                }
            }

            return sum / count;
        }

        Assert.True(Sharpness(1.0) > Sharpness(0.0) * 1.2, "ridged terrain should be more sharply curved than smooth terrain");
    }
}
