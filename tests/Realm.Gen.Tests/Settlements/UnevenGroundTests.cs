using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
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

/// <summary>Cities on sloping ground (ADR 0014): sites, foundations, steps, draped ground and walls, the landmark plinth, the baked ground grid.</summary>
public class UnevenGroundTests
{
    /// <summary>A plane rising along plan X (and optionally Y).</summary>
    private sealed class Slope : IGroundSampler
    {
        private readonly double _dx;
        private readonly double _dy;

        public Slope(double dx, double dy = 0)
        {
            _dx = dx;
            _dy = dy;
        }

        public double HeightAt(double x, double y) => (x * _dx) + (y * _dy);
    }

    private static City Make(int seed = 3, int buildings = 150, CentrepieceKind centre = CentrepieceKind.Fountain) =>
        CityGenerator.Generate(new CityParams { Seed = seed, MaxBuildings = buildings, Centre = centre });

    private static Building FirstIn(City city, WardType ward) => city.AllBuildings.First(b => b.Ward == ward);

    private static (Building Building, Interior Interior) Furnish(City city, WardType ward)
    {
        Building b = FirstIn(city, ward);
        return (b, InteriorGenerator.Generate(b, city.Seed));
    }

    [Fact]
    public void LevelGround_NeedsNoFoundation_AndTheFloorStaysAtZero()
    {
        City city = Make();
        Building b = city.AllBuildings.First();

        BuildingSite site = BuildingSite.Plan(b, FlatGround.Instance);

        Assert.Equal(0, site.BaseY);
        Assert.Null(site.Style);
        Interior interior = InteriorGenerator.Generate(b, city.Seed);
        Assert.True(BuildingMesher.Far(b, interior, site).Min.Y >= -1e-6f);
    }

    [Theory]
    [InlineData(0.15)]
    [InlineData(0.4)]
    [InlineData(-0.6)]
    public void TheFloor_IsNeverBelowAnyGroundUnderTheFootprint(double slope)
    {
        City city = Make();
        var ground = new Slope(slope, slope / 2);

        foreach (Building b in city.AllBuildings)
        {
            BuildingSite site = BuildingSite.Plan(b, ground);
            foreach (Vec2 corner in b.Footprint.Points.Append(b.Footprint.Centroid))
            {
                Assert.True(site.BaseY >= ground.HeightAt(corner.X, corner.Y) - 1e-9, $"building {b.Id}: ground rises into the floor");
            }

            double diameter = b.Footprint.Points.Max(p => b.Footprint.Points.Max(q => Vec2.Distance(p, q)));
            double gradient = Math.Sqrt((slope * slope) + (slope * slope / 4));
            Assert.True(site.BaseY - site.LowestGroundY <= (gradient * (diameter + 1.5)) + 0.5, // the ground just in front of the door counts too
                 $"building {b.Id}: the floor is far above the ground it stands on");
        }
    }

    [Fact]
    public void OnASlope_BuildingsGetAFoundation_AndTheMeshReachesDownToTheLowestGround()
    {
        City city = Make();
        var ground = new Slope(0.4);
        (Building b, Interior interior) = Furnish(city, WardType.Merchant);

        BuildingSite site = BuildingSite.Plan(b, ground);
        PropMesh mesh = BuildingMesher.Far(b, interior, site);

        Assert.NotNull(site.Style);
        Assert.True(site.Drop > 0.5);
        Assert.True(mesh.Min.Y <= (site.LowestGroundY - site.BaseY) - 0.1f, "the foundation does not reach the lowest ground");
        Assert.True(mesh.Min.Y >= (site.LowestGroundY - site.BaseY) - 2.5f, "the foundation sinks far below the ground");
    }

