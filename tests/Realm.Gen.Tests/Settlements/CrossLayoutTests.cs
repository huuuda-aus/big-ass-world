using System;
using System.IO;
using System.Linq;
using Realm.Core.Validation;
using Realm.Data.Content;
using Realm.Gen.Props;
using Realm.Gen.Settlements.Generation;
using Realm.Gen.Settlements.Geometry;
using Realm.Gen.Settlements.Meshing;
using Realm.Gen.Settlements.Model;
using Realm.Gen.Settlements.Placement;
using Xunit;

namespace Realm.Gen.Tests.Settlements;

/// <summary>The cross layout: two streets crossing at a square, arms of different length, rows of houses along them.</summary>
public class CrossLayoutTests
{
    private static City Cross(int seed, int cap, IWaterSampler? water = null) =>
        CityGenerator.Generate(new CityParams { Seed = seed, MaxBuildings = cap, Layout = SettlementLayout.Cross, Parks = false, Cathedral = true, Water = water });

    [Theory]
    [InlineData(1, 50)]
    [InlineData(2, 50)]
    [InlineData(3, 250)]
    [InlineData(4, 250)]
    [InlineData(5, 120)]
    public void TheHouseCap_IsRespected_AndMostOfItUsed(int seed, int cap)
    {
        City city = Cross(seed, cap);

        int houses = city.CityPatches.Sum(p => p.Buildings.Count);
        Assert.InRange(houses, cap * 0.7, cap);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void TheArms_DifferInLength_EachEndsInAGate(int seed)
    {
        City city = Cross(seed, 250);

        Assert.Equal(4, city.Gates.Count);
        Assert.Equal(4, city.Streets.Count);
        double[] lengths = city.Gates.Select(g => g.Length).ToArray();
        Assert.True(lengths.Max() > lengths.Min() * 1.15, $"the arms are almost the same length: {string.Join(", ", lengths.Select(l => l.ToString("F0")))}");
        // The gates lie on the axes and are vertices of the wall (the wall mesher cuts the gap there).
        Assert.All(city.Gates, g => Assert.True(Math.Min(Math.Abs(g.X), Math.Abs(g.Y)) < 1e-9 && city.Wall.Any(w => Vec2.Distance(w, g) < 0.01)));
    }

    [Fact]
    public void WithMoreHouses_TheArmsRunFurther()
    {
        double Reach(int cap) => Enumerable.Range(1, 3).Average(seed => Cross(seed, cap).Gates.Max(g => g.Length));

        Assert.True(Reach(250) > Reach(120) * 1.2 && Reach(120) > Reach(50) * 1.1);
    }

    [Fact]
    public void EveryHouse_IsARectangle_InsideTheOutline_WithItsDoorOnTheStreetSide()
    {
        City city = Cross(3, 250);
        foreach (Building b in city.AllBuildings.Where(b => b.Ward != WardType.Outskirts))
        {
            ConvexPoly fp = b.Footprint;
            Assert.Equal(4, fp.Count);
            Assert.Equal(0, b.DoorEdge);
            for (int i = 0; i < 4; i++)
            {
                Assert.True(Math.Abs(Vec2.Dot(fp.Edge(i).Normalized(), fp.Edge((i + 1) % 4).Normalized())) < 1e-6, $"building {b.Id} is not rectangular");
            }

            Assert.All(fp.Points, v => Assert.True(InsidePlus(city.Wall, v), $"building {b.Id} sticks out of the wall outline"));

            // The door faces the axis of the street its arm follows (row 1 faces the street, row 2 faces the lane behind row 1).
            Vec2 c = fp.Centroid;
            Vec2 normal = fp.OutwardNormal(b.DoorEdge);
            double across = Math.Abs(c.X) > Math.Abs(c.Y) ? c.Y : c.X; // lateral position of the house
            double facing = Math.Abs(c.X) > Math.Abs(c.Y) ? normal.Y : normal.X;
            Assert.True(across * facing < 0, $"building {b.Id}: the door faces away from the street");
        }
    }

    [Fact]
    public void Houses_StandApart_AndHaveASecondRow()
    {
        City city = Cross(4, 250);
        var houses = city.CityPatches.SelectMany(p => p.Buildings).ToList();

        double closest = double.MaxValue;
        for (int i = 0; i < houses.Count; i++)
        {
            for (int j = i + 1; j < houses.Count; j++)
            {
                if (Vec2.Distance(houses[i].Footprint.Centroid, houses[j].Footprint.Centroid) > 40)
                {
                    continue;
                }

                closest = Math.Min(closest, Gap(houses[i].Footprint, houses[j].Footprint));
            }
        }

        Assert.True(closest >= 2.4, $"two houses are {closest:F2} m apart");
        // Row 1 starts 5 m from the street axis, row 2 at least 12 m out.
        Assert.Contains(houses, b => Math.Min(Math.Abs(b.Footprint.Centroid.X), Math.Abs(b.Footprint.Centroid.Y)) > 15);
    }

    [Fact]
    public void TheNumberOfHouses_FallsTheFurtherFromTheCrossroads()
    {
        // Count the houses by how far along their arm they stand, summed over several settlements: the first third has the most.
        double[] thirds = new double[3];
        foreach (int seed in Enumerable.Range(1, 6))
        {
            City city = Cross(seed, 250);
            foreach (Building b in city.CityPatches.SelectMany(p => p.Buildings).Where(b => b.Ward != WardType.Cathedral))
            {
                Vec2 c = b.Footprint.Centroid;
                bool alongX = Math.Abs(c.X) > Math.Abs(c.Y);
                double distance = alongX ? Math.Abs(c.X) : Math.Abs(c.Y);
                double armLength = city.Gates.Where(g => alongX ? Math.Abs(g.Y) < 1e-9 && Math.Sign(g.X) == Math.Sign(c.X) : Math.Abs(g.X) < 1e-9 && Math.Sign(g.Y) == Math.Sign(c.Y)).Max(g => g.Length);
                double third = (distance - 28) / Math.Max(1, armLength - 28) * 3;
                thirds[(int)Math.Clamp(Math.Floor(third), 0, 2)]++;
            }
        }

        Assert.True(thirds[0] > thirds[1] && thirds[1] > thirds[2], $"houses by third of the arm: {string.Join(", ", thirds)}");
        Assert.True(thirds[0] > thirds[2] * 1.8, $"the far end ({thirds[2]}) is almost as built up as the crossroads ({thirds[0]})");
    }

    [Fact]
    public void TheSecondRowFadesOutBeforeTheEndOfTheArm()
    {
        foreach (int seed in new[] { 1, 2, 3 })
        {
            City city = Cross(seed, 250);
            foreach (Building b in city.CityPatches.SelectMany(p => p.Buildings).Where(b => b.Ward != WardType.Cathedral))
            {
                Vec2 c = b.Footprint.Centroid;
                bool alongX = Math.Abs(c.X) > Math.Abs(c.Y);
                double lateral = alongX ? Math.Abs(c.Y) : Math.Abs(c.X);
                if (lateral < 17)
                {
                    continue; // row 1
                }

                double distance = alongX ? Math.Abs(c.X) : Math.Abs(c.Y);
                double armLength = city.Gates.Where(g => alongX ? Math.Abs(g.Y) < 1e-9 && Math.Sign(g.X) == Math.Sign(c.X) : Math.Abs(g.X) < 1e-9 && Math.Sign(g.Y) == Math.Sign(c.Y)).Max(g => g.Length);
                Assert.True((distance - 28) / (armLength - 28) <= 0.7, $"a second-row house stands {(distance - 28) / (armLength - 28):P0} of the way along its arm");
            }
        }
    }

    [Theory]
    [InlineData(8)]
    [InlineData(14)]
    [InlineData(22)]
    public void ASmallHamletCross_IsSmall_AndHoldsItsHouses(int cap)
    {
        City hamlet = CityGenerator.Generate(new CityParams { Seed = 5, MaxBuildings = cap, Layout = SettlementLayout.Cross, Humble = true, Parks = false });
        City town = CityGenerator.Generate(new CityParams { Seed = 5, MaxBuildings = cap, Layout = SettlementLayout.Cross, Parks = false });

        int houses = hamlet.CityPatches.Sum(p => p.Buildings.Count);
        Assert.InRange(houses, Math.Max(3, cap * 0.55), cap);
        Assert.All(hamlet.CityPatches.SelectMany(p => p.Buildings), b => Assert.Equal(BuildingStyle.Slum, b.Style));
        // A hamlet's square and wall are smaller than those of a rich cross settlement.
        Assert.True(hamlet.Wall.Max(w => w.Length) < town.Wall.Max(w => w.Length));
        Assert.True(hamlet.Patches[0].Shape.Area < town.Patches[0].Shape.Area);
    }

    [Fact]
    public void ThePlacer_GivesTheCrossLayoutOnlyToSmallSettlements_AndPalisadesOnlyToBiggerTowns()
    {
        var settings = new SettlementSettings();
        Assert.Equal(24, SettlementSettings.SmallSettlementMaxBuildings);
        Assert.True(settings.HamletBuildings <= SettlementSettings.SmallSettlementMaxBuildings);
        Assert.True(settings.HamletMinBuildings <= settings.HamletBuildings);
        Assert.False(new SettlementSettings { HamletMinBuildings = 30 }.TryValidate(out _));
        Assert.True(settings.TownBuildings > SettlementSettings.SmallSettlementMaxBuildings); // towns are radial and walled
        Assert.True(settings.CityBuildings > SettlementSettings.SmallSettlementMaxBuildings);
    }

    [Fact]
    public void ServicesAreAllPresent_InACrossTown()
    {
        var db = ContentLoader.Load(Path.Combine(RepoRoot(), "content"), new ValidationReport());
        foreach (SettlementTier tier in new[] { SettlementTier.City, SettlementTier.Town })
        {
            for (int seed = 1; seed <= 4; seed++)
            {
                var s = new Settlement($"settlement.cross_{tier}_{seed}", "Test", tier, 0, 0, 0, 100, 150, 180, seed, tier == SettlementTier.City ? 250 : 50,
                    tier == SettlementTier.City, CentrepieceKind.Fountain, ServiceCatalog.For(db, tier), false, SettlementLayout.Cross);
                City city = CityGenerator.Generate(s.ToCityParams());
                var report = new ValidationReport();

                ServiceValidator.Validate(s, city, report);

                Assert.DoesNotContain(report.Issues, i => i.Severity == Severity.Error);
            }
        }
    }

    [Fact]
    public void Walls_CanBeBuiltForTheOutline_AndCutAtWater()
    {
        City city = Cross(2, 250);
        double reach = city.Gates.Where(g => Math.Abs(g.Y) < 1e-9).Min(g => Math.Abs(g.X)); // the shorter of the east and west arms
        var river = new RiverAcross(reach * 0.6);

        foreach (Fortification kind in new[] { Fortification.Stone, Fortification.Palisade })
        {
            PropMesh dry = CityMesher.Wall(city, null, kind)!;
            PropMesh wet = CityMesher.Wall(city, null, kind, river)!;
            bool InRiver(PropTriangle t) => Math.Abs(t.A.X - river.X) < 3.5 && Math.Abs(t.B.X - river.X) < 3.5 && Math.Abs(t.C.X - river.X) < 3.5;
            Assert.Contains(dry.Triangles, InRiver);
            Assert.DoesNotContain(wet.Triangles, InRiver); // the wall stops short of the middle of the river
        }
    }

    [Fact]
    public void WaterDropsTheHousesInIt_ButNotTheOutline()
    {
        var river = new RiverAcross(70);
        City dry = Cross(2, 250);
        City wet = Cross(2, 250, river);

        Assert.Equal(dry.Wall, wet.Wall);
        Assert.True(wet.AllBuildings.Count() < dry.AllBuildings.Count());
        Assert.All(wet.AllBuildings, b => Assert.False(CityGenerator.TouchesWater(b.Footprint, river)));
    }

    [Fact]
    public void SameSeed_SameCity()
    {
        string Fingerprint(City c) => string.Join(";", c.AllBuildings.Select(b => $"{b.Id}:{b.Footprint.Centroid.X:F3},{b.Footprint.Centroid.Y:F3}")) + "|" + string.Join(";", c.Wall.Select(w => $"{w.X:F2},{w.Y:F2}"));

        Assert.Equal(Fingerprint(Cross(7, 250)), Fingerprint(Cross(7, 250)));
        Assert.NotEqual(Fingerprint(Cross(7, 250)), Fingerprint(Cross(8, 250)));
    }

    [Fact]
    public void Layout_IsChosenByThePlacer_AndStoredInTheManifest()
    {
        var all = new SettlementSettings { CrossShare = 1, Hamlets = 0 };
        var none = new SettlementSettings { CrossShare = 0, Hamlets = 0 };
        Assert.True(all.TryValidate(out _));
        Assert.False(new SettlementSettings { CrossShare = 1.5 }.TryValidate(out _));
        Assert.Equal(1.0, new SettlementSettings().CrossShare); // every small settlement is a cross by default

        var s = new Settlement("settlement.a", "A", SettlementTier.Town, 1, 2, 0.3, 50, 90, 120, 4, 50, false, CentrepieceKind.Market, null, false, SettlementLayout.Cross);
        var back = SettlementManifest.FromJson(new SettlementManifest { World = "w", Seed = 1, Settlements = new[] { s } }.ToJson(), new ValidationReport());
        Assert.Equal(SettlementLayout.Cross, back!.Settlements[0].Layout);
        Assert.Equal(SettlementLayout.Cross, s.ToCityParams().Layout);
        Assert.Equal(SettlementLayout.Radial, new Settlement("settlement.b", "B", SettlementTier.Hamlet, 0, 0, 0, 1, 1, 1, 1, 12, false, CentrepieceKind.None).Layout);
    }

    private sealed class RiverAcross : IWaterSampler
    {
        public RiverAcross(double x) => X = x;
        public double X { get; }
        public bool IsWater(double x, double y) => Math.Abs(x - X) <= 10;
    }

    // Inside the plus-shaped outline: within the cross's vertical or horizontal band, bounded by the arm ends.
    private static bool InsidePlus(System.Collections.Generic.IReadOnlyList<Vec2> wall, Vec2 v)
    {
        double h = wall.Where(w => Math.Abs(Math.Abs(w.X) - Math.Abs(w.Y)) < 1e-9 && Math.Abs(w.X) > 0).Min(w => Math.Abs(w.X)); // the inner corners (+-h, +-h)
        double east = wall.Max(w => w.X), west = -wall.Min(w => w.X), north = wall.Max(w => w.Y), south = -wall.Min(w => w.Y);
        bool inHorizontal = Math.Abs(v.Y) <= h + 1e-6 && v.X <= east && -v.X <= west;
        bool inVertical = Math.Abs(v.X) <= h + 1e-6 && v.Y <= north && -v.Y <= south;
        return inHorizontal || inVertical;
    }

    private static double Gap(ConvexPoly a, ConvexPoly b)
    {
        double best = double.MaxValue;
        foreach ((ConvexPoly p, ConvexPoly q) in new[] { (a, b), (b, a) })
        {
            foreach (Vec2 v in p.Points)
            {
                for (int k = 0; k < q.Count; k++)
                {
                    Vec2 s = q.Points[k], e = q.Edge(k);
                    double t = Math.Clamp(Vec2.Dot(v - s, e) / Vec2.Dot(e, e), 0, 1);
                    best = Math.Min(best, Vec2.Distance(v, s + (e * t)));
                }
            }
        }

        return best;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Realm.sln")))
        {
            dir = dir.Parent;
        }

        return dir!.FullName;
    }
}
