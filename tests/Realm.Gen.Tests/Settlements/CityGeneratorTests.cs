using System;
using System.Collections.Generic;
using System.Linq;
using Realm.Gen.Settlements.Generation;
using Realm.Gen.Settlements.Geometry;
using Realm.Gen.Settlements.Model;
using Xunit;

namespace Realm.Gen.Tests.Settlements;

public class CityGeneratorTests
{
    [Fact]
    public void SameSeedGivesSameCity()
    {
        City a = CityGenerator.Generate(new CityParams { Seed = 7 });
        City b = CityGenerator.Generate(new CityParams { Seed = 7 });

        Assert.Equal(a.Wall, b.Wall);
        Assert.Equal(
            a.CityPatches.SelectMany(p => p.Buildings).Select(x => x.Footprint.Centroid),
            b.CityPatches.SelectMany(p => p.Buildings).Select(x => x.Footprint.Centroid));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(42)]
    public void CityHasWallGatesStreetsAndBuildings(int seed)
    {
        City city = CityGenerator.Generate(new CityParams { Seed = seed });

        Assert.True(city.Wall.Count >= 6, "wall loop too short");
        Assert.NotEmpty(city.Gates);
        Assert.NotEmpty(city.Streets);
        Assert.True(city.CityPatches.Sum(p => p.Buildings.Count) > 100);
    }

    [Fact]
    public void BuildingsAreValidAndInsideTheirWard()
    {
        City city = CityGenerator.Generate(new CityParams { Seed = 5 });

        foreach (Patch patch in city.CityPatches)
        foreach (Building b in patch.Buildings)
        {
            Assert.True(b.Footprint.Area > 0);
            Assert.InRange(b.DoorEdge, 0, b.Footprint.Count - 1);
            foreach (Vec2 v in b.Footprint.Points)
                Assert.True(Inside(patch.Shape, v), $"{v} outside ward {patch.Index}");
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(42)]
    public void FrontDoorsOpenOntoFreeSpace(int seed)
    {
        City city = CityGenerator.Generate(new CityParams { Seed = seed });
        var all = city.CityPatches.SelectMany(p => p.Buildings).ToList();

        var blocked = new List<string>();
        foreach (Building b in all)
        {
            ConvexPoly fp = b.Footprint;
            Vec2 mid = Vec2.Lerp(fp.Points[b.DoorEdge], fp.EdgeEnd(b.DoorEdge), 0.5);
            Vec2 probe = mid + fp.OutwardNormal(b.DoorEdge) * 0.8;

            Building? hit = all.FirstOrDefault(o => o != b && Inside(o.Footprint, probe));
            if (hit != null)
                blocked.Add($"{b.Id} fp {string.Join(" ", fp.Points)} door {b.DoorEdge} tags [{string.Join(",", fp.Tags)}] probe {probe} hit {hit.Id} fp {string.Join(" ", hit.Footprint.Points)} tags [{string.Join(",", hit.Footprint.Tags)}]");
        }
        Assert.True(blocked.Count == 0, $"{blocked.Count} blocked doors: {string.Join("; ", blocked.Take(5))}");
    }

    private static int Houses(City city) => city.CityPatches.Sum(p => p.Buildings.Count);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(42)]
    public void CathedralCanBeSwitchedOff(int seed)
    {
        City with = CityGenerator.Generate(new CityParams { Seed = seed });
        City without = CityGenerator.Generate(new CityParams { Seed = seed, Cathedral = false });

        Assert.Contains(with.CityPatches, p => p.Ward == WardType.Cathedral);
        Assert.DoesNotContain(without.CityPatches, p => p.Ward == WardType.Cathedral);
        Assert.DoesNotContain(without.CityPatches.SelectMany(p => p.Buildings), b => b.Ward == WardType.Cathedral);
        // the ward simply becomes housing, so the city does not lose its houses
        Assert.True(without.CityPatches.Sum(p => p.Buildings.Count) >= with.CityPatches.Sum(p => p.Buildings.Count));
    }

    [Fact]
    public void NoCentrepieceLeavesTheSquareEmpty()
    {
        Assert.Null(CityGenerator.Generate(new CityParams { Seed = 1 }).Landmark);
    }

    [Theory]
    [InlineData(CentrepieceKind.Fountain)]
    [InlineData(CentrepieceKind.Pyre)]
    [InlineData(CentrepieceKind.ExecutionBlock)]
    [InlineData(CentrepieceKind.Market)]
    public void CentrepieceStandsInsideTheSquareAndFitsIt(CentrepieceKind kind)
    {
        foreach (int seed in new[] { 1, 2, 42 })
        {
            City city = CityGenerator.Generate(new CityParams { Seed = seed, Centre = kind });
            Landmark landmark = city.Landmark!;
            ConvexPoly square = city.CityPatches[0].Shape;

            Assert.Equal(kind, landmark.Kind);
            Assert.True(landmark.Radius > 1.5, $"seed {seed}: radius {landmark.Radius}");
            Assert.True(Inside(square, landmark.Center));

            // the whole footprint stays inside the square, clear of its edge
            for (int i = 0; i < square.Count; i++)
            {
                double clearance = -Vec2.Dot(square.OutwardNormal(i), landmark.Center - square.Points[i]);
                Assert.True(clearance >= landmark.Radius, $"seed {seed}: reaches the edge of the square");
            }
        }
    }