    [Fact]
    public void Style_FollowsTheWard_TheServiceAndTheDrop()
    {
        City city = Make(buildings: 400);
        var steep = new Slope(0.4);

        Assert.Equal(FoundationStyle.Stone, BuildingSite.Plan(FirstIn(city, WardType.Merchant), steep).Style);

        Building slum = FirstIn(city, WardType.Slum);
        BuildingSite slumSite = BuildingSite.Plan(slum, new Slope(0.15));
        Assert.Contains(slumSite.Style, new FoundationStyle?[] { FoundationStyle.Wood, FoundationStyle.Stone }); // a small drop stays stone
        Assert.True(BuildingSite.Plan(slum, new Slope(1.2)).Style is FoundationStyle.Pier, "a tall wooden foundation becomes a pier");

        Building forced = FirstIn(city, WardType.Merchant);
        forced.Service = new ServiceSpec("service.test", "Test", 1, Array.Empty<WardType>(), ServiceNear.Any, false, 10, FoundationStyle.Wood, RoomType.Hall);
        Assert.NotEqual(FoundationStyle.Stone, BuildingSite.Plan(forced, steep).Style);
    }

    [Fact]
    public void WoodenFoundations_StandOnPosts_AndAPierAddsADeck()
    {
        City city = Make(buildings: 400);
        (Building slum, Interior interior) = Furnish(city, WardType.Slum);

        BuildingSite wood = BuildingSite.Plan(slum, new Slope(0.9, 0.5));
        PropMesh withPosts = BuildingMesher.Far(slum, interior, wood);
        PropMesh flat = BuildingMesher.Far(slum, interior, BuildingSite.Level());

        Assert.True(withPosts.Triangles.Count > flat.Triangles.Count + 50, "pilings add many triangles");
        // Posts are thin vertical tubes: some triangles are vertical and far below the floor.
        Assert.Contains(withPosts.Triangles, t => MathF.Abs(Vector3.Normalize(Vector3.Cross(t.B - t.A, t.C - t.A)).Y) < 0.1f && t.A.Y < -1.5f);
    }

    [Fact]
    public void ADoorAboveTheStreet_GetsStepsDownToIt()
    {
        City city = Make();
        (Building b, Interior interior) = Furnish(city, WardType.Merchant);
        // Ground falls away in front of the door: height decreases along the door's outward normal.
        Vec2 outward = b.Footprint.OutwardNormal(b.DoorEdge);
        var ground = new Slope(-outward.X * 0.5, -outward.Y * 0.5);

        BuildingSite site = BuildingSite.Plan(b, ground);
        PropMesh withSteps = BuildingMesher.Far(b, interior, site);
        PropMesh level = BuildingMesher.Far(b, interior, BuildingSite.Level());

        Vec2 door = b.Footprint.Points[b.DoorEdge] + (b.Footprint.Edge(b.DoorEdge) * 0.5);
        // Something below the floor stands out in front of the door, beyond the plinth lip.
        Assert.Contains(withSteps.Triangles, t => Vector3.Dot(new Vector3((float)(t.A.X - door.X), 0, (float)(t.A.Z - door.Y)), new Vector3((float)outward.X, 0, (float)outward.Y)) > 0.5f && t.A.Y < -0.3f);
        Assert.DoesNotContain(level.Triangles, t => t.A.Y < -0.3f);
    }

    [Fact]
    public void Wall_FollowsTheSlope_AndIsSunkIntoIt()
    {
        City city = Make();
        var ground = new Slope(0.25);
        PropMesh wall = CityMesher.Wall(city, ground)!;

        double low = city.Wall.Min(v => ground.HeightAt(v.X, v.Y));
        double high = city.Wall.Max(v => ground.HeightAt(v.X, v.Y));

        Assert.Equal(low - 2, wall.Min.Y, 0.6);
        Assert.True(wall.Max.Y >= high + CityMesher.WallHeight, "the wall is too low on the uphill side");
        // A tower on the uphill side stands as tall above its ground as one downhill.
        Assert.True(wall.Max.Y <= high + CityMesher.WallHeight + 4 + 0.6);
    }

    [Fact]
    public void Landmark_StandsOnTheGroundAtTheSquare_WithAPlinthOnASlope()
    {
        City city = Make();
        var ground = new Slope(0.3);
        CityMeshes onSlope = CityMesher.Build(city, 3, ground);
        CityMeshes onFlat = CityMesher.Build(city, 3);

        Assert.Equal(ground.HeightAt(city.Landmark!.Center.X, city.Landmark.Center.Y), onSlope.LandmarkBaseY, 6);
        Assert.Equal(0, onFlat.LandmarkBaseY);
        Assert.True(onSlope.Landmark!.Solid.Triangles.Count > onFlat.Landmark!.Solid.Triangles.Count, $"a plinth is added on a slope ({onSlope.Landmark.Solid.Triangles.Count} vs {onFlat.Landmark.Solid.Triangles.Count})");
        Assert.True(onSlope.Landmark.Solid.Min.Y < -0.5f, $"min y {onSlope.Landmark.Solid.Min.Y}");
        Assert.True(onFlat.Landmark.Solid.Min.Y >= -1e-6f, $"flat min y {onFlat.Landmark.Solid.Min.Y}");
    }

