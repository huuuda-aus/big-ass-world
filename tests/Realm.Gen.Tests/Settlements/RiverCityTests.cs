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
using Realm.Gen.Terrain.Erosion;
using Xunit;

namespace Realm.Gen.Tests.Settlements;

/// <summary>River widths, a city halved by a river, the bank buffer and the arched bridges.</summary>
public class RiverCityTests
{
    /// <summary>A river along plan X = offset, with a given half width.</summary>
    private sealed class Strip : IWaterSampler
    {
        public Strip(double offset, double halfWidth)
        {
            Offset = offset;
            HalfWidth = halfWidth;
        }

        public double Offset { get; }
        public double HalfWidth { get; }
        public bool IsWater(double x, double y) => Math.Abs(x - Offset) <= HalfWidth;
    }

    private static City Generate(IWaterSampler? water, SettlementLayout layout = SettlementLayout.Radial, int cap = 330, int seed = 4) =>
        CityGenerator.Generate(new CityParams { Seed = seed, MaxBuildings = cap, Layout = layout, Parks = false, Cathedral = true, Water = water, WaterBufferMeters = 10 });

    // ---- widths ----------------------------------------------------------------------------------------------

    [Fact]
    public void RiversAreAboutThreeAndAHalfTimesAsWideAsBefore()
    {
        Assert.Equal(7.0, RiverWidth.BaseHalfWidthMeters(0), 6);        // was 2 m
        Assert.Equal(2.1 * 10, RiverWidth.BaseHalfWidthMeters(100), 6); // was 0.6 * 10
        Assert.Equal(3.5, RiverWidth.MinimumHalfWidthMeters / 2.0, 6);
        Assert.Equal(3.5, RiverWidth.HalfWidthPerRootKm2 / 0.6, 6);
    }

    // A river of 40 km2 falling at the given slope, 32 m between points.
    private static List<RiverPoint> Course(int points, double fall, double flow = 40) =>
        Enumerable.Range(0, points).Select(i => new RiverPoint(i * 32.0, 0, 200 - (i * 32.0 * fall), flow)).ToList();

    [Fact]
    public void TheWidth_VariesALotAlongARiver_ButNeverJumps()
    {
        double baseWidth = RiverWidth.BaseHalfWidthMeters(40);

        List<RiverPoint> river = RiverWidth.Apply(Course(160, 0.012), 3);

        double[] widths = river.Select(p => p.HalfWidthMeters).ToArray();
        Assert.InRange(widths.Min() / baseWidth, RiverWidth.LeastFactor - 0.001, 0.85);
        Assert.True(widths.Max() / baseWidth > 1.3, "no stretch is wider than the base");
        Assert.True(widths.Max() / widths.Min() > 1.8, $"the river is almost the same width all along ({widths.Min():F1} to {widths.Max():F1} m)");
        Assert.True(widths.Zip(widths.Skip(1), (a, b) => Math.Max(a / b, b / a)).Max() <= RiverWidth.MaxStep + 1e-9, "the width jumps between neighbouring points");
        Assert.All(widths, w => Assert.InRange(w / baseWidth, RiverWidth.LeastFactor - 1e-9, RiverWidth.MostFactor + 1e-9));
        Assert.Equal(widths, RiverWidth.Apply(Course(160, 0.012), 3).Select(p => p.HalfWidthMeters).ToArray()); // deterministic
        Assert.NotEqual(widths, RiverWidth.Apply(Course(160, 0.012), 4).Select(p => p.HalfWidthMeters).ToArray()); // each river varies differently
    }

    [Fact]
    public void GentleReachesAreWiderThanSteepOnes_AndASourceStartsNarrow()
    {
        double Mean(double fall) => RiverWidth.Apply(Course(160, fall), 2).Skip(10).Average(p => p.HalfWidthMeters);

        Assert.True(Mean(0.002) > Mean(0.012) * 1.15, "a gentle reach is not wider than an ordinary one");
        Assert.True(Mean(0.012) > Mean(0.12) * 1.2, "a steep reach is not narrower than an ordinary one");
        List<RiverPoint> river = RiverWidth.Apply(Course(60, 0.012), 2);
        Assert.True(river[0].HalfWidthMeters < RiverWidth.BaseHalfWidthMeters(40) * 0.75, "the source is not narrow");
    }