    [Fact]
    public void MarketHasStallsInsideTheSquare()
    {
        City city = CityGenerator.Generate(new CityParams { Seed = 1, Centre = CentrepieceKind.Market });
        ConvexPoly square = city.CityPatches[0].Shape;

        Assert.True(city.Landmark!.Stalls.Count >= 8, $"only {city.Landmark.Stalls.Count} stalls");
        foreach (ConvexPoly stall in city.Landmark.Stalls)
            foreach (Vec2 corner in stall.Points)
                Assert.True(Inside(square, corner));
    }

    [Fact]
    public void ChoosingACentrepieceDoesNotMoveAnyHouse()
    {
        IEnumerable<Vec2> Houses(CityParams p) =>
            CityGenerator.Generate(p).CityPatches.SelectMany(x => x.Buildings).Select(b => b.Footprint.Centroid);

        var baseline = Houses(new CityParams { Seed = 3 }).ToList();
        foreach (CentrepieceKind kind in new[] { CentrepieceKind.Fountain, CentrepieceKind.Pyre, CentrepieceKind.Market })
            Assert.Equal(baseline, Houses(new CityParams { Seed = 3, Centre = kind }));
    }

    [Theory]
    [InlineData(1, 50)]
    [InlineData(2, 150)]
    [InlineData(42, 300)]
    public void MaxBuildingsIsHonouredExactly(int seed, int max)
    {
        City city = CityGenerator.Generate(new CityParams { Seed = seed, MaxBuildings = max });

        Assert.Equal(max, Houses(city));
    }

    [Theory]
    [InlineData(6)]
    [InlineData(10)]
    [InlineData(24)]
    [InlineData(40)]
    public void WardCountIsHonoured(int wards)
    {
        City city = CityGenerator.Generate(new CityParams { Seed = 1, CityPatchCount = wards });

        Assert.Equal(wards, city.CityPatches.Count);
    }

    [Fact]
    public void ExplicitWardsAndMaxBuildingsTrimsInsteadOfGrowing()
    {
        City city = CityGenerator.Generate(new CityParams { Seed = 1, CityPatchCount = 24, MaxBuildings = 100 });

        Assert.Equal(24, city.CityPatches.Count);
        Assert.Equal(100, Houses(city));
    }

    [Fact]
    public void TrimmingRemovesTheOutskirtsAndKeepsTheCentre()
    {
        var full = CityGenerator.Generate(new CityParams { Seed = 1, CityPatchCount = 24 });
        var cut = CityGenerator.Generate(new CityParams { Seed = 1, CityPatchCount = 24, MaxBuildings = 100 });

        double Farthest(City c) => c.CityPatches.SelectMany(p => p.Buildings).Max(b => b.Footprint.Centroid.Length);
        Assert.True(Farthest(cut) < Farthest(full), "the cap should pull the edge of the city inwards");

        // the second-closest ward is the cathedral; it must survive a heavy cut
        var heavy = CityGenerator.Generate(new CityParams { Seed = 1, CityPatchCount = 24, MaxBuildings = 30 });
        Assert.Contains(heavy.CityPatches.SelectMany(p => p.Buildings), b => b.Ward == WardType.Cathedral);
    }

    [Fact]
    public void MaxBuildingsIsDeterministic()
    {
        var a = CityGenerator.Generate(new CityParams { Seed = 7, MaxBuildings = 120 });
        var b = CityGenerator.Generate(new CityParams { Seed = 7, MaxBuildings = 120 });

        Assert.Equal(
            a.CityPatches.SelectMany(p => p.Buildings).Select(x => x.Id),
            b.CityPatches.SelectMany(p => p.Buildings).Select(x => x.Id));
    }

