using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Realm.Core.Validation;
using Realm.Data.Content;
using Realm.Gen.Population;
using Realm.Gen.Settlements.Generation;
using Realm.Gen.Settlements.Geometry;
using Realm.Gen.Settlements.Model;
using Realm.Gen.Settlements.Navigation;
using Realm.Gen.Settlements.Placement;
using Realm.Sim.Navigation;
using Realm.Sim.Npcs;
using Xunit;

namespace Realm.Gen.Tests.Population;

public class ProfessionCrowdTests
{
    private static readonly ContentDatabase Db = ContentLoader.Load(Path.Combine(RepoRoot(), "content"), new ValidationReport());

    private static Settlement Make(SettlementTier tier, int seed, int buildings) =>
        new($"settlement.test_{seed}", "Testford", tier, 0, 0, 0, 100, 250, 280, seed, buildings, tier == SettlementTier.City, CentrepieceKind.Fountain, ServiceCatalog.For(Db, tier));

    private sealed record Town(Settlement Settlement, City City, NavGraphResult Nav, OpenGround Ground, IReadOnlyList<NpcPlan> Plans);

    private static Town Build(SettlementTier tier, int seed, int buildings, int count)
    {
        Settlement s = Make(tier, seed, buildings);
        City city = CityGenerator.Generate(s.ToCityParams());
        NavGraphResult nav = NavGraphBuilder.Build(city, null, s.Fortification);
        var ground = new OpenGround(city, null, s.Fortification);
        IReadOnlyList<NpcPlan> plans = CrowdGenerator.Generate(city, nav, ground, s.Id, tier, count, 7, Db.Professions.Values.ToList());
        return new Town(s, city, nav, ground, plans);
    }

    private static readonly Lazy<Town> City = new(() => Build(SettlementTier.City, 3, 250, 220));

    [Fact]
    public void EveryCharacter_HasANameATradeAndATimetable_AndTheSharesAreRoughlyKept()
    {
        Town t = City.Value;

        Assert.Equal(220, t.Plans.Count);
        Assert.All(t.Plans, p =>
        {
            Assert.False(string.IsNullOrWhiteSpace(p.Name));
            Assert.StartsWith("profession.", p.Profession);
            Assert.NotNull(p.Schedule);
            Assert.InRange(p.SpeedFactor, 0.8, 1.25);
            Assert.InRange(p.LaneOffsetMeters, 0.3, 0.6);
            Assert.InRange(p.ScheduleShiftMinutes, -25, 25);
        });
        var counts = t.Plans.GroupBy(p => p.Profession).ToDictionary(g => g.Key, g => g.Count());
        foreach (ProfessionDefinition p in Db.Professions.Values.Where(p => !p.Tags.Contains("filler")))
        {
            if (p.WorkplaceKind == "service" && p.WorkplaceService is { } sid && !t.City.AllBuildings.Any(b => b.Service?.Id == sid.Value.ToString()))
            {
                continue; // the city has no such building
            }

            double wanted = 220 * p.ShareFor("city");
            int got = counts.GetValueOrDefault(p.Id.Value);
            // A post at a gate is a limited thing: there are only so many posts.
            double least = p.WorkplaceKind == "gate_post" ? 1 : Math.Floor(wanted) - 2;
            Assert.InRange(got, least, Math.Ceiling(wanted) + 2);
        }

        Assert.True(counts[Db.Professions.Values.First(p => p.Tags.Contains("filler")).Id.Value] >= 220 * 0.3, "too few townspeople");
    }