    [Fact]
    public void MarketStalls_StandOnTheGroundUnderThem()
    {
        City city = Make(centre: CentrepieceKind.Market);
        var ground = new Slope(0.3);

        CityMeshes meshes = CityMesher.Build(city, 3, ground);

        // Stall counters are about 0.9 m high: with stalls following the ground relative to the base, the mesh spans well below and above 0.
        Assert.True(meshes.Landmark!.Solid.Min.Y < -1f);
        Assert.True(meshes.Landmark.Solid.Max.Y > 1f);
    }

    [Fact]
    public void ServiceBuildings_GetASign()
    {
        City city = Make();
        Building plain = city.AllBuildings.First(b => b.Ward == WardType.Merchant && b.Service is null);
        Interior interior = InteriorGenerator.Generate(plain, city.Seed);
        PropMesh before = BuildingMesher.Far(plain, interior);

        plain.Service = new ServiceSpec("service.inn", "Inn", 1, Array.Empty<WardType>(), ServiceNear.Any, false, 10, null, RoomType.Kitchen);
        PropMesh after = BuildingMesher.Far(plain, interior);

        Assert.True(after.Triangles.Count > before.Triangles.Count);
        Assert.Contains(after.Triangles, t => t.Colour == SettlementPalette.Sign("service.inn"));
    }

    [Fact]
    public void GroundGrid_MatchesTheTerrain_RoundTripsThroughTheFile_AndRejectsDamage()
    {
        const int World = 4096;
        const int Spacing = 16;
        int side = (World / Spacing) + 1;
        var heights = new float[side * side];
        for (int row = 0; row < side; row++)
        {
            for (int col = 0; col < side; col++)
            {
                heights[(row * side) + col] = (float)(100 + (0.2 * ((col * Spacing) - 2048)) + (0.1 * ((row * Spacing) - 2048)) + (5 * Math.Sin(col / 7.0)));
            }
        }

        var field = new HeightField(World, Spacing, heights);
        var settlement = new Settlement("settlement.test", "Test", SettlementTier.Town, 300, -200, 0.7, 100, 190, 220, 1, 40, false, CentrepieceKind.None);

        GroundGrid grid = GroundGrid.Capture(field, settlement);

        foreach ((double x, double z) in new[] { (300.0, -200.0), (420.5, -150.25), (180.0, -310.0) })
        {
            Assert.Equal(field.Sample(x, z), grid.HeightAt(x, z), 0.3);
        }

        IReadOnlyDictionary<string, GroundGrid>? back = GroundGridFile.FromBytes(GroundGridFile.ToBytes(new[] { grid }), new ValidationReport());
        Assert.NotNull(back);
        Assert.Equal(grid.Heights.ToArray(), back!["settlement.test"].Heights.ToArray());
        Assert.Equal(grid.OriginX, back["settlement.test"].OriginX);

        var report = new ValidationReport();
        Assert.Null(GroundGridFile.FromBytes(new byte[] { 1, 2, 3 }, report));
        Assert.Equal(SettlementCodes.ManifestInvalid, Assert.Single(report.Issues).Code);
        var truncated = new ValidationReport();
        byte[] bytes = GroundGridFile.ToBytes(new[] { grid });
        Assert.Null(GroundGridFile.FromBytes(bytes[..(bytes.Length / 2)], truncated));
        Assert.NotEmpty(truncated.Issues);

        // The sampler works in the city plan: rotated and relative to the settlement's ground height.
        var sampler = new SettlementGround(settlement, grid);
        (double wx, double wz) = settlement.ToWorld(new Vec2(40, 25));
        Assert.Equal(grid.HeightAt(wx, wz) - 100, sampler.HeightAt(40, 25), 9);
    }
}
