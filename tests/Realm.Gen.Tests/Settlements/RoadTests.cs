using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Realm.Core.Validation;
using Realm.Gen.Props;
using Realm.Gen.Settlements.Meshing;
using Realm.Gen.Settlements.Model;
using Realm.Gen.Settlements.Placement;
using Realm.Gen.Settlements.Roads;
using Realm.Gen.Terrain;
using Realm.Gen.Terrain.Erosion;
using Xunit;

namespace Realm.Gen.Tests.Settlements;

public class RoadTests
{
    private const int World = 8192;
    private const int Spacing = 32;
    private const int Side = (World / Spacing) + 1;
    private const double Half = World / 2.0;
    private const float Plateau = 100f;

    private static HeightField Terrain(Func<double, double, float> height)
    {
        var heights = new float[Side * Side];
        for (int row = 0; row < Side; row++)
        {
            for (int col = 0; col < Side; col++)
            {
                heights[(row * Side) + col] = height(-Half + (col * (double)Spacing), -Half + (row * (double)Spacing));
            }
        }

        return new HeightField(World, Spacing, heights);
    }

    private static Settlement Town(string id, double x, double z) =>
        new(id, id, SettlementTier.Town, x, z, 0, Plateau, 40, 70, 7, 12, false, CentrepieceKind.None);

    private static RiverNetwork RiverAlongX0()
    {
        var points = new List<RiverPoint>();
        for (double z = -3500; z <= 3500; z += 100)
        {
            points.Add(new RiverPoint(0, z, 100, 10));
        }

        return new RiverNetwork(new List<River> { new(0, points, RiverMouth.Sea, null, 7000) }, new List<Lake>(), 32);
    }

    private static IReadOnlyList<Road> Plan(HeightField field, RiverNetwork? network, Settlement[] settlements, ValidationReport? report = null, RoadSettings? settings = null) =>
        RoadPlanner.Plan(field, network, settlements, settings, report ?? new ValidationReport());

    private static readonly Settlement West = Town("settlement.west", -2500, 0);
    private static readonly Settlement East = Town("settlement.east", 2500, 0);

    [Fact]
    public void TwoTowns_GetOneRoad_BetweenTheirGates()
    {
        HeightField flat = Terrain((_, _) => Plateau);
        var report = new ValidationReport();

        IReadOnlyList<Road> roads = Plan(flat, null, new[] { West, East }, report);

        Road road = Assert.Single(roads);
        Assert.Empty(report.Issues);
        Assert.Equal((West.Id, East.Id), (road.From, road.To));
        // It starts at a gate of one town and ends at a gate of the other: within the wall radius of each centre.
        Assert.InRange(West.DistanceTo(road.Points[0].X, road.Points[0].Z), 1, 60);
        Assert.InRange(East.DistanceTo(road.Points[^1].X, road.Points[^1].Z), 1, 60);
        // On flat ground the road is nearly a straight line: not much longer than the straight distance.
        double length = Enumerable.Range(1, road.Points.Count - 1).Sum(i => Math.Sqrt(Math.Pow(road.Points[i].X - road.Points[i - 1].X, 2) + Math.Pow(road.Points[i].Z - road.Points[i - 1].Z, 2)));
        Assert.InRange(length, 4800, 5400);
        Assert.Empty(road.Bridges);
    }

    [Fact]
    public void SurfaceHeights_NeverExceedTheMaximumGrade()
    {
        // Rolling hills so the road has something to grade.
        HeightField hills = Terrain((x, z) => (float)(Plateau + (25 * Math.Sin(x / 300) * Math.Cos(z / 260))));
        RoadSettings settings = new();

        foreach (Road road in Plan(hills, null, new[] { West, East }, null, settings))
        {
            for (int i = 1; i < road.Points.Count; i++)
            {
                double run = Math.Sqrt(Math.Pow(road.Points[i].X - road.Points[i - 1].X, 2) + Math.Pow(road.Points[i].Z - road.Points[i - 1].Z, 2));
                double grade = Math.Abs(road.Points[i].Height - road.Points[i - 1].Height) / run;
                Assert.True(grade <= settings.MaxGrade + 0.002, $"grade {grade:F3} at point {i}"); // heights are rounded to 1 cm
            }
        }
    }