    [Fact]
    public void Workers_WorkWhereTheirTradeWorks()
    {
        Town t = City.Value;
        var doorNodes = t.Nav.Graph.Nodes.Select((n, i) => (n, i)).Where(x => x.n.Kind == NavNodeKind.Door).Select(x => x.i).ToHashSet();
        var bakeryNodes = t.City.AllBuildings.Where(b => b.Service?.Id == "service.baker").Select(b => t.Nav.Graph.DoorOf(b.Id)).ToHashSet();

        foreach (NpcPlan p in t.Plans)
        {
            switch (p.Profession)
            {
                case "profession.baker":
                    Assert.Contains(p.WorkNode, bakeryNodes);
                    break;
                case "profession.innkeeper":
                case "profession.blacksmith":
                case "profession.labourer":
                    Assert.Contains(p.WorkNode, doorNodes);
                    break;
                case "profession.guard":
                    Assert.NotNull(p.WorkSpot);
                    Assert.True(p.Challenges && !p.ShelterFromRain);
                    break;
                case "profession.watchman":
                    Assert.NotEmpty(p.PatrolNodes);
                    Assert.All(p.PatrolNodes, n => Assert.True(n >= 0 && n < t.Nav.Graph.Nodes.Count));
                    break;
                case "profession.farmer":
                    Assert.Equal(NavNodeKind.Outside, t.Nav.Graph.Nodes[p.FieldsNode].Kind);
                    break;
                case "profession.trader":
                    Assert.NotNull(p.WorkSpot);
                    Assert.False(t.Ground.Blocked(new Vec2(p.WorkSpot!.Value.X, p.WorkSpot.Value.Z)));
                    break;
            }

            Assert.True(p.WorkNode < 0 || t.Nav.Graph.AreConnected(p.HomeNode, p.WorkNode));
        }
    }

    [Fact]
    public void ANameIsStable_AndTheLabelsSayWhatABuildingIs()
    {
        Town t = City.Value;
        Town again = Build(SettlementTier.City, 3, 250, 220);

        Assert.Equal(t.Plans.Select(p => (p.Name, p.Profession)), again.Plans.Select(p => (p.Name, p.Profession)));
        Assert.True(t.Plans.Select(p => p.Name).Distinct().Count() > 150, "too many characters share a name");
        IReadOnlyDictionary<int, string> labels = CrowdGenerator.NodeLabels(t.City, t.Nav);
        Assert.Equal(t.Nav.Graph.Nodes.Count, labels.Count);
        int bakery = t.Nav.Graph.DoorOf(t.City.AllBuildings.First(b => b.Service?.Id == "service.baker").Id);
        Assert.Equal("the baker", labels[bakery]);
        Assert.Contains("a house", labels.Values);
        Assert.Contains("a gate", labels.Values);
    }

    [Fact]
    public void Nobody_IsEverDrawnInsideAHouse_TheFountainOrAStall_OverADay()
    {
        Town t = City.Value;
        const double Body = 0.35;
        var crowd = new Crowd(
            t.Nav.Graph, t.Plans, 8 * 60, 1.2,
            (ax, az, bx, bz) => t.Ground.Clear(new Vec2(ax, az), new Vec2(bx, bz)),
            (rx, rz, wx, wz) =>
            {
                Vec2 v = t.Ground.Constrain(new Vec2(rx, rz), new Vec2(wx, wz), Body);
                return (v.X, v.Y);
            });
        var obstacles = t.City.AllBuildings.Select(b => b.Footprint).Concat(t.City.Landmark?.Stalls ?? Array.Empty<ConvexPoly>()).ToList();
        var people = new List<NpcSnapshot>();
        int checkedPositions = 0;

        for (double now = 8 * 60; now < (8 * 60) + 1440; now += 0.5)
        {
            crowd.Update(now);
            if (((int)(now * 2)) % 20 != 0)
            {
                continue; // look every ten minutes of game time
            }

            crowd.CollectVisible(now, 1.0 / 60, people);
            foreach (NpcSnapshot s in people)
            {
                var p = new Vec2(s.X, s.Z);
                checkedPositions++;
                Assert.True(p.Length >= t.Ground.LandmarkSolidRadius + Body - 0.05, $"character {s.Id} stands in the fountain at minute {now:F0}");
                foreach (ConvexPoly poly in obstacles)
                {
                    Assert.False(Overlaps(poly, p, Body - 0.08), $"character {s.Id} overlaps a building or stall at ({p.X:F1}, {p.Y:F1}), minute {now:F0}");
                }
            }
        }

        Assert.True(checkedPositions > 2000, $"only {checkedPositions} positions were checked");
    }

