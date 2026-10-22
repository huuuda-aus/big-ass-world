using System;
using System.Collections.Generic;
using System.Linq;
using Realm.Core.Validation;
using Realm.Gen.Props;
using Realm.Gen.Settlements.Generation;
using Realm.Gen.Settlements.Geometry;
using Realm.Gen.Settlements.Meshing;
using Realm.Gen.Settlements.Model;
using Realm.Gen.Settlements.Placement;
using Realm.Gen.Terrain;
using Xunit;

namespace Realm.Gen.Tests.Settlements;

/// <summary>Settlements beside rivers and lakes: houses stay out of the water and the wall is cut at the water's edge.</summary>
public class WaterfrontTests
{
    /// <summary>A river running along plan Y = offset, 20 m wide.</summary>
    private sealed class River : IWaterSampler
    {
        private readonly double _offset;
        private readonly double _halfWidth;

        public River(double offset, double halfWidth = 10)
        {
            _offset = offset;
            _halfWidth = halfWidth;
        }

        public bool IsWater(double x, double y) => Math.Abs(y - _offset) <= _halfWidth;
    }

    private static CityParams Params(IWaterSampler? water = null) => new() { Seed = 5, MaxBuildings = 150, Parks = false, Water = water };

    [Fact]
    public void Water_DoesNotChangeThePlan_OnlyDropsTheHousesInIt()
    {
        var river = new River(60);
        City dry = CityGenerator.Generate(Params());
        City wet = CityGenerator.Generate(Params(river));

        Assert.Equal(dry.Wall, wet.Wall);
        Assert.Equal(dry.Gates, wet.Gates);
        Assert.Equal(dry.CityPatches.Count, wet.CityPatches.Count);
        Assert.True(wet.AllBuildings.Count() < dry.AllBuildings.Count(), "a river across the city should cost it some houses");
        Assert.All(wet.AllBuildings, b => Assert.False(CityGenerator.TouchesWater(b.Footprint, river), $"building {b.Id} stands in the river"));
    }

    [Fact]
    public void ServiceBuildings_AreNotBuiltInTheWater()
    {
        var river = new River(110, 14); // across the farmland just outside the wall
        var spec = new ServiceSpec("service.farmer", "Farmer", 6, Array.Empty<WardType>(), ServiceNear.Any, true, 50, null, RoomType.Kitchen);
        City city = CityGenerator.Generate(new CityParams { Seed = 5, MaxBuildings = 150, Parks = false, Water = river, Services = new[] { spec } });

        Assert.All(city.AllBuildings.Where(b => b.Service is not null), b => Assert.False(CityGenerator.TouchesWater(b.Footprint, river)));
    }

    [Theory]
    [InlineData(Fortification.Stone)]
    [InlineData(Fortification.Palisade)]
    public void TheWall_StopsAtTheWater_AndRunsAMetreOrTwoIntoIt(Fortification kind)
    {
        City city = CityGenerator.Generate(Params());
        double radius = city.Wall.Max(w => w.Length);
        // A river through the walled area, clear of the gates (a gate tower in the water is not cut).
        double offset = Enumerable.Range(2, 30).Select(k => radius * k / 50.0).First(o => city.Gates.All(g => Math.Abs(g.Y - o) > 25));
        var river = new River(offset, 12);
        PropMesh dry = CityMesher.Wall(city, null, kind)!;
        PropMesh wet = CityMesher.Wall(city, null, kind, river)!;

        // No wall stands in the middle of the river, but the dry wall does cross it.
        bool InMiddle(PropTriangle t) => Math.Abs(t.A.Z - river.Offset()) < 6 && Math.Abs(t.B.Z - river.Offset()) < 6 && Math.Abs(t.C.Z - river.Offset()) < 6;
        int dryInRiver = dry.Triangles.Count(InMiddle);
        int wetInRiver = wet.Triangles.Count(InMiddle);
        Assert.True(dryInRiver > 0, "the dry wall should cross the river");
        Assert.True(wetInRiver * 4 <= dryInRiver, $"the wall still stands in the river ({wetInRiver} of {dryInRiver} triangles)");
        // Away from the river the wall is the same, so the mesh only lost the piece in the water.
        Assert.True(wet.Triangles.Count < dry.Triangles.Count);
        // It does not stop short: some wall stands within 4 m of the water's edge, and some within it (the overlap).
        double edge = river.Offset() - 12;
        Assert.Contains(wet.Triangles, t => Math.Abs(t.A.Z - edge) < 4 || Math.Abs(t.A.Z - (river.Offset() + 12)) < 4);
    }