    [Fact]
    public void ARiverOpensIntoAnEstuaryAtTheSea_AndIntoAPondWhereItDriesUp()
    {
        double baseWidth = RiverWidth.BaseHalfWidthMeters(40);
        List<RiverPoint> course = Course(120, 0.012);

        List<RiverPoint> toSea = RiverWidth.Apply(course, 2, RiverMouth.Sea);
        List<RiverPoint> dries = RiverWidth.Apply(course, 2, RiverMouth.Dried);
        List<RiverPoint> joins = RiverWidth.Apply(course, 2, RiverMouth.Confluence);

        Assert.True(toSea[^1].HalfWidthMeters > baseWidth * 1.9, $"the mouth is only {toSea[^1].HalfWidthMeters / baseWidth:F1} times the base width");
        Assert.True(dries[^1].HalfWidthMeters > baseWidth * 1.5, "the end of a river that dries up is not a pond");
        Assert.True(toSea[^1].HalfWidthMeters > dries[^1].HalfWidthMeters);
        Assert.True(toSea[^1].HalfWidthMeters > joins[^1].HalfWidthMeters * 1.5);
        // Upstream of the estuary the river is the same whatever the mouth.
        Assert.Equal(joins[10].HalfWidthMeters, toSea[10].HalfWidthMeters, 6);
        // It opens out gradually: the width grows steadily over the last stretch.
        Assert.True(toSea[^1].HalfWidthMeters > toSea[^8].HalfWidthMeters && toSea[^8].HalfWidthMeters > toSea[^20].HalfWidthMeters);
    }

    [Fact]
    public void AnUnsetPointFallsBackToTheBaseWidth()
    {
        Assert.Equal(RiverWidth.BaseHalfWidthMeters(40), new RiverPoint(0, 0, 10, 40).HalfWidth, 6);
        Assert.Equal(33.0, new RiverPoint(0, 0, 10, 40, 33).HalfWidth, 6);
    }

    [Fact]
    public void TheRaster_AndTheWaterMask_FollowTheWidenedRiver()
    {
        var points = new[] { new RiverPoint(-300, 0, 10, 100, 40), new RiverPoint(300, 0, 10, 100, 40) };
        var network = new RiverNetwork(new List<River> { new(0, points, RiverMouth.Sea, null, 600) }, new List<Lake>(), 32);
        var raster = new Realm.Gen.Settlements.Roads.RiverRaster(2048, network);
        var mask = new WaterMask(2048, network);

        Assert.True(raster.IsRiver(0, 35));
        Assert.False(raster.IsRiver(0, 52));
        Assert.True(mask.AnyWithin(0, 36, 10)); // a cell 32 m off the line is inside a 40 m half width
        Assert.False(new WaterMask(2048, new RiverNetwork(new List<River> { new(0, new[] { new RiverPoint(-300, 0, 10, 1, 4), new RiverPoint(300, 0, 10, 1, 4) }, RiverMouth.Sea, null, 600) }, new List<Lake>(), 32)).AnyWithin(0, 200, 10));
    }

    // ---- bridges ---------------------------------------------------------------------------------------------

    private static Street Line(double x0, double y0, double x1, double y1) => new(new[] { new Vec2(x0, y0), new Vec2(x1, y1) });

    [Fact]
    public void ABridge_CarriesAStreetFromBankToBank()
    {
        var river = new Strip(60, 15);

        IReadOnlyList<CityBridge> bridges = CityBridgeFinder.Find(new[] { Line(0, 0, 150, 0) }, river, 2);

        CityBridge b = Assert.Single(bridges);
        Assert.False(river.IsWater(b.From.X, b.From.Y));
        Assert.False(river.IsWater(b.To.X, b.To.Y));
        Assert.InRange(b.From.X, 40, 46); // the bank is at 45; the end is 3 m back on land
        Assert.InRange(b.To.X, 74, 80);
        Assert.Equal(CityBridgeFinder.RoadwayMeters, b.WidthMeters);
    }

    [Fact]
    public void Bridges_AreLimitedToTwo_NearestTheCentreFirst_AndApart()
    {
        var rivers = new MultiStrip(new[] { 50.0, 110.0, 170.0, 175.0 }, 8);
        IReadOnlyList<CityBridge> bridges = CityBridgeFinder.Find(new[] { Line(0, 0, 300, 0), Line(0, 5, 300, 5) }, rivers, 2);

        Assert.Equal(2, bridges.Count);
        Assert.True(bridges[0].From.X < bridges[1].From.X);
        Assert.InRange(bridges[0].From.X, 30, 45);
        Assert.True((bridges[1].From - bridges[0].From).Length > 35);
        Assert.Empty(CityBridgeFinder.Find(new[] { Line(0, 0, 300, 0) }, new Strip(100, 2), 2)); // a stream of 4 m is stepped over
        Assert.Empty(CityBridgeFinder.Find(new[] { Line(0, 0, 400, 0) }, new Strip(200, 70), 2)); // 140 m of water is too wide
    }

