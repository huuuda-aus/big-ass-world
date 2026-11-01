using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Realm.Core.Random;
using Realm.Data.Content;
using Realm.Gen.Props;
using Realm.Gen.Settlements.Generation;
using Realm.Gen.Settlements.Geometry;
using Realm.Gen.Settlements.Meshing;
using Realm.Gen.Settlements.Model;
using Realm.Gen.Settlements.Placement;
using Xunit;

namespace Realm.Gen.Tests.Settlements;

/// <summary>What protects a settlement depends on its tier: stone walls, a wooden palisade, or nothing.</summary>
public class FortificationTests
{
    private static readonly City Town = CityGenerator.Generate(new CityParams { Seed = 8, MaxBuildings = 50 });

    public static IEnumerable<object[]> Seeds => Enumerable.Range(1, 30).Select(s => new object[] { s });

    private static Settlement Of(SettlementTier tier, int buildings = 50) =>
        new($"settlement.{tier}", "Test", tier, 0, 0, 0, 100, 100, 130, 1, buildings, false, CentrepieceKind.None);

    [Fact]
    public void PalisadeLogs_OverlapWithNoGaps_AndVaryInSizeAndHeight()
    {
        var from = new Vec2(0, 0);
        var to = new Vec2(30, 0);

        var logs = CityMesher.LogsAlong(from, to);

        // The front row (offset 0) is continuous: each neighbour is closer than the two radii together.
        var front = logs.Where(l => System.Math.Abs(l.At.Y) < 0.01).OrderBy(l => l.At.X).ToList();
        for (int i = 1; i < front.Count; i++)
        {
            Assert.True(front[i].At.X - front[i - 1].At.X < front[i].Radius + front[i - 1].Radius, $"gap before log {i}");
        }

        Assert.True(front[0].At.X <= 0 && front[^1].At.X >= 30, "the run ends are not covered");
        Assert.True(logs.Select(l => l.Height).Max() - logs.Select(l => l.Height).Min() > 0.3, "heights do not vary");
        Assert.True(logs.Select(l => l.Radius).Distinct().Count() > 5, "thickness does not vary");
    }

    [Fact]
    public void TheTier_ChoosesTheFortification()
    {
        Assert.Equal(Fortification.Stone, Of(SettlementTier.City).Fortification);
        Assert.Equal(Fortification.Palisade, Of(SettlementTier.Town).Fortification);
        Assert.Equal(Fortification.None, Of(SettlementTier.Hamlet).Fortification);
    }

    [Theory]
    [InlineData(24, Fortification.None)]
    [InlineData(25, Fortification.Palisade)]
    [InlineData(10, Fortification.None)]
    [InlineData(80, Fortification.Palisade)]
    public void ASmallSettlement_UnderTwentyFiveHouses_HasNoPalisade(int buildings, Fortification expected)
    {
        Assert.Equal(24, SettlementSettings.SmallSettlementMaxBuildings);
        Assert.Equal(expected, Of(SettlementTier.Town, buildings).Fortification);
        Assert.Equal(Fortification.Stone, Of(SettlementTier.City, 10).Fortification); // a city keeps its wall whatever its size
    }

    [Fact]
    public void NoFortification_MeansNoWallMesh()
    {
        Assert.Null(CityMesher.Wall(Town, null, Fortification.None));
        Assert.Null(CityMesher.Build(Town, 8, null, Fortification.None).Wall);
        Assert.NotNull(CityMesher.Build(Town, 8, null, Fortification.Palisade).Wall);
    }