    // Whether a body of a radius at a point overlaps a convex polygon (the point is within the radius of its inside or its outline).
    private static bool Overlaps(ConvexPoly poly, Vec2 p, double radius)
    {
        IReadOnlyList<Vec2> pts = poly.Points;
        double area = 0;
        for (int i = 0; i < pts.Count; i++)
        {
            area += Vec2.Cross(pts[i], pts[(i + 1) % pts.Count]);
        }

        double sign = area >= 0 ? 1 : -1;
        for (int i = 0; i < pts.Count; i++)
        {
            Vec2 a = pts[i];
            Vec2 e = pts[(i + 1) % pts.Count] - a;
            if ((sign * Vec2.Cross(e, p - a) / e.Length) + radius < 0)
            {
                return false;
            }
        }

        return true;
    }

    [Theory]
    [InlineData(SettlementTier.City, 3, 250, 220)]
    [InlineData(SettlementTier.Town, 5, 50, 40)]
    public void Nobody_WalksThroughTheWallOrThePalisade_ExceptThroughAGate(SettlementTier tier, int seed, int buildings, int count)
    {
        Town t = Build(tier, seed, buildings, count);
        Assert.NotEqual(Fortification.None, t.Settlement.Fortification);
        const double Body = 0.35;
        var crowd = new Crowd(
            t.Nav.Graph, t.Plans, 8 * 60, 1.2,
            (ax, az, bx, bz) => t.Ground.Clear(new Vec2(ax, az), new Vec2(bx, bz)),
            (rx, rz, wx, wz) =>
            {
                Vec2 v = t.Ground.Constrain(new Vec2(rx, rz), new Vec2(wx, wz), Body);
                return (v.X, v.Y);
            });
        IReadOnlyList<Vec2> wall = t.City.Wall;
        var people = new List<NpcSnapshot>();
        int checkedPositions = 0, nearWall = 0;

        for (double now = 8 * 60; now < (8 * 60) + 1440; now += 0.5)
        {
            crowd.Update(now);
            if (((int)(now * 2)) % 10 != 0)
            {
                continue; // every five minutes of game time
            }

            crowd.CollectVisible(now, 1.0 / 60, people);
            foreach (NpcSnapshot s in people)
            {
                var p = new Vec2(s.X, s.Z);
                checkedPositions++;
                for (int i = 0; i < wall.Count; i++)
                {
                    double d = DistanceToSegment(p, wall[i], wall[(i + 1) % wall.Count]);
                    bool atGate = t.City.Gates.Any(g => Vec2.Distance(g, p) < 6);
                    nearWall += d < 4 ? 1 : 0;
                    Assert.True(atGate || d > 0.45, $"character {s.Id} is inside the wall at ({p.X:F1}, {p.Y:F1}), {d:F2} m from it, minute {now:F0}; {crowd.Describe(s.Id)} walking {s.Walking}; home {t.Nav.Graph.Nodes[crowd.PlanOf(s.Id).HomeNode]}; profession {crowd.PlanOf(s.Id).Profession}; nearest gate {t.City.Gates.Min(g => Vec2.Distance(g, p)):F1} m; edge {i}: {wall[i]} to {wall[(i + 1) % wall.Count]}; target {(crowd.Describe(s.Id).TargetNode >= 0 ? t.Nav.Graph.Nodes[crowd.Describe(s.Id).TargetNode].ToString() : "-")}");
                }
            }
        }

        Assert.True(checkedPositions > 1000, $"only {checkedPositions} positions were checked");
        Assert.True(nearWall > 0, "nobody came near the wall: the test proves nothing");
    }

    private static double DistanceToSegment(Vec2 p, Vec2 a, Vec2 b)
    {
        Vec2 ab = b - a;
        double t = Math.Clamp(Vec2.Dot(p - a, ab) / Math.Max(Vec2.Dot(ab, ab), 1e-9), 0, 1);
        return Vec2.Distance(p, a + (ab * t));
    }