    [Fact]
    public void TheArch_RisesToACrown_StartsAndEndsOnTheGround_AndIsWalkable()
    {
        var bridge = new CityBridge(new Vec2(0, 0), new Vec2(40, 0), 7);
        var ground = new Slope(0.02);

        PropMesh mesh = ArchBridgeMesher.Build(bridge, ground);

        double crown = ArchBridgeMesher.CrownMeters(40);
        Assert.Equal(4.0, crown, 6);
        Assert.Equal(ground.HeightAt(0, 0) + 0.05, ArchBridgeMesher.RoadwayHeight(bridge, ground, 0), 6);
        Assert.Equal(ground.HeightAt(40, 0) + 0.05, ArchBridgeMesher.RoadwayHeight(bridge, ground, 1), 6);
        double middle = ArchBridgeMesher.RoadwayHeight(bridge, ground, 0.5);
        Assert.Equal(((ground.HeightAt(0, 0) + ground.HeightAt(40, 0)) / 2) + 0.05 + crown, middle, 6);

        // The roadway is made of up-facing triangles that reach the crown, and none of them is steeper than a walker can climb.
        var roadway = mesh.Triangles.Where(t => t.Colour == SettlementPalette.Hex(0x9a8f78)).ToList();
        Assert.NotEmpty(roadway);
        Assert.True(roadway.Max(t => Math.Max(t.A.Y, Math.Max(t.B.Y, t.C.Y))) >= middle - 0.01);
        Assert.All(roadway, t => Assert.True(Vector3.Normalize(Vector3.Cross(t.B - t.A, t.C - t.A)).Y > 0.8f, "the roadway is too steep to walk"));

        // The underside is an arch: the bridge is thinner at the crown than at the ends.
        var underside = mesh.Triangles.Where(t => t.Colour == SettlementPalette.Hex(0x6e6a62)).ToList();
        Assert.NotEmpty(underside);
        double MinUnder(double x0, double x1) => underside.Where(t => t.A.X >= x0 && t.A.X <= x1).Min(t => t.A.Y);
        double thicknessAtEnd = ArchBridgeMesher.RoadwayHeight(bridge, ground, 0) - MinUnder(0, 2);
        double thicknessAtCrown = middle - MinUnder(19, 21);
        Assert.True(thicknessAtCrown < thicknessAtEnd - 0.3, $"no arch: {thicknessAtEnd:F2} m at the ends, {thicknessAtCrown:F2} m at the crown");
        // Parapets stand a metre above the roadway.
        Assert.True(mesh.Max.Y >= middle + 0.95f);
    }

    [Fact]
    public void TheArchBridge_HasNoMasonryBeyondItsEnds_OrAboveTheRoadway_SoNothingClipsThroughTheBank()
    {
        var bridge = new CityBridge(new Vec2(10, 0), new Vec2(50, 0), 7);
        var ground = new Slope(0.03);

        PropMesh mesh = ArchBridgeMesher.Build(bridge, ground);

        // Nothing sticks out past the two ends along the bridge (the abutments are under the roadway, not behind it).
        Assert.All(mesh.Triangles, t =>
        {
            Assert.InRange(Math.Min(t.A.X, Math.Min(t.B.X, t.C.X)), 10 - 0.05f, 50 + 0.05f);
            Assert.InRange(Math.Max(t.A.X, Math.Max(t.B.X, t.C.X)), 10 - 0.05f, 50 + 0.05f);
        });
        // Every up-facing surface within a metre of an end is the roadway (or a parapet top), never an abutment top at ground level.
        double endHeight = ArchBridgeMesher.RoadwayHeight(bridge, ground, 0);
        var stone = mesh.Triangles.Where(t => t.Colour == SettlementPalette.Hex(0x8c887e)
            && Vector3.Normalize(Vector3.Cross(t.B - t.A, t.C - t.A)).Y > 0.9f
            && t.A.X < 12 && t.A.Y < endHeight + 0.5f).ToList();
        Assert.All(stone, t => Assert.True(t.A.Y < endHeight - 0.25f || t.A.Y > endHeight + 0.9f, $"a stone top at {t.A.Y:F2} m lies at roadway level ({endHeight:F2} m)"));
    }

