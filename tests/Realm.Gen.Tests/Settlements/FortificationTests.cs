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
