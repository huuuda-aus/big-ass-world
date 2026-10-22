using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Realm.Gen.Props;
using Realm.Gen.Settlements.Generation;
using Realm.Gen.Settlements.Geometry;
using Realm.Gen.Settlements.Meshing;
using Realm.Gen.Settlements.Model;
using Realm.Gen.Settlements.Placement;
using Xunit;

namespace Realm.Gen.Tests.Settlements;

/// <summary>Rich and slum buildings, thick walls and slabs, and the house caps.</summary>
public class BuildingStyleTests
{
    private static readonly City Big = CityGenerator.Generate(new CityParams { Seed = 4, MaxBuildings = 250 });

    private static IEnumerable<Building> Of(BuildingStyle style) => Big.AllBuildings.Where(b => b.Style == style);

    [Fact]
    public void Style_FollowsTheWard()
    {
        Assert.All(Big.AllBuildings, b => Assert.Equal(b.Ward is WardType.Slum or WardType.Outskirts ? BuildingStyle.Slum : BuildingStyle.Rich, b.Style));
        Assert.NotEmpty(Of(BuildingStyle.Slum));
        Assert.NotEmpty(Of(BuildingStyle.Rich));
    }

    [Fact]
    public void SlumDwellings_AreSmall_AndConsiderablySmallerThanRichHouses()
    {
        double slumMax = Of(BuildingStyle.Slum).Max(b => b.Footprint.Area);
        double slumAverage = Of(BuildingStyle.Slum).Average(b => b.Footprint.Area);
        double richAverage = Of(BuildingStyle.Rich).Average(b => b.Footprint.Area);

        Assert.True(slumMax <= (4.2 + 1.6) * (3.6 + 1.6) + 1e-6, $"a slum dwelling is {slumMax:F0} m2");
        Assert.True(slumAverage * 2 < richAverage, $"slum average {slumAverage:F0} m2 is not far below rich average {richAverage:F0} m2");
        Assert.All(Of(BuildingStyle.Slum), b => Assert.Equal(1, InteriorGenerator.Generate(b, Big.Seed).Floors.Count));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void RichWards_HaveASecondRow_BehindTheFirst(int seed)
    {
        City city = CityGenerator.Generate(new CityParams { Seed = seed, CityPatchCount = 12, Parks = false });

        // A house in the first row stands within about 4.5 m (street) + 10 m (row) of the ward's edge; the second row is further in.
        bool Deep(Patch patch, Building b)
        {
            Vec2 c = b.Footprint.Centroid;
            double nearest = Enumerable.Range(0, patch.Shape.Count).Min(i => -Vec2.Dot(patch.Shape.OutwardNormal(i), c - patch.Shape.Points[i]));
            return nearest > 17;
        }

        var rich = city.CityPatches.Where(p => p.Ward is WardType.Merchant or WardType.Craftsmen).ToList();
        Assert.NotEmpty(rich);
        Assert.Contains(rich, p => p.Buildings.Any(b => Deep(p, b)));
    }

    [Fact]
    public void WallThickness_DependsOnTheMaterial_SlumsThin_RichThick()
    {
        Building rich = Of(BuildingStyle.Rich).First(), slum = Of(BuildingStyle.Slum).First();

        Assert.Equal(0.30, InteriorGenerator.Generate(rich, Big.Seed).ExteriorWallThickness);
        Assert.Equal(0.15, InteriorGenerator.Generate(slum, Big.Seed).ExteriorWallThickness);
        // The rooms start at the inner face of the wall: the thinner the wall, the bigger the room.
        double roomRich = InteriorGenerator.Generate(rich, Big.Seed).Rooms.Sum(r => r.Shape.Area) / rich.Footprint.Area;
        double roomSlum = InteriorGenerator.Generate(slum, Big.Seed).Rooms.Sum(r => r.Shape.Area) / slum.Footprint.Area;
        Assert.True(roomSlum > 0.6 && roomRich > 0.6);
    }

    [Fact]
    public void SlumWalls_AreMud_AndRoofsDark_NoHalfTimbering()
    {
        Building slum = Of(BuildingStyle.Slum).First();
        Interior it = InteriorGenerator.Generate(slum, Big.Seed);

        PropMesh far = BuildingMesher.Far(slum, it);

        Assert.Contains(far.Triangles, t => t.Colour == SettlementPalette.Mud(slum.Id));
        Assert.Contains(far.Triangles, t => t.Colour == SettlementPalette.SlumRoof);
        Assert.DoesNotContain(far.Triangles, t => t.Colour == SettlementPalette.Beam);
        Building rich = Of(BuildingStyle.Rich).First(b => b.Ward != WardType.Cathedral);
        Assert.Contains(BuildingMesher.Far(rich, InteriorGenerator.Generate(rich, Big.Seed)).Triangles, t => t.Colour == SettlementPalette.Beam);
    }

    [Fact]
    public void FloorsAndCeilings_AreAtLeast30cmThick()
    {
        foreach (Building b in Big.AllBuildings.Where(b => b.Ward != WardType.Cathedral).Take(60))
        {
            Interior it = InteriorGenerator.Generate(b, Big.Seed);
            PropMesh collision = BuildingMesher.Near(b, it).Collision;
            for (int boundary = 1; boundary <= it.Floors.Count; boundary++)
            {
                double ceilingY = (boundary * it.FloorHeight) + 0.01 - InteriorGenerator.SlabThickness;
                // The ceiling's underside faces down at exactly 30 cm below the top of the slab.
                Assert.Contains(collision.Triangles, t => Normal(t).Y < -0.99f && new[] { t.A, t.B, t.C }.All(v => MathF.Abs(v.Y - (float)ceilingY) < 1e-4f));
            }
        }

        Assert.True(InteriorGenerator.SlabThickness >= 0.30);
    }

    [Fact]
    public void WallsBetweenRooms_AreSolid_AtLeast15cmThick()
    {
        (Building b, Interior it) = Big.AllBuildings
            .Select(b => (b, InteriorGenerator.Generate(b, Big.Seed))).First(x => x.Item2.Rooms.Count >= 3);
        BuildingMeshes near = BuildingMesher.Near(b, it);

        // Inner-coloured faces of an interior wall come in pairs 15 cm apart (the two sides of one wall box), facing opposite ways.
        var faces = near.Collision.Triangles.Where(t => t.Colour == SettlementPalette.Inner && MathF.Abs(Normal(t).Y) < 0.01f).ToList();
        int pairs = 0;
        foreach (PropTriangle t in faces)
        {
            Vector3 n = Normal(t);
            if (faces.Any(o => Vector3.Dot(Normal(o), n) < -0.99f && MathF.Abs(Vector3.Dot(Centroid(o) - Centroid(t), n) + (float)it.InteriorWallThickness) < 1e-3f))
            {
                pairs++;
            }
        }

        Assert.True(pairs > 0, "no interior wall has two faces one wall thickness apart");
        Assert.True(it.InteriorWallThickness >= 0.15);
    }

    [Theory]
    [InlineData(SettlementTier.City, 250)]
    [InlineData(SettlementTier.Town, 50)]
    public void SettlementSettings_CapTheHouseCounts_AndGeneratedCitiesStayWithin(SettlementTier tier, int cap)
    {
        var settings = new SettlementSettings();
        Assert.Equal(cap, tier == SettlementTier.City ? settings.CityBuildings : settings.TownBuildings);

        foreach (int seed in new[] { 1, 2, 3 })
        {
            City city = CityGenerator.Generate(new CityParams { Seed = seed, MaxBuildings = cap });
            int houses = city.CityPatches.Sum(p => p.Buildings.Count);
            Assert.InRange(houses, cap * 0.7, cap);
        }
    }

    private static Vector3 Normal(PropTriangle t) => Vector3.Normalize(Vector3.Cross(t.B - t.A, t.C - t.A));

    private static Vector3 Centroid(PropTriangle t) => (t.A + t.B + t.C) / 3;
}