    [Fact]
    public void ARiverCrossing_BecomesOneBridge_OfAboutTheRiversWidth()
    {
        HeightField flat = Terrain((_, _) => Plateau);
        RiverNetwork river = RiverAlongX0();

        Road road = Assert.Single(Plan(flat, river, new[] { West, East }));

        RoadBridge bridge = Assert.Single(road.Bridges);
        Assert.InRange(bridge.X, -20, 20); // the river runs along x = 0
        Assert.InRange(bridge.LengthMeters, 8, 48);
        Assert.Equal(Plateau, bridge.StartHeight, 0.5);
        Assert.Equal(Plateau, bridge.EndHeight, 0.5);
    }

    [Fact]
    public void Road_GoesAroundAHighRidge_ThroughThePass()
    {
        // A 250 m ridge across the whole world at x = 0 except a low pass at z = 2,400 to 3,000.
        HeightField ridge = Terrain((x, z) => (float)(Plateau + (Math.Abs(x) < 150 && !(z > 2400 && z < 3000) ? 250 * (1 - (Math.Abs(x) / 150)) : 0)));

        Road road = Assert.Single(Plan(ridge, null, new[] { West, East }));

        double crossingZ = road.Points.Where(p => Math.Abs(p.X) < 60).Select(p => p.Z).First();
        Assert.InRange(crossingZ, 2300, 3100);
    }

    [Fact]
    public void WhenTheSeaCutsTheWorld_NoRoadIsFound_AndTheTownIsReported()
    {
        HeightField sea = Terrain((x, _) => Math.Abs(x) < 300 ? -50f : Plateau);
        var report = new ValidationReport();

        IReadOnlyList<Road> roads = Plan(sea, null, new[] { West, East }, report);

        Assert.Empty(roads);
        Assert.Contains(report.Issues, i => i.Code == RoadCodes.NotConnected);
    }

    [Fact]
    public void ThreeTowns_AreAllConnected_ByATreeAtLeast()
    {
        HeightField flat = Terrain((_, _) => Plateau);
        Settlement north = Town("settlement.north", 0, -2800);
        var report = new ValidationReport();

        IReadOnlyList<Road> roads = Plan(flat, null, new[] { West, East, north }, report);

        Assert.InRange(roads.Count, 2, 3);
        Assert.Empty(report.Issues);
        var seen = new HashSet<string> { West.Id };
        for (int round = 0; round < 3; round++)
        {
            foreach (Road r in roads.Where(r => seen.Contains(r.From) || seen.Contains(r.To)))
            {
                seen.Add(r.From);
                seen.Add(r.To);
            }
        }

        Assert.Equal(3, seen.Count);
    }

    [Fact]
    public void Carve_PutsTheTerrainOnTheSurface_AndLeavesFarGroundAlone()
    {
        HeightField hills = Terrain((x, z) => (float)(Plateau + (18 * Math.Sin(x / 300) * Math.Cos(z / 260))));
        IReadOnlyList<Road> roads = Plan(hills, null, new[] { West, East });
        var settings = new RoadSettings();

        HeightField carved = RoadTerrain.Carve(hills, roads, settings);

        foreach (RoadPoint p in roads[0].Points.Skip(1).Take(roads[0].Points.Count - 2))
        {
            Assert.Equal(p.Height, carved.Sample(p.X, p.Z), 0.6);
        }

        // Far from the road (more than width/2 + shoulder away) nothing changed.
        for (int row = 0; row < Side; row += 9)
        {
            for (int col = 0; col < Side; col += 9)
            {
                double x = -Half + (col * (double)Spacing);
                double z = -Half + (row * (double)Spacing);
                bool near = roads[0].Points.Any(p => Math.Abs(p.X - x) < 60 && Math.Abs(p.Z - z) < 60);
                if (!near)
                {
                    Assert.Equal(hills.At(col, row), carved.At(col, row));
                }
            }
        }
    }