    // ---- a river through the city ----------------------------------------------------------------------------

    [Theory]
    [InlineData(SettlementLayout.Radial)]
    [InlineData(SettlementLayout.Cross)]
    public void ARiverThroughACity_LeavesHousesOnBothBanks_AwayFromTheWater(SettlementLayout layout)
    {
        var river = new Strip(45, 18);
        City dry = Generate(null, layout);
        City wet = Generate(river, layout);

        var houses = wet.CityPatches.SelectMany(p => p.Buildings).ToList();
        int west = houses.Count(b => b.Footprint.Centroid.X < river.Offset);
        int east = houses.Count(b => b.Footprint.Centroid.X > river.Offset);
        Assert.True(west >= houses.Count * 0.2 && east >= houses.Count * 0.2, $"the city is not on both banks ({west} / {east})");
        Assert.True(wet.AllBuildings.Count() < dry.AllBuildings.Count());
        // The least distance from any house corner to the bank is the buffer.
        double nearest = wet.AllBuildings.SelectMany(b => b.Footprint.Points).Min(v => Math.Abs(v.X - river.Offset) - river.HalfWidth);
        Assert.True(nearest >= 9, $"a house is only {nearest:F1} m from the water");
        // The wall and the gates are the same with or without the river: it runs through untouched.
        Assert.Equal(dry.Wall, wet.Wall);
        Assert.Equal(dry.Gates, wet.Gates);
    }

    [Theory]
    [InlineData(SettlementLayout.Radial)]
    [InlineData(SettlementLayout.Cross)]
    public void TheBridges_SpanTheRiver_AndNoStreetCrossesItWithoutOne(SettlementLayout layout)
    {
        var river = new Strip(45, 18);
        City city = Generate(river, layout);

        Assert.InRange(city.Bridges.Count, 1, 2);
        Assert.All(city.Bridges, b =>
        {
            Assert.False(river.IsWater(b.From.X, b.From.Y));
            Assert.False(river.IsWater(b.To.X, b.To.Y));
            Assert.True(Math.Min(b.From.X, b.To.X) < river.Offset - river.HalfWidth && Math.Max(b.From.X, b.To.X) > river.Offset + river.HalfWidth);
        });
        Assert.Empty(Generate(null, layout).Bridges);
    }

    [Fact]
    public void TheRiverCityIsPlannedBiggerSoItsHousesComeToAboutTheCap()
    {
        var settings = new SettlementSettings();
        Assert.True(settings.RiverCityPlanFactor > 1);
        Assert.Equal(1, settings.RiverCities);
        var river = new Strip(45, 18);
        int cap = settings.CityBuildings;
        int planned = (int)Math.Round(cap * settings.RiverCityPlanFactor);

        int houses = Generate(river, SettlementLayout.Radial, planned).AllBuildings.Count(b => b.Ward != WardType.Outskirts);

        Assert.InRange(houses, cap * 0.7, cap * 1.05);
        Assert.False(new SettlementSettings { RiverCityPlanFactor = 0.5 }.TryValidate(out _));
    }

    [Fact]
    public void TheValidatorFlagsARiverCityWithoutARiver()
    {
        var flat = new float[(2048 / 16 + 1) * (2048 / 16 + 1)];
        Array.Fill(flat, 100f);
        var field = new HeightField(2048, 16, flat);
        var settlement = new Settlement("settlement.river_1", "Test", SettlementTier.City, 0, 0, 0, 100, 150, 180, 1, 330, true, CentrepieceKind.Fountain);
        var report = new ValidationReport();

        SettlementValidator.Validate(new[] { settlement }, field, null, null, report);

        Assert.True(settlement.IsRiverCity);
        Assert.Contains(report.Issues, i => i.Code == SettlementCodes.RiverCityWithoutRiver);
    }

    private sealed class MultiStrip : IWaterSampler
    {
        private readonly double[] _centres;
        private readonly double _half;

        public MultiStrip(double[] centres, double half)
        {
            _centres = centres;
            _half = half;
        }

        public bool IsWater(double x, double y) => _centres.Any(c => Math.Abs(x - c) <= _half);
    }

    private sealed class Slope : IGroundSampler
    {
        private readonly double _dx;
        public Slope(double dx) => _dx = dx;
        public double HeightAt(double x, double y) => x * _dx;
    }
}
