using System;
using System.Collections.Generic;
using System.Linq;
using Realm.Core.Validation;
using Realm.Gen.Settlements.Generation;
using Realm.Gen.Settlements.Geometry;
using Realm.Gen.Settlements.Model;
using Realm.Gen.Settlements.Placement;
using Realm.Gen.Terrain;
using Realm.Gen.Terrain.Erosion;
using Xunit;

namespace Realm.Gen.Tests.Settlements;

public class SettlementPlacementTests
{
    private const int World = 8192;
    private const int Spacing = 32;
    private const int Side = (World / Spacing) + 1;
    private const double Half = World / 2.0;
    private const float Plateau = 100f;
    private const ulong Seed = 1;

    // A plateau at 100 m with the sea (-50 m) west of x = -3000 and a steep ramp (about 30 degrees) east of x = 3000.
    private static HeightField Plateaus(Func<double, double, float>? extra = null)
    {
        var heights = new float[Side * Side];
        for (int row = 0; row < Side; row++)
        {
            for (int col = 0; col < Side; col++)
            {
                double x = -Half + (col * (double)Spacing);
                double z = -Half + (row * (double)Spacing);
                float h = x < -3000 ? -50f : (x > 3000 ? (float)(Plateau + ((x - 3000) * 0.6)) : Plateau);
                heights[(row * Side) + col] = extra?.Invoke(x, z) ?? h;
            }
        }

        return new HeightField(World, Spacing, heights);
    }

    private static RiverNetwork RiverAlongZ0()
    {
        var points = new List<RiverPoint>();
        for (double x = -3000; x <= 3000; x += 100)
        {
            points.Add(new RiverPoint(x, 0, 100, 10));
        }

        return new RiverNetwork(new List<River> { new(0, points, RiverMouth.Sea, null, 6000) }, new List<Lake>(), 32);
    }

    private static IReadOnlyList<Settlement> Place(HeightField field, RiverNetwork? network, SettlementSettings settings, ValidationReport? report = null, ulong seed = Seed) =>
        SettlementPlacer.Place(field, network, seed, settings, report ?? new ValidationReport());

    private static SettlementSettings Small(int cities, int towns) =>
        new() { Cities = cities, Towns = towns, MountainCities = 0, CityBuildings = 60, TownBuildings = 24 };

    [Fact]
    public void PlacesTheRequestedNumber_CitiesFirst_OnAGenerousPlateau()
    {
        var report = new ValidationReport();

        IReadOnlyList<Settlement> placed = Place(Plateaus(), null, Small(1, 2), report);

        Assert.Equal(3, placed.Count);
        Assert.Equal(new[] { SettlementTier.City, SettlementTier.Town, SettlementTier.Town }, placed.Select(s => s.Tier));
        Assert.Empty(report.Issues);
    }

    [Fact]
    public void SameInputs_SamePlacement_DifferentSeed_Different()
    {
        HeightField field = Plateaus();

        Assert.Equal(Place(field, null, Small(1, 2)), Place(field, null, Small(1, 2)));
        Assert.NotEqual(Place(field, null, Small(1, 2)), Place(field, null, Small(1, 2), seed: 2));
    }

    [Fact]
    public void EverySettlement_StandsOnLand_OnAFlatLevel_InsideTheWorld()
    {
        SettlementSettings settings = Small(1, 2);

        foreach (Settlement s in Place(Plateaus(), null, settings))
        {
            Assert.Equal(Plateau, s.GroundHeightMeters, 1);
            // The whole pad is above sea level and inside the plateau (x between -3000 and 3000).
            Assert.True(s.X - s.PadRadiusMeters > -3000 && s.X + s.PadRadiusMeters < 3000, $"{s.Id} pad leaves the plateau");
            // The pad, its blend and the edge margin all fit inside the world.
            double reach = s.PadRadiusMeters + settings.BlendMeters + settings.EdgeMarginMeters;
            Assert.True(Math.Abs(s.X) + reach <= Half + 1 && Math.Abs(s.Z) + reach <= Half + 1, $"{s.Id} is too near the world edge");
        }
    }