    [Fact]
    public void Carve_LeavesTheRiverChannelUnderABridge()
    {
        HeightField channel = Terrain((x, _) => Math.Abs(x) < 12 ? Plateau - 10 : Plateau);
        RiverNetwork river = RiverAlongX0();
        IReadOnlyList<Road> roads = Plan(channel, river, new[] { West, East });

        HeightField carved = RoadTerrain.Carve(channel, roads);

        Assert.Single(roads[0].Bridges);
        Assert.Equal(channel.Sample(0, 0), carved.Sample(0, 0), 0.01); // the bed under the bridge is untouched
    }

    [Fact]
    public void Validator_AcceptsPlannedRoads_AndFlagsTamperedOnes()
    {
        HeightField flat = Terrain((_, _) => Plateau);
        RiverNetwork river = RiverAlongX0();
        IReadOnlyList<Road> roads = Plan(flat, river, new[] { West, East });
        HeightField carved = RoadTerrain.Carve(flat, roads);
        var good = new ValidationReport();
        RoadValidator.Validate(roads, flat, carved, river, null, good);

        var raised = roads.Select(r => r with { Points = r.Points.Select(p => p with { Height = p.Height + 40 }).ToList() }).ToList();
        var bad = new ValidationReport();
        RoadValidator.Validate(raised, flat, carved, river, null, bad);

        var longBridge = roads.Select(r => r with { Bridges = r.Bridges.Select(b => b with { LengthMeters = 200 }).ToList() }).ToList();
        var tooLong = new ValidationReport();
        RoadValidator.Validate(longBridge, flat, carved, river, null, tooLong);

        Assert.Empty(good.Issues);
        Assert.Contains(bad.Issues, i => i.Code == RoadCodes.BedMismatch);
        Assert.Contains(tooLong.Issues, i => i.Code == RoadCodes.BridgeTooLong);

        var steepBridge = roads.Select(r => r with { Bridges = r.Bridges.Select(b => b with { EndHeight = b.StartHeight + (0.5 * b.LengthMeters) }).ToList() }).ToList();
        var steep = new ValidationReport();
        RoadValidator.Validate(steepBridge, flat, carved, river, null, steep);
        Assert.Contains(steep.Issues, i => i.Code == RoadCodes.BridgeTooSteep);
    }

    [Fact]
    public void PlannedBridges_RecordTheLowestGroundUnderEachAbutment()
    {
        HeightField flat = Terrain((_, _) => Plateau);
        IReadOnlyList<Road> roads = Plan(flat, RiverAlongX0(), new[] { West, East });

        RoadBridge bridge = roads.SelectMany(r => r.Bridges).First();

        Assert.NotNull(bridge.StartFootingHeight);
        Assert.NotNull(bridge.EndFootingHeight);
        Assert.True(bridge.StartFootingHeight <= bridge.StartHeight && bridge.EndFootingHeight <= bridge.EndHeight);
        var manifest = new RoadManifest { World = "w", Seed = 1, Roads = roads };
        RoadManifest? back = RoadManifest.FromJson(manifest.ToJson(), new ValidationReport());
        Assert.Equal(bridge, back!.Roads.SelectMany(r => r.Bridges).First());
    }

    [Fact]
    public void AnAbutment_ReachesDownToTheLowestGroundUnderIt()
    {
        // Deck at 50 m, but the ground under the first abutment is at 30 m (a cliff edge): the stone must reach 1 m below that.
        var bridge = new RoadBridge(100, 200, 0.5, 30, 6, 50, 53, 4, 8, 30, 45);

        PropMesh mesh = BridgeMesher.Build(bridge);

        var stone = mesh.Triangles.Where(t => t.Colour == SettlementPalette.Hex(0x8c887e)).ToList();
        Assert.InRange(stone.Min(t => Math.Min(t.A.Y, Math.Min(t.B.Y, t.C.Y))), 28.9f, 29.1f);
        // Without a footing the old fixed depth applies.
        PropMesh plain = BridgeMesher.Build(bridge with { StartFootingHeight = null, EndFootingHeight = null });
        Assert.True(plain.Triangles.Where(t => t.Colour == SettlementPalette.Hex(0x8c887e)).Min(t => t.A.Y) > 45);
    }

