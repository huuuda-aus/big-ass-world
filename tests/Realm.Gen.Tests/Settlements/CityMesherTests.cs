using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Realm.Gen.Props;
using Realm.Gen.Settlements.Generation;
using Realm.Gen.Settlements.Geometry;
using Realm.Gen.Settlements.Meshing;
using Realm.Gen.Settlements.Model;
using Xunit;

namespace Realm.Gen.Tests.Settlements;

public class CityMesherTests
{
    private static readonly Dictionary<(CentrepieceKind, int, int), City> Cache = new();

    // Generating a city takes a fraction of a second; the tests only read it, so each variant is generated once.
    private static City Make(CentrepieceKind centre, int seed = 3, int buildings = 120)
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue((centre, seed, buildings), out City? city))
            {
                city = CityGenerator.Generate(new CityParams { Seed = seed, MaxBuildings = buildings, Centre = centre });
                Cache[(centre, seed, buildings)] = city;
            }

            return city;
        }
    }

    private static float Area(PropTriangle t) => Vector3.Cross(t.B - t.A, t.C - t.A).Length() / 2;

    private static Vector3 Normal(PropTriangle t) => Vector3.Normalize(Vector3.Cross(t.B - t.A, t.C - t.A));

    private static bool ContainsPlan(PropTriangle t, double x, double z)
    {
        float px = (float)x;
        float pz = (float)z;
        float d = ((t.B.Z - t.C.Z) * (t.A.X - t.C.X)) + ((t.C.X - t.B.X) * (t.A.Z - t.C.Z));
        if (MathF.Abs(d) < 1e-9f)
        {
            return false;
        }

        float l1 = (((t.B.Z - t.C.Z) * (px - t.C.X)) + ((t.C.X - t.B.X) * (pz - t.C.Z))) / d;
        float l2 = (((t.C.Z - t.A.Z) * (px - t.C.X)) + ((t.A.X - t.C.X) * (pz - t.C.Z))) / d;
        return l1 >= 0 && l2 >= 0 && 1 - l1 - l2 >= 0;
    }

    [Fact]
    public void Wall_HasTowersAtEveryCorner_AndAnOpenGapAtEveryGate()
    {
        City city = Make(CentrepieceKind.None);

        PropMesh wall = CityMesher.Wall(city)!;

        // Looking straight down: tower and wall tops cover every ordinary wall corner but leave each gate open.
        var tops = wall.Triangles.Where(t => Normal(t).Y > 0.5f).ToList();
        foreach (Vec2 corner in city.Wall.Where(v => !city.Gates.Any(g => Vec2.Distance(g, v) < 0.01)))
        {
            Assert.Contains(tops, t => ContainsPlan(t, corner.X, corner.Y));
        }

        foreach (Vec2 gate in city.Gates)
        {
            Assert.DoesNotContain(tops, t => ContainsPlan(t, gate.X, gate.Y));
        }

        Assert.Equal(CityMesher.WallHeight + 4, (double)wall.Max.Y, 3); // the gate towers are the tallest (wall height plus 4 m)
        Assert.Equal(-2.0, (double)wall.Min.Y, 3); // the wall is sunk 2 m into the ground so no gap shows on slopes
    }

    [Fact]
    public void Towers_FaceAwayFromTheirAxis()
    {
        City city = Make(CentrepieceKind.None);
        PropMesh wall = CityMesher.Wall(city)!;

        foreach (Vec2 corner in city.Wall.Where(v => !city.Gates.Any(g => Vec2.Distance(g, v) < 0.01)))
        {
            var side = wall.Triangles.Where(t => MathF.Abs(Normal(t).Y) < 0.2f)
                .Where(t => new Vec2(((t.A + t.B + t.C) / 3).X, ((t.A + t.B + t.C) / 3).Z) is var c && Vec2.Distance(c, corner) is > 2.0 and < 3.3)
                .ToList();

            Assert.NotEmpty(side);
            foreach (PropTriangle t in side)
            {
                Vector3 c = (t.A + t.B + t.C) / 3;
                Assert.True(Vector3.Dot(Normal(t), new Vector3(c.X - (float)corner.X, 0, c.Z - (float)corner.Y)) > 0, "a tower face points into the tower");
            }
        }
    }

    [Theory]
    [InlineData(CentrepieceKind.Fountain)]
    [InlineData(CentrepieceKind.Pyre)]
    [InlineData(CentrepieceKind.ExecutionBlock)]
    [InlineData(CentrepieceKind.Market)]
    public void EveryCentrepiece_Builds_WithinTheSquare(CentrepieceKind kind)
    {
        City city = Make(kind);

        LandmarkMeshes? meshes = LandmarkMesher.Build(city.Landmark!, 3);

        Assert.NotNull(meshes);
        Vec2 c = city.Landmark!.Center;
        foreach (PropTriangle t in meshes!.Solid.Triangles)
        {
            foreach (Vector3 v in new[] { t.A, t.B, t.C })
            {
                // Nothing strays beyond about twice the square's allowance (stall awnings and the pyre's flames reach a bit past it).
                Assert.True(new Vec2(v.X, v.Z).Length < 1 || Vec2.Distance(new Vec2(v.X, v.Z), c) < (city.Landmark.Radius * 2) + 6, $"{kind}: a vertex is far from the centre");
            }
        }
    }

    [Fact]
    public void None_BuildsNothing()
    {
        City city = Make(CentrepieceKind.None);

        Assert.Null(city.Landmark);
        Assert.Null(CityMesher.Build(city, 3).Landmark);
    }

    [Fact]
    public void Fountain_BasinWallsFaceOutward_AndInwardOnTheInsideFace()
    {
        City city = Make(CentrepieceKind.Fountain);
        LandmarkMeshes fountain = LandmarkMesher.Build(city.Landmark!, 3)!;
        var c = new Vector3((float)city.Landmark!.Center.X, 0, (float)city.Landmark.Center.Y);
        float r = (float)city.Landmark.Radius;

        bool OnRadius(PropTriangle t, float radius) => new[] { t.A, t.B, t.C }.All(v => MathF.Abs(new Vector2(v.X - c.X, v.Z - c.Z).Length() - radius) < 1e-3f);
        var outer = fountain.Solid.Triangles.Where(t => OnRadius(t, r) && t.A.Y < 1).ToList();
        var inner = fountain.Solid.Triangles.Where(t => OnRadius(t, r - 0.25f) && t.A.Y < 1).ToList();

        Assert.NotEmpty(outer);
        Assert.NotEmpty(inner);
        foreach (PropTriangle t in outer)
        {
            Vector3 radial = Vector3.Normalize(new Vector3(t.A.X - c.X, 0, t.A.Z - c.Z));
            Assert.True(Vector3.Dot(Normal(t), radial) > 0.9f, "outer basin wall faces outward");
        }

        foreach (PropTriangle t in inner)
        {
            Vector3 radial = Vector3.Normalize(new Vector3(t.A.X - c.X, 0, t.A.Z - c.Z));
            Assert.True(Vector3.Dot(Normal(t), radial) < -0.9f, "inner basin wall faces the water");
        }

        Assert.NotNull(fountain.Water);
        Assert.NotNull(fountain.JetOrigin);
        Assert.True(fountain.Solid.HeightMeters > r * 0.9f, "the spout rises above the pillar");
    }

    [Fact]
    public void Pyre_HasLogsFlamesAndALightAboveThePile()
    {
        City city = Make(CentrepieceKind.Pyre);

        LandmarkMeshes pyre = LandmarkMesher.Build(city.Landmark!, 3)!;

        Assert.NotNull(pyre.Flames);
        Assert.True(pyre.FireLight!.Value.Y > pyre.Solid.HeightMeters * 0.5f);
        Assert.True(pyre.Solid.Triangles.Count > 100);
    }

    [Fact]
    public void Market_HasOneAwningPerStall()
    {
        City city = Make(CentrepieceKind.Market);
        LandmarkMeshes market = LandmarkMesher.Build(city.Landmark!, 3)!;

        Assert.True(city.Landmark!.Stalls.Count > 0);
        // Each stall: 4 posts + counter + awning + 3 goods = 9 boxes of 12 triangles.
        int expected = city.Landmark.Stalls.Count(s => Vec2.Distance(s.Points[0], s.Points[1]) >= 0.5 && Vec2.Distance(s.Points[0], s.Points[3]) >= 0.5) * 9 * 12;
        Assert.Equal(expected, market.Solid.Triangles.Count);
    }

    [Fact]
    public void Landmarks_AreDeterministic()
    {
        City city = Make(CentrepieceKind.Pyre);

        Assert.Equal(LandmarkMesher.Build(city.Landmark!, 3)!.Solid.Triangles, LandmarkMesher.Build(city.Landmark!, 3)!.Solid.Triangles);
    }
}