    [Fact]
    public void Settlements_KeepTheirDistance()
    {
        SettlementSettings settings = Small(2, 3);

        IReadOnlyList<Settlement> placed = Place(Plateaus(), null, settings);

        for (int i = 0; i < placed.Count; i++)
        {
            for (int j = i + 1; j < placed.Count; j++)
            {
                Assert.True(placed[i].DistanceTo(placed[j].X, placed[j].Z) >= settings.MinSpacingMeters, $"{placed[i].Id} and {placed[j].Id} are too close");
            }
        }
    }

    [Fact]
    public void Settlements_AvoidTheSteepRamp()
    {
        // Everything east of x = 3000 rises at 0.6; with 60 houses the pad radius is about 120 m, so a site there would have 90 m of relief.
        foreach (Settlement s in Place(Plateaus(), null, Small(1, 2)))
        {
            Assert.True(s.X + s.PadRadiusMeters <= 3000 + 32, $"{s.Id} reaches onto the ramp");
        }
    }

    [Fact]
    public void Rivers_KeepTheirClearance_AndAttractTheFirstCity()
    {
        SettlementSettings settings = Small(1, 0);
        RiverNetwork river = RiverAlongZ0();

        Settlement city = Place(Plateaus(), river, settings).Single();

        double distance = Math.Abs(city.Z); // the river runs along z = 0
        Assert.True(distance >= city.PadRadiusMeters + settings.WaterClearanceMeters - WaterMask.CellMeters, $"pad touches the river ({distance:F0} m away)");
        Assert.True(distance <= city.PadRadiusMeters + settings.WaterClearanceMeters + settings.RiverAttractionMeters + WaterMask.CellMeters,
            $"a city on flat land with a river should settle within reach of it ({distance:F0} m away)");
    }

    [Fact]
    public void TooLittleLand_PlacesWhatFits_AndWarns()
    {
        // Only a 1,200 m square of land: room for one small town at most.
        HeightField tiny = Plateaus((x, z) => Math.Abs(x) < 600 && Math.Abs(z) < 600 ? Plateau : -50f);
        var report = new ValidationReport();

        IReadOnlyList<Settlement> placed = Place(tiny, null, new SettlementSettings { Cities = 0, Towns = 3, TownBuildings = 12, EdgeMarginMeters = 0 }, report);

        Assert.InRange(placed.Count, 0, 1);
        Assert.Equal(SettlementCodes.TooFewPlaced, Assert.Single(report.Issues).Code);
    }

    [Fact]
    public void InvalidSettings_AreRejected()
    {
        foreach (SettlementSettings bad in new[]
        {
            new SettlementSettings { Cities = -1 },
            new SettlementSettings { Cities = 40, Towns = 40 },
            new SettlementSettings { CityBuildings = 2 },
            new SettlementSettings { BlendMeters = 0 },
            new SettlementSettings { CandidateStepMeters = 4 },
            new SettlementSettings { MinHeightMeters = 500, MaxHeightMeters = 100 },
            new SettlementSettings { MaxReliefPerRadius = 0 },
        })
        {
            var report = new ValidationReport();

            Assert.Empty(Place(Plateaus(), null, bad, report));
            Assert.Equal(SettlementCodes.SettingsInvalid, Assert.Single(report.Issues).Code);
        }
    }

    [Fact]
    public void WallRadius_AndPad_MatchTheGeneratedCity()
    {
        SettlementSettings settings = Small(1, 0);
        Settlement s = Place(Plateaus(), null, settings).Single();

        City city = CityGenerator.Generate(s.ToCityParams());

        Assert.Equal(city.Wall.Max(v => v.Length), s.WallRadiusMeters, 1);
        Assert.Equal(s.WallRadiusMeters + settings.PadMarginMeters, s.PadRadiusMeters, 1);
        Assert.True(city.CityPatches.Sum(p => p.Buildings.Count) <= 60);
    }

    [Fact]
    public void ToWorld_RotatesThePlan_AndToLocalUndoesIt()
    {
        var s = new Settlement("settlement.test", "Test", SettlementTier.Town, 1000, -500, Math.PI / 2, 10, 100, 130, 1, 40, false, CentrepieceKind.None);

        (double x, double z) = s.ToWorld(new Vec2(1, 0));

        // A quarter turn takes plan +x to world +z.
        Assert.Equal(1000, x, 9);
        Assert.Equal(-499, z, 9);
        Vec2 back = s.ToLocal(x, z);
        Assert.Equal(1, back.X, 9);
        Assert.Equal(0, back.Y, 9);
    }
}