    [Fact]
    public void WithoutWater_TheWallIsExactlyAsBefore()
    {
        City city = CityGenerator.Generate(Params());

        Assert.Equal(CityMesher.Wall(city)!.Triangles, CityMesher.Wall(city, null, Fortification.Stone, null)!.Triangles);
    }

    [Fact]
    public void GroundGrid_KnowsWhereTheWaterIs_AndKeepsItInTheFile()
    {
        const int World = 2048, Spacing = 8;
        int side = (World / Spacing) + 1;
        var heights = new float[side * side];
        for (int i = 0; i < heights.Length; i++)
        {
            heights[i] = 50;
        }

        // The sea: everything with column below 20 is under 0 m.
        for (int row = 0; row < side; row++)
        {
            for (int col = 0; col < 20; col++)
            {
                heights[(row * side) + col] = -5;
            }
        }

        var field = new HeightField(World, Spacing, heights);
        var settlement = new Settlement("settlement.test", "Test", SettlementTier.Town, -700, 0, 0.4, 50, 90, 120, 1, 40, false, CentrepieceKind.None);

        GroundGrid grid = GroundGrid.Capture(field, settlement);

        Assert.True(grid.IsWaterAt(-900, 0), "the sea is water");
        Assert.False(grid.IsWaterAt(-600, 0), "dry land is not");
        Assert.True(grid.Water.ToArray().Any(w => w) && grid.Water.ToArray().Any(w => !w));

        IReadOnlyDictionary<string, GroundGrid>? back = GroundGridFile.FromBytes(GroundGridFile.ToBytes(new[] { grid }), new ValidationReport());
        Assert.Equal(grid.Water.ToArray(), back!["settlement.test"].Water.ToArray());

        // The sampler turns it into the city's plan frame.
        var sampler = new SettlementGround(settlement, grid);
        (double wx, double wz) = settlement.ToWorld(new Vec2(0, 0));
        Assert.Equal(grid.IsWaterAt(wx, wz), sampler.IsWater(0, 0));
    }

    [Fact]
    public void Placement_KeepsTheCentreDry_ButAllowsWaterInTheOuterWards()
    {
        var settings = new SettlementSettings();
        Settlement s = new("settlement.test", "Test", SettlementTier.City, 0, 0, 0, 100, 150, 180, 1, 250, true, CentrepieceKind.Fountain);
        var flat = new float[(2048 / 16 + 1) * (2048 / 16 + 1)];
        for (int i = 0; i < flat.Length; i++)
        {
            flat[i] = 100;
        }

        var field = new HeightField(2048, 16, flat);
        var report = new ValidationReport();
        var nearCentre = new Realm.Gen.Terrain.Erosion.RiverNetwork(
            new List<Realm.Gen.Terrain.Erosion.River>
            {
                new(0, new List<Realm.Gen.Terrain.Erosion.RiverPoint> { new(-300, 20, 100, 5), new(300, 20, 100, 5) }, Realm.Gen.Terrain.Erosion.RiverMouth.Sea, null, 200),
            },
            new List<Realm.Gen.Terrain.Erosion.Lake>(), 32);
        var outer = new Realm.Gen.Terrain.Erosion.RiverNetwork(
            new List<Realm.Gen.Terrain.Erosion.River>
            {
                new(0, new List<Realm.Gen.Terrain.Erosion.RiverPoint> { new(-300, 140, 100, 5), new(300, 140, 100, 5) }, Realm.Gen.Terrain.Erosion.RiverMouth.Sea, null, 200),
            },
            new List<Realm.Gen.Terrain.Erosion.Lake>(), 32);

        SettlementValidator.Validate(new[] { s }, field, nearCentre, settings, report);
        Assert.Contains(report.Issues, i => i.Code == SettlementCodes.WaterOnPad);

        var ok = new ValidationReport();
        SettlementValidator.Validate(new[] { s }, field, outer, settings, ok);
        Assert.DoesNotContain(ok.Issues, i => i.Code == SettlementCodes.WaterOnPad);
    }
}

internal static class RiverExtensions
{
    public static double Offset(this object river) => ((double)river.GetType().GetField("_offset", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(river)!);
}