    [Fact]
    public void SamePlan_SameRoads()
    {
        HeightField flat = Terrain((x, z) => (float)(Plateau + (10 * Math.Sin(x / 200))));

        Assert.Equal(
            Plan(flat, null, new[] { West, East }).Select(r => r.Points).First(),
            Plan(flat, null, new[] { West, East }).Select(r => r.Points).First());
    }

    [Fact]
    public void Weight_IsOneOnTheRoad_HalfwayInTheShoulder_AndZeroBeyond()
    {
        Assert.Equal(1, RoadTerrain.Weight(0, 3, 10));
        Assert.Equal(1, RoadTerrain.Weight(3, 3, 10));
        Assert.Equal(0.5, RoadTerrain.Weight(8, 3, 10), 12);
        Assert.Equal(0, RoadTerrain.Weight(13, 3, 10));
        Assert.Equal(0, RoadTerrain.Weight(5, 3, 0));
    }

    [Fact]
    public void InvalidSettings_AreRejected()
    {
        foreach (RoadSettings bad in new[]
        {
            new RoadSettings { WidthMeters = 0 }, new RoadSettings { GridMeters = 4 }, new RoadSettings { SampleMeters = 64 },
            new RoadSettings { SlopePenalty = -1 }, new RoadSettings { MaxGrade = 0 }, new RoadSettings { MaxBridgeMeters = 0 },
        })
        {
            var report = new ValidationReport();

            Assert.Empty(Plan(Terrain((_, _) => Plateau), null, new[] { West, East }, report, bad));
            Assert.Equal(RoadCodes.SettingsInvalid, Assert.Single(report.Issues).Code);
        }
    }

    [Fact]
    public void Manifest_RoundTrips_AndRejectsBadInput()
    {
        HeightField flat = Terrain((_, _) => Plateau);
        IReadOnlyList<Road> roads = Plan(flat, RiverAlongX0(), new[] { West, East });
        var manifest = new RoadManifest { World = "world.test", Seed = 3, Generators = new[] { "road.planner v1" }, Roads = roads };

        var report = new ValidationReport();
        RoadManifest? back = RoadManifest.FromJson(manifest.ToJson(), report);

        Assert.Empty(report.Issues);
        Assert.NotNull(back);
        Assert.Equal(roads[0].Points, back!.Roads[0].Points);
        Assert.Equal(roads[0].Bridges, back.Roads[0].Bridges);
        Assert.Equal(manifest.ToJson(), back.ToJson());
        foreach (string json in new[] { "not json", "{\"format\": 9}", manifest.ToJson().Replace("\"points\":[[", "\"points\":[[1,") })
        {
            var bad = new ValidationReport();
            Assert.Null(RoadManifest.FromJson(json, bad));
            Assert.Equal(RoadCodes.ManifestInvalid, Assert.Single(bad.Issues).Code);
        }
    }

    [Fact]
    public void BridgeAbutments_StayBelowTheRoadway_SoNoStonePlateClipsThroughTheTerrain()
    {
        var bridge = new RoadBridge(100, 200, 0.5, 30, 6, 50, 53, 4, 8);

        PropMesh mesh = BridgeMesher.Build(bridge);

        // The stone is the abutments only. Nothing of it rises above the deck's underside (the deck is 0.5 m thick), so the graded road
        // meets the deck with no stone showing at road level (the old abutments were flush with the road and clipped the terrain).
        var stone = mesh.Triangles.Where(t => t.Colour == SettlementPalette.Hex(0x8c887e)).ToList();
        Assert.NotEmpty(stone);
        Assert.True(stone.Max(t => Math.Max(t.A.Y, Math.Max(t.B.Y, t.C.Y))) <= 53.2 - 0.4, "stone shows above the deck's underside");
        // And it stays inside the footprint of the deck along the bridge: no plate sticks out beyond the ends.
        float half = 15;
        Assert.All(stone, t => Assert.InRange(Math.Min(t.A.X, Math.Min(t.B.X, t.C.X)), -half - 0.01f, half + 0.01f));
        Assert.All(stone, t => Assert.InRange(Math.Max(t.A.X, Math.Max(t.B.X, t.C.X)), -half - 0.01f, half + 0.01f));
        // Narrower than the deck, so no abutment side shares a plane with it.
        Assert.All(stone, t => Assert.True(Math.Abs(t.A.Z) <= 3 - 0.05f + 1e-3f && Math.Abs(t.B.Z) <= 3 && Math.Abs(t.C.Z) <= 3));
    }

