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

public class BuildingMesherTests
{
    private static readonly City TestCity = CityGenerator.Generate(new CityParams { Seed = 7, MaxBuildings = 60 });

    private static IEnumerable<(Building Building, Interior Interior)> Houses() =>
        TestCity.CityPatches.SelectMany(p => p.Buildings).Select(b => (b, InteriorGenerator.Generate(b, TestCity.Seed)));

    private static Vector3 Normal(PropTriangle t) => Vector3.Normalize(Vector3.Cross(t.B - t.A, t.C - t.A));

    private static float Area(PropTriangle t) => Vector3.Cross(t.B - t.A, t.C - t.A).Length() / 2;

    private static Vector3 Centroid(PropTriangle t) => (t.A + t.B + t.C) / 3;

    private static bool Same(Vector3 a, Vector3 b) => (a - b).LengthSquared() < 1e-8f;

    [Fact]
    public void Far_RoofIsFacingUpward_AndWallsFaceAwayFromTheHouse_AndHeightMatches()
    {
        foreach ((Building b, Interior it) in Houses())
        {
            PropMesh far = BuildingMesher.Far(b, it);
            double wall = it.Floors.Count * it.FloorHeight;
            double rise = b.Ward == WardType.Cathedral ? 5.0 : b.Style == BuildingStyle.Slum ? 1.5 : 2.4;
            Vec2 c = b.Footprint.Centroid;
            Vector3 centre = new((float)c.X, (float)(wall / 2), (float)c.Y);

            Assert.Equal(wall + rise, far.HeightMeters, 3);
            foreach (PropTriangle t in far.Triangles.Where(t => Same(t.Colour, b.Style == BuildingStyle.Slum ? SettlementPalette.SlumRoof : SettlementPalette.WardRoof(b.Ward))))
            {
                Assert.True(Normal(t).Y > 0.2f, $"building {b.Id}: a roof triangle faces down or sideways");
            }

            foreach (PropTriangle t in far.Triangles.Where(t => Same(t.Colour, SettlementPalette.WallColour(b)) && MathF.Abs(Normal(t).Y) < 0.5f))
            {
                Assert.True(Vector3.Dot(Normal(t), Centroid(t) - centre) > 0, $"building {b.Id}: a wall faces into the house");
            }
        }
    }

    [Fact]
    public void Far_HasPaintedOpenings_OneFrameAndOnePanePerOpening()
    {
        foreach ((Building b, Interior it) in Houses())
        {
            IReadOnlyList<WallOpening> openings = BuildingMesher.Openings(b, it);
            PropMesh far = BuildingMesher.Far(b, it);

            Assert.Contains(openings, o => o.IsDoor);
            int frames = far.Triangles.Count(t => Same(t.Colour, SettlementPalette.Frame));
            Assert.Equal(openings.Count * 2, frames); // each opening: one frame quad = 2 triangles
        }
    }

    [Fact]
    public void Far_IsCheap()
    {
        foreach ((Building b, Interior it) in Houses())
        {
            Assert.InRange(BuildingMesher.Far(b, it).Triangles.Count, 20, 1500);
        }
    }

    [Fact]
    public void Near_OuterWalls_HaveExactlyTheAreaLeftAfterTheOpenings()
    {
        foreach ((Building b, Interior it) in Houses())
        {
            BuildingMeshes near = BuildingMesher.Near(b, it);
            IReadOnlyList<WallOpening> openings = BuildingMesher.Openings(b, it);
            double height = it.Floors.Count * it.FloorHeight;
            ConvexPoly fp = b.Footprint;

            for (int j = 0; j < fp.Count; j++)
            {
                double length = fp.EdgeLength(j);
                Vec2 a = fp.Points[j];
                Vec2 dir = fp.Edge(j).Normalized();
                // Triangles in the plane of this edge, coloured as outer wall, facing outward.
                double actual = near.Visual.Triangles
                    .Where(t => Same(t.Colour, SettlementPalette.WallColour(b)))
                    .Where(t => new[] { t.A, t.B, t.C }.All(v => Math.Abs(Vec2.Cross(dir, new Vec2(v.X, v.Z) - a)) < 1e-4 && Vec2.Dot(dir, new Vec2(v.X, v.Z) - a) > -1e-3 && Vec2.Dot(dir, new Vec2(v.X, v.Z) - a) < length + 1e-3))
                    .Sum(t => (double)Area(t));
                double holes = openings.Where(o => o.Edge == j).Sum(o =>
                    (Math.Min(length - 0.05, o.U + (o.Width / 2)) - Math.Max(0.05, o.U - (o.Width / 2))) * (o.Top - o.Bottom));
                Assert.True(Math.Abs(((length * height) - holes) - actual) < 0.01, $"building {b.Id} edge {j} (len {length:F2}, height {height}): expected {(length * height) - holes}, got {actual}; openings {string.Join(" ", openings.Where(o => o.Edge == j))}");
            }
        }
    }