    [Fact]
    public void UnreachableMaxBuildingsGivesTheBiggestCityAndTerminates()
    {
        City city = CityGenerator.Generate(new CityParams { Seed = 1, MaxBuildings = 100000 });

        Assert.InRange(Houses(city), 200, 99999);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(42)]
    public void NoHouseIsTriangular(int seed)
    {
        var parameters = new[]
        {
            new CityParams { Seed = seed },
            new CityParams { Seed = seed, CityPatchCount = 6, Cathedral = false },
            new CityParams { Seed = seed, MaxBuildings = 700 },
        };
        foreach (CityParams p in parameters)
        {
            City city = CityGenerator.Generate(p);
            var triangles = city.CityPatches.SelectMany(w => w.Buildings).Where(b => b.Footprint.Count < 4).ToList();
            Assert.True(triangles.Count == 0, $"seed {seed}: {triangles.Count} triangular houses, e.g. {triangles.FirstOrDefault()?.Id}");
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(42)]
    public void NeighbouringHousesDoNotTouch(int seed)
    {
        City city = CityGenerator.Generate(new CityParams { Seed = seed });
        var tooClose = new List<string>();

        foreach (Patch patch in city.CityPatches)
        {
            var houses = patch.Buildings;
            for (int i = 0; i < houses.Count; i++)
            for (int j = i + 1; j < houses.Count; j++)
            {
                double d = Distance(houses[i].Footprint, houses[j].Footprint);
                if (d < 1.5) tooClose.Add($"{houses[i].Id}/{houses[j].Id}: {d:0.00} m");
            }
        }
        Assert.True(tooClose.Count == 0, $"{tooClose.Count} pairs closer than 1.5 m: {string.Join("; ", tooClose.Take(5))}");
    }

    // Convex polygons: if they don't overlap, the gap is between a vertex of one and an edge of the other.
    private static double Distance(ConvexPoly a, ConvexPoly b) =>
        System.Math.Min(VertexToEdges(a, b), VertexToEdges(b, a));

    private static double VertexToEdges(ConvexPoly from, ConvexPoly to)
    {
        double best = double.MaxValue;
        foreach (Vec2 p in from.Points)
            for (int e = 0; e < to.Count; e++)
            {
                Vec2 s = to.Points[e], d = to.Edge(e);
                double t = System.Math.Max(0, System.Math.Min(1, Vec2.Dot(p - s, d) / Vec2.Dot(d, d)));
                best = System.Math.Min(best, Vec2.Distance(p, s + d * t));
            }
        return best;
    }

    private static bool Inside(ConvexPoly poly, Vec2 p)
    {
        for (int i = 0; i < poly.Count; i++)
            if (Vec2.Cross(poly.Edge(i), p - poly.Points[i]) < -1e-6) return false;
        return true;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(42)]
    public void EveryBuildingHasARectangularFloorPlan_WithItsDoorOnTheFront(int seed)
    {
        City city = CityGenerator.Generate(new CityParams { Seed = seed, MaxBuildings = 400 });

        foreach (Building b in city.AllBuildings)
        {
            ConvexPoly fp = b.Footprint;
            Assert.Equal(4, fp.Count);
            Assert.Equal(0, b.DoorEdge);
            for (int i = 0; i < 4; i++)
            {
                Vec2 e = fp.Edge(i).Normalized();
                Vec2 next = fp.Edge((i + 1) % 4).Normalized();
                Assert.True(Math.Abs(Vec2.Dot(e, next)) < 1e-6, $"building {b.Id}: corner {i} is not a right angle");
            }

            Assert.True(Math.Abs(fp.EdgeLength(0) - fp.EdgeLength(2)) < 1e-6 && Math.Abs(fp.EdgeLength(1) - fp.EdgeLength(3)) < 1e-6);
            Assert.True(fp.EdgeLength(0) >= 3.5 - 1e-6 && fp.EdgeLength(1) >= 3.0 - 1e-6, $"building {b.Id} is too small");
        }
    }

    [Fact]
    public void NeighbouringBuildings_StandAtLeastAboutThreeMetresApart()
    {
        City city = CityGenerator.Generate(new CityParams { Seed = 5, MaxBuildings = 300 });
        var buildings = city.CityPatches.SelectMany(p => p.Buildings).ToList();

        double closest = double.MaxValue;
        for (int i = 0; i < buildings.Count; i++)
        for (int j = i + 1; j < buildings.Count; j++)
        {
            if (Vec2.Distance(buildings[i].Footprint.Centroid, buildings[j].Footprint.Centroid) > 60) continue;
            closest = Math.Min(closest, Gap(buildings[i].Footprint, buildings[j].Footprint));
        }

        Assert.True(closest >= 2.7, $"two buildings are only {closest:F2} m apart");
    }

    // Distance between two non-overlapping convex polygons: the shortest vertex-to-edge distance either way.
    private static double Gap(ConvexPoly a, ConvexPoly b)
    {
        double best = double.MaxValue;
        foreach ((ConvexPoly p, ConvexPoly q) in new[] { (a, b), (b, a) })
        foreach (Vec2 v in p.Points)
        for (int k = 0; k < q.Count; k++)
        {
            Vec2 s = q.Points[k], e = q.Edge(k);
            double t = Math.Clamp(Vec2.Dot(v - s, e) / Vec2.Dot(e, e), 0, 1);
            best = Math.Min(best, Vec2.Distance(v, s + (e * t)));
        }

        return best;
    }
}