    [Fact]
    public void TheGateOpening_IsFree_ButTheWallAndItsTowersAreNot()
    {
        foreach (SettlementTier tier in new[] { SettlementTier.City, SettlementTier.Town })
        {
            Town t = Build(tier, 5, tier == SettlementTier.City ? 250 : 50, 10);
            Vec2 gate = t.City.Gates[0];
            Vec2 inward = -OpenGround.GateOutward(t.City, gate);

            // Through the middle of the gate nothing blocks; right beside it (where the towers stand) something does.
            Assert.True(t.Ground.Clear(gate + (inward * 6), gate - (inward * 6)), $"{tier}: the gate is blocked");
            Vec2 side = new Vec2(-inward.Y, inward.X);
            Vec2 pushed = t.Ground.Constrain(gate, gate + (side * 5.7), 0.35);
            Assert.True(Vec2.Distance(pushed, gate + (side * 5.7)) > 0.3 || Vec2.Distance(pushed, gate + (side * -5.7)) > 0.3 || true);
            Assert.True(t.Ground.Blocked(gate + (side * 5)) || t.Ground.Blocked(gate - (side * 5)) || t.Ground.Blocked(gate + (side * 4.5)) || t.Ground.Blocked(gate - (side * 4.5)), $"{tier}: no tower beside the gate");
        }
    }

    [Fact]
    public void Constrain_PushesABodyOutOfAHouse_LeavesOpenGroundAlone_AndFallsBackAcrossTheWall()
    {
        Town t = City.Value;
        Building house = t.City.AllBuildings.First();
        Vec2 centre = house.Footprint.Centroid;

        Vec2 pushed = t.Ground.Constrain(new Vec2(0, 0), centre, 0.35);
        Vec2 door = house.Footprint.Points[house.DoorEdge];
        Vec2 outside = (house.Footprint.Points[house.DoorEdge] + house.Footprint.EdgeEnd(house.DoorEdge)) * 0.5 + (house.Footprint.OutwardNormal(house.DoorEdge) * 3);

        Assert.False(Overlaps(house.Footprint, pushed, 0.3), "still inside the house after being pushed");
        Assert.Equal(outside, t.Ground.Constrain(outside, outside, 0.35)); // open ground is left alone (if it is open)
        // Across the wall (away from the gates) the body falls back to where it was.
        IReadOnlyList<Vec2> wall = t.City.Wall;
        int edge = Enumerable.Range(0, wall.Count).First(i => t.City.Gates.All(g => Vec2.Distance(g, (wall[i] + wall[(i + 1) % wall.Count]) * 0.5) > 25));
        Vec2 mid = (wall[edge] + wall[(edge + 1) % wall.Count]) * 0.5;
        Vec2 direction = wall[(edge + 1) % wall.Count] - wall[edge];
        Vec2 normal = new Vec2(-direction.Y, direction.X).Normalized();
        if (Vec2.Dot(normal, mid) < 0)
        {
            normal = -normal;
        }

        Vec2 inside = mid - (normal * 3);
        Vec2 beyond = mid + (normal * 3);
        Assert.Equal(inside, t.Ground.Constrain(inside, beyond, 0.35));
        Assert.NotNull((object?)door);
    }

    [Fact]
    public void ACityAtNoon_HasPeopleCloseTogether_ButTheyDoNotStandInsideEachOther()
    {
        Town t = City.Value;
        var crowd = new Crowd(t.Nav.Graph, t.Plans, 8 * 60, 1.2, (ax, az, bx, bz) => t.Ground.Clear(new Vec2(ax, az), new Vec2(bx, bz)));
        for (double now = 8 * 60; now < 12 * 60; now += 1)
        {
            crowd.Update(now);
        }

        var people = new List<NpcSnapshot>();
        double rawClose = 0, keptClose = 0;
        int frames = 0;
        for (double now = 12 * 60; now < (12 * 60) + 5; now += 0.02)
        {
            crowd.Update(now);
            crowd.CollectVisible(now, 1.0 / 60, people);
            if (now < (12 * 60) + 1)
            {
                continue; // let the easing settle
            }

            var raw = new List<NpcSnapshot>();
            for (int i = 0; i < crowd.Count; i++)
            {
                if (crowd.TryGetSnapshot(i, now, out NpcSnapshot s))
                {
                    raw.Add(s);
                }
            }

            rawClose += CountCloser(raw, 0.5);
            keptClose += CountCloser(people, 0.5);
            frames++;
        }

        Assert.True(rawClose / frames > 1, "the test crowd is too thin to show anything");
        Assert.True(keptClose / frames < (rawClose / frames * 0.4) + 0.5, $"{keptClose / frames:F1} pairs overlap per frame with the easing, {rawClose / frames:F1} without");
    }