    [Fact]
    public void Palisade_IsWoodAndLowerThanAStoneWall_WithGatesLeftOpen()
    {
        PropMesh stone = CityMesher.Wall(Town, null, Fortification.Stone)!;
        PropMesh palisade = CityMesher.Wall(Town, null, Fortification.Palisade)!;

        Assert.DoesNotContain(palisade.Triangles, t => t.Colour == SettlementPalette.Wall);
        Assert.Contains(stone.Triangles, t => t.Colour == SettlementPalette.Wall);
        // Logs are 3.2 to 4.0 m; the watchtowers (6.5 m posts and a roof) are the tallest part.
        Assert.True(palisade.Max.Y < stone.Max.Y, $"palisade {palisade.Max.Y} m, stone {stone.Max.Y} m");
        Assert.InRange(palisade.Max.Y, 6.5f, 9.5f);
        // Looking straight down no log stands in the middle of a gate.
        foreach (Vec2 gate in Town.Gates)
        {
            Assert.DoesNotContain(palisade.Triangles, t => Distance(t, gate) < 1.5);
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Watchtowers_BackPostsStandInsideThePalisade(int seed)
    {
        // The platform's front edge is meant to sit flush with (a little past) the wall line, where the wall
        // itself stands in for the front posts, so only the two back posts are checked here. A post tube's only
        // triangle with every vertex above 4 m is its top cap (its side quads mix a low and a high vertex), and
        // Colour == Log(32) above that height is unique to a watchtower post (everything else in a palisade uses
        // a hash under 32, which collapses to the same shade as 0: Log's shade index is (hash >> 5) % count).
        City city = seed == 8 ? Town : CityGenerator.Generate(new CityParams { Seed = seed, MaxBuildings = 50 });
        PropMesh palisade = CityMesher.Wall(city, null, Fortification.Palisade)!;
        var postCaps = palisade.Triangles.Where(t => t.Colour == SettlementPalette.Log(32) && t.A.Y > 4f && t.B.Y > 4f && t.C.Y > 4f).ToList();
        Assert.NotEmpty(postCaps);

        foreach (PropTriangle t in postCaps)
        {
            foreach (Vector3 v in new[] { t.A, t.B, t.C })
            {
                var p = new Vec2(v.X, v.Z);
                Assert.True(PointInPolygon(city.Wall, p), $"seed {seed}: a watchtower back post at {v} is outside the palisade");
            }
        }
    }

    [Fact]
    public void Build_WithAPalisade_ExposesLadders_OneAtEveryWatchtower_ReachingFromGroundToThePlatform()
    {
        CityMeshes meshes = CityMesher.Build(Town, Town.Seed, null, Fortification.Palisade);

        Assert.NotEmpty(meshes.Ladders);
        foreach (Ladder ladder in meshes.Ladders)
        {
            Assert.True(ladder.TopY > ladder.BottomY, $"ladder at {ladder.Position}: top {ladder.TopY} is not above bottom {ladder.BottomY}");
            Assert.True(PointInPolygon(Town.Wall, ladder.Position), $"ladder at {ladder.Position} is outside the palisade");
            Assert.True(PointInPolygon(Town.Wall, ladder.TopPosition), $"ladder top landing at {ladder.TopPosition} is outside the palisade");
            // The top landing point must clear the floor's hole (0.5 m half-width) by at least a body radius
            // (0.4 m) plus a little margin, or arriving there lands in the hole, which has no floor.
            Assert.True(Vec2.Distance(ladder.Position, ladder.TopPosition) >= 0.95, $"ladder top landing at {ladder.TopPosition} is only {Vec2.Distance(ladder.Position, ladder.TopPosition):F2} m from the hole's centre");
        }
    }

    [Fact]
    public void Build_WithAPalisade_TheRoofClearsAStandingPlayerAboveTheLadder()
    {
        PropMesh palisade = CityMesher.Wall(Town, null, Fortification.Palisade)!;
        CityMeshes meshes = CityMesher.Build(Town, Town.Seed, null, Fortification.Palisade);
        double roofBaseY = palisade.Triangles.Where(t => t.Colour == SettlementPalette.SlumRoof).SelectMany(t => new[] { t.A.Y, t.B.Y, t.C.Y }).Min();

        // 1.8 m is the player capsule's height (WorldScene.BodyHeight); Realm.Gen does not reference it directly
        // (it stays Godot-free), so this is the one place that would catch the two falling out of step again.
        foreach (Ladder ladder in meshes.Ladders)
        {
            Assert.True(roofBaseY - ladder.TopY >= 1.8, $"only {roofBaseY - ladder.TopY:F2} m of headroom above the ladder's top landing at {ladder.TopPosition}, less than the player's 1.8 m height");
        }
    }

    [Fact]
    public void Build_WithAPalisade_HasAWaistHighRailingAroundThePlatform()
    {
        PropMesh palisade = CityMesher.Wall(Town, null, Fortification.Palisade)!;
        CityMeshes meshes = CityMesher.Build(Town, Town.Seed, null, Fortification.Palisade);
        double floorTopY = meshes.Ladders[0].TopY; // flat ground: every tower's floor sits at the same height

        // Colour == Log(1) above the floor's own top surface is the railing specifically: the front braces share
        // the colour but their top end sits just under the floor (platformY - 0.1), never reaching it.
        var railing = palisade.Triangles.Where(t => t.Colour == SettlementPalette.Log(1) && t.A.Y >= floorTopY - 0.05f && t.B.Y >= floorTopY - 0.05f && t.C.Y >= floorTopY - 0.05f).ToList();
        Assert.NotEmpty(railing);

        double top = railing.SelectMany(t => new[] { t.A.Y, t.B.Y, t.C.Y }).Max();
        Assert.True(top - floorTopY >= 0.9, $"railing only reaches {top - floorTopY:F2} m above the floor, expected close to waist height (1.0 m)");
        // Posts alone (thin upright boxes) are a few hundred triangles across all four edges; the round horizontal
        // log courses stacked tight between them (no gap: each spaced less than a log's diameter from the next,
        // LogSides = 8 round faces each) add several hundred more, so a low count here means the fill courses,
        // not just the posts, are missing or too sparse.
        Assert.True(railing.Count >= 500, $"only {railing.Count} railing triangles, expected the posts plus tightly stacked horizontal fill courses");
    }

    [Fact]
    public void Build_WithAStoneWall_HasNoLadders()
    {
        CityMeshes meshes = CityMesher.Build(Town, Town.Seed, null, Fortification.Stone);

        Assert.Empty(meshes.Ladders);
    }

    // Standard even-odd ray-casting test; correct for any simple polygon, convex or not.
    private static bool PointInPolygon(IReadOnlyList<Vec2> poly, Vec2 p)
    {
        bool inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            Vec2 a = poly[i], c = poly[j];
            if ((a.Y > p.Y) != (c.Y > p.Y) && p.X < ((c.X - a.X) * (p.Y - a.Y) / (c.Y - a.Y)) + a.X)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    [Fact]
    public void Palisade_FollowsTheGround_AndHasManyLogs()
    {
        var slope = new TiltedGround(0.25);
        PropMesh flat = CityMesher.Wall(Town, null, Fortification.Palisade)!;
        PropMesh tilted = CityMesher.Wall(Town, slope, Fortification.Palisade)!;

        double low = Town.Wall.Min(v => slope.HeightAt(v.X, v.Y));
        double high = Town.Wall.Max(v => slope.HeightAt(v.X, v.Y));
        Assert.True(tilted.Min.Y <= low - 0.2, "the logs do not reach down to the low ground");
        Assert.True(tilted.Max.Y >= high + 3, "the logs on the high ground are too short");
        Assert.True(flat.Triangles.Count > 500, "a palisade is made of many logs");
    }

    [Fact]
    public void Hamlets_AreSmallPoorAndHaveFewServices()
    {
        City hamlet = CityGenerator.Generate(Of(SettlementTier.Hamlet, 14).ToCityParams());

        Assert.True(hamlet.AllBuildings.Count() <= 14 + 3);
        Assert.All(hamlet.CityPatches.SelectMany(p => p.Buildings), b => Assert.Equal(BuildingStyle.Slum, b.Style));
        Assert.True(hamlet.CityPatches.All(p => p.Ward is WardType.Market or WardType.Slum));
        Assert.Equal(SettlementLayout.Radial, Of(SettlementTier.Hamlet, 14).Layout); // a Settlement is radial unless the placer says otherwise
    }

    [Fact]
    public void Hamlets_GetTheirOwnNamesAndServices()
    {
        var used = new System.Collections.Generic.HashSet<string>();
        string name = SettlementNamer.Generate(Rng.ForKey(1UL, "name"), SettlementTier.Hamlet, used);
        Assert.Matches("(thorpe|cot|holt|fold|end|shaw|green|side|well|wyke)$", name);

        var db = ContentLoader.Load(System.IO.Path.Combine(RepoRoot(), "content"), new Realm.Core.Validation.ValidationReport());
        var services = ServiceCatalog.For(db, SettlementTier.Hamlet).Select(s => s.Id).ToList();
        Assert.Equal(new[] { "service.farmer", "service.woodcutter" }, services);
        Assert.Contains("hamlet", ServiceVocabulary.Tiers);
    }

    [Fact]
    public void SettingsRejectAHamletHeightBelowTheMinimum()
    {
        Assert.False(new SettlementSettings { HamletMaxHeightMeters = 5 }.TryValidate(out _));
        Assert.True(new SettlementSettings().TryValidate(out _));
    }

    private static double Distance(PropTriangle t, Vec2 gate) =>
        System.Math.Min(System.Math.Min(Vec2.Distance(new Vec2(t.A.X, t.A.Z), gate), Vec2.Distance(new Vec2(t.B.X, t.B.Z), gate)), Vec2.Distance(new Vec2(t.C.X, t.C.Z), gate));

    private sealed class TiltedGround : IGroundSampler
    {
        private readonly double _dx;
        public TiltedGround(double dx) => _dx = dx;
        public double HeightAt(double x, double y) => x * _dx;
    }

    private static string RepoRoot()
    {
        var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "Realm.sln")))
        {
            dir = dir.Parent;
        }

        return dir!.FullName;
    }
}