    [Fact]
    public void BridgeMesh_DeckTopFollowsTheEndHeights_AndTheRailingStandsAboveIt()
    {
        var bridge = new RoadBridge(100, 200, 0.5, 30, 6, 50, 53, 4, 8);

        PropMesh mesh = BridgeMesher.Build(bridge);

        // Highest upward-facing surface over the deck's middle, ignoring the rails (which are narrow).
        double TopAt(double x)
        {
            double best = double.MinValue;
            foreach (PropTriangle t in mesh.Triangles.Where(t => Vector3.Normalize(Vector3.Cross(t.B - t.A, t.C - t.A)).Y > 0.9f))
            {
                float min = Math.Min(t.A.X, Math.Min(t.B.X, t.C.X));
                float max = Math.Max(t.A.X, Math.Max(t.B.X, t.C.X));
                float zmin = Math.Min(t.A.Z, Math.Min(t.B.Z, t.C.Z));
                float zmax = Math.Max(t.A.Z, Math.Max(t.B.Z, t.C.Z));
                if (x >= min && x <= max && zmin < -1 && zmax > 1)
                {
                    // Height of the triangle's plane above (x, 0).
                    Vector3 n = Vector3.Cross(t.B - t.A, t.C - t.A);
                    best = Math.Max(best, t.A.Y - (((n.X * (x - t.A.X)) + (n.Z * (0 - t.A.Z))) / n.Y));
                }
            }

            return best;
        }

        // The deck rises 3 m over 30 m: the top surface at x = -14.5 is 50.35 and at 14.5 is 53.05 (with the 0.2 m deck lift).
        Assert.Equal(50.35, TopAt(-14.5), 0.2);
        Assert.Equal(53.05, TopAt(14.5), 0.2);
        Assert.InRange(mesh.HeightMeters, 54.1, 54.4); // end height 53, the 0.2 m lift and the 1 m railing
        Assert.True(mesh.Min.Y < 47, "abutments reach below the deck");
    }

    [Fact]
    public void TheDeck_HasAGentleTimberRampAtEachEnd_DownToTheRoad()
    {
        var bridge = new RoadBridge(100, 200, 0.5, 30, 6, 50, 53, 4, 8);

        (PropMesh timber, PropMesh stone) = BridgeMesher.BuildParts(bridge);

        // The ramps stand beyond the deck's ends (x = -15 and 15) and are timber, not stone.
        Assert.True(timber.Min.X <= -15 - 3.9f && timber.Max.X >= 15 + 3.9f, "no ramp beyond the ends");
        Assert.All(stone.Triangles, t => Assert.InRange(Math.Min(t.A.X, Math.Min(t.B.X, t.C.X)), -15.01f, 15.01f));
        var up = timber.Triangles.Where(t => Vector3.Normalize(Vector3.Cross(t.B - t.A, t.C - t.A)).Y > 0.9f).ToList();
        var startRamp = up.Where(t => Math.Max(t.A.X, Math.Max(t.B.X, t.C.X)) <= -15 + 0.01f && Math.Min(t.A.X, Math.Min(t.B.X, t.C.X)) >= -19.01f).ToList();
        Assert.NotEmpty(startRamp);
        Assert.All(startRamp, t => Assert.True(Vector3.Normalize(Vector3.Cross(t.B - t.A, t.C - t.A)).Y > 0.99f, "the ramp is steeper than 8 percent"));
        // Its tip is level with the road at the bridge's start height (50 m).
        Assert.InRange(startRamp.Min(t => Math.Min(t.A.Y, Math.Min(t.B.Y, t.C.Y))), 49.95f, 50.05f);
        // Build merges both parts.
        Assert.Equal(timber.Triangles.Count + stone.Triangles.Count, BridgeMesher.Build(bridge).Triangles.Count);
    }
}