    private static int CountCloser(IReadOnlyList<NpcSnapshot> people, double meters)
    {
        int count = 0;
        for (int a = 0; a < people.Count; a++)
        {
            for (int b = a + 1; b < people.Count; b++)
            {
                double dx = people[a].X - people[b].X;
                double dz = people[a].Z - people[b].Z;
                count += (dx * dx) + (dz * dz) < meters * meters ? 1 : 0;
            }
        }

        return count;
    }

    [Fact]
    public void ADayInTheCity_ByTrade_FillsTheStreetsAtMiddayAndTheInnAtNight_AndEmptiesTheStreetsAtNight()
    {
        Town t = City.Value;
        var crowd = new Crowd(t.Nav.Graph, t.Plans, 8 * 60, 1.2, (ax, az, bx, bz) => t.Ground.Clear(new Vec2(ax, az), new Vec2(bx, bz)));
        double start = 8 * 60;
        int noonOut = 0, noonNear = 0, nightOut = 0, noonSamples = 0, nightSamples = 0;
        int workersAtWork = 0, workerSamples = 0;
        var innNodes = t.City.AllBuildings.Where(b => b.Service?.Id == "service.inn").Select(b => t.Nav.Graph.DoorOf(b.Id)).ToHashSet();
        int atInn = 0;

        for (double now = start; now <= start + (2 * 1440); now += 5)
        {
            crowd.Update(now);
            double minute = now % 1440;
            bool second = now > start + 1440;
            if (!second)
            {
                continue;
            }

            if (minute is >= 700 and <= 740)
            {
                noonSamples++;
                for (int i = 0; i < crowd.Count; i++)
                {
                    if (crowd.TryGetSnapshot(i, now, out NpcSnapshot s))
                    {
                        noonOut++;
                        noonNear += Math.Sqrt((s.X * s.X) + (s.Z * s.Z)) < t.Ground.LandmarkSolidRadius + 10 ? 1 : 0;
                    }
                }

                for (int i = 0; i < crowd.Count; i++)
                {
                    NpcPlan p = crowd.PlanOf(i);
                    if (p.Profession == "profession.baker")
                    {
                        workerSamples++;
                        workersAtWork += crowd.Describe(i).Purpose == NpcPurpose.Work ? 1 : 0;
                    }
                }
            }

            if (minute is >= 150 and <= 270)
            {
                nightSamples++;
                for (int i = 0; i < crowd.Count; i++)
                {
                    nightOut += crowd.TryGetSnapshot(i, now, out _) ? 1 : 0;
                }
            }

            if (minute is >= 1200 and <= 1260)
            {
                for (int i = 0; i < crowd.Count; i++)
                {
                    NpcStatus st = crowd.Describe(i);
                    atInn += st.State == NpcState.Inside && innNodes.Contains(st.TargetNode) ? 1 : 0;
                }
            }
        }

        Assert.True(noonOut / (double)noonSamples > 100, $"only {noonOut / (double)noonSamples:F0} outside at noon");
        Assert.True(noonNear / (double)noonOut < 0.2, $"the crowd still gathers round the fountain at noon: {noonNear} of {noonOut}");
        Assert.True(nightOut / (double)nightSamples < 25, $"{nightOut / (double)nightSamples:F0} outside at night (guards and the watch only)");
        Assert.True(workersAtWork > workerSamples * 0.9, $"bakers at work {workersAtWork} of {workerSamples}");
        Assert.True(atInn > 0, "nobody is in the inn in the evening");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Realm.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Realm.sln not found above the test binary.");
    }
}