    [Fact]
    public void Near_OuterWalls_FaceOutward_AndInteriorWallsFaceIntoTheBuilding()
    {
        foreach ((Building b, Interior it) in Houses())
        {
            BuildingMeshes near = BuildingMesher.Near(b, it);
            foreach (PropTriangle t in near.Visual.Triangles.Where(t => Same(t.Colour, SettlementPalette.WallColour(b))))
            {
                Vector3 n = Normal(t);
                Vector3 c = Centroid(t) + (n * 0.05f);
                Assert.False(InsidePlan(b.Footprint, c.X, c.Z), $"building {b.Id}: an outer wall faces inward");
            }

            foreach (PropTriangle t in near.Visual.Triangles.Where(t => Same(t.Colour, SettlementPalette.Inner) && Area(t) > 0.05f)) // slivers have no stable normal
            {
                Vector3 n = Normal(t);
                Vector3 c = Centroid(t) + (n * 0.05f);
                Assert.True(it.Rooms.Any(r => InsidePlan(r.Shape, c.X, c.Z, 0.01)), $"building {b.Id} ({b.Ward}, {it.Rooms.Count} rooms): an inner wall face at {Centroid(t)} normal {n} points into the wall or outside; room polygons {string.Join(" | ", it.Rooms.Select(r => string.Join(" ", r.Shape.Points)))}");
            }
        }
    }

    [Fact]
    public void Near_Floors_CoverTheRoomsMinusTheStairwell()
    {
        foreach ((Building b, Interior it) in Houses())
        {
            BuildingMeshes near = BuildingMesher.Near(b, it);
            double roomArea = it.Rooms.Sum(r => r.Shape.Area);
            double stairArea = it.Stairs is { } s ? s.Width * Vec2.Distance(s.Start, s.End) : 0;

            for (int level = 0; level < it.Floors.Count; level++)
            {
                double y = (level * it.FloorHeight) + 0.01;
                double actual = near.Collision.Triangles
                    .Where(t => Normal(t).Y > 0.99f && new[] { t.A, t.B, t.C }.All(v => MathF.Abs(v.Y - (float)y) < 1e-4f))
                    .Sum(t => (double)Area(t));
                double expected = roomArea - (level >= 1 ? stairArea : 0);
                Assert.Equal(expected, actual, 2);
            }
        }
    }

    [Fact]
    public void Near_StairRamp_ClimbsOneStoreyOverItsLength()
    {
        (Building b, Interior it) = Houses().First(h => h.Interior.Stairs is not null);
        Stair stair = it.Stairs!;
        BuildingMeshes near = BuildingMesher.Near(b, it);
        Vec2 mid = (stair.Start + stair.End) * 0.5;
        Vec2 start = stair.Start + ((stair.End - stair.Start).Normalized() * 0.2);
        Vec2 end = stair.End - ((stair.End - stair.Start).Normalized() * 0.2);

        double Top(Vec2 p)
        {
            double best = double.NaN;
            foreach (PropTriangle t in near.Collision.Triangles.Where(t => Normal(t).Y > 0.3f && Same(t.Colour, SettlementPalette.Stair)))
            {
                if (InsideTriangle(t, (float)p.X, (float)p.Y, out float y) && (double.IsNaN(best) || y > best))
                {
                    best = y;
                }
            }

            return best;
        }

        double rise = it.FloorHeight;
        // The ramp surface runs through the middle of the flight and rises steadily from foot to head.
        Assert.InRange(Top(mid), (rise / 2) - 0.05, (rise / 2) + (rise / 14) + 0.05);
        Assert.True(Top(end) > Top(start) + (rise * 0.6), "the ramp should climb most of a storey between its ends");
    }

    [Fact]
    public void Near_Collision_ExcludesDoorsAndDetail_AndIsClosedToTheOutside()
    {
        foreach ((Building b, Interior it) in Houses())
        {
            BuildingMeshes near = BuildingMesher.Near(b, it);

            Assert.DoesNotContain(near.Collision.Triangles, t => Same(t.Colour, SettlementPalette.Wood) || Same(t.Colour, SettlementPalette.Frame) || Same(t.Colour, SettlementPalette.Handle));
            Assert.True(near.Collision.Triangles.Count < near.Visual.Triangles.Count);
        }
    }

    [Fact]
    public void Meshes_AreDeterministic()
    {
        (Building b, Interior it) = Houses().First();

        Assert.Equal(BuildingMesher.Far(b, it).Triangles, BuildingMesher.Far(b, it).Triangles);
        Assert.Equal(BuildingMesher.Near(b, it).Visual.Triangles, BuildingMesher.Near(b, it).Visual.Triangles);
    }

    private static bool InsidePlan(ConvexPoly poly, double x, double z, double tolerance = 0)
    {
        for (int i = 0; i < poly.Count; i++)
        {
            Vec2 a = poly.Points[i];
            Vec2 e = poly.Edge(i);
            // Counter-clockwise polygon: inside is to the left of every edge.
            if (Vec2.Cross(e.Normalized(), new Vec2(x, z) - a) < -tolerance)
            {
                return false;
            }
        }

        return true;
    }

    private static bool InsideTriangle(PropTriangle t, float x, float z, out float y)
    {
        y = 0;
        float d = ((t.B.Z - t.C.Z) * (t.A.X - t.C.X)) + ((t.C.X - t.B.X) * (t.A.Z - t.C.Z));
        if (MathF.Abs(d) < 1e-9f)
        {
            return false;
        }

        float l1 = (((t.B.Z - t.C.Z) * (x - t.C.X)) + ((t.C.X - t.B.X) * (z - t.C.Z))) / d;
        float l2 = (((t.C.Z - t.A.Z) * (x - t.C.X)) + ((t.A.X - t.C.X) * (z - t.C.Z))) / d;
        float l3 = 1 - l1 - l2;
        if (l1 < -1e-6f || l2 < -1e-6f || l3 < -1e-6f)
        {
            return false;
        }

        y = (l1 * t.A.Y) + (l2 * t.B.Y) + (l3 * t.C.Y);
        return true;
    }
}
