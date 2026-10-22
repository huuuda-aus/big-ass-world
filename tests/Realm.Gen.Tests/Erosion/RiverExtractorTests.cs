using System;
using System.Linq;
using Realm.Core.Validation;
using Realm.Gen.Terrain;
using Realm.Gen.Terrain.Erosion;
using Xunit;

namespace Realm.Gen.Tests.Erosion;

public class RiverExtractorTests
{
    private const double Spacing = 1000; // 1 km: one cell is 1 km2, so rainfall 1 is exactly 1 km2 of flow

    private static float[] Grid(int side, Func<int, int, float> height)
    {
        var h = new float[side * side];
        for (int z = 0; z < side; z++)
        {
            for (int x = 0; x < side; x++)
            {
                h[(z * side) + x] = height(x, z);
            }
        }

        return h;
    }

    private static double[] Uniform(int count, double value) => Enumerable.Repeat(value, count).ToArray();

    private static (FlowRouting Routing, double[] Flow) Route(float[] h, int side, double[] rain, double[]? loss = null, float seaLevel = -1000)
    {
        FlowRouting r = FlowRouter.Route(h, side, Spacing, seaLevel, 0.5f);
        return (r, FlowAccumulator.Accumulate(r, rain, loss ?? Uniform(side * side, 0)));
    }

    [Fact]
    public void VShapedValley_GivesOneRiverDownTheMiddle()
    {
        const int Side = 11;
        // Slopes south toward the bottom edge and inward toward column 5: water converges on the centre column.
        float[] h = Grid(Side, (x, z) => ((Side - 1 - z) * 10f) + (Math.Abs(x - 5) * 20f));
        (FlowRouting r, double[] flow) = Route(h, Side, Uniform(Side * Side, 1));

        // Flow in the centre column grows with every row; no other column comes close.
        double centreAtRow5 = flow[(5 * Side) + 5];
        double nextBest = Enumerable.Range(0, Side * Side).Where(i => i % Side != 5 && !r.IsOutlet[i]).Max(i => flow[i]);
        Assert.True(centreAtRow5 > nextBest, $"centre {centreAtRow5} vs {nextBest}");

        var rivers = RiverExtractor.ExtractRivers(r, h, flow, thresholdKm2: centreAtRow5, seaLevel: -1000);

        River river = Assert.Single(rivers);
        Assert.Equal(RiverMouth.WorldEdge, river.Mouth);
        Assert.All(river.Points, p => Assert.Equal(0.0, p.X, 9)); // straight down the middle (x = 0 m from the centre)
        Assert.True(river.Points[0].Z < river.Points[^1].Z, "river flows from north to south");
        Assert.Equal(5000.0, river.Points[^1].Z, 9); // reaches the south edge
    }

    [Fact]
    public void River_HeightsNeverRiseDownstream_AndFlowNeverDrops()
    {
        const int Side = 11;
        float[] h = Grid(Side, (x, z) => ((Side - 1 - z) * 10f) + (Math.Abs(x - 5) * 20f));
        (FlowRouting r, double[] flow) = Route(h, Side, Uniform(Side * Side, 1));

        River river = RiverExtractor.ExtractRivers(r, h, flow, 6, -1000).First();

        for (int i = 1; i < river.Points.Count; i++)
        {
            Assert.True(river.Points[i].Height <= river.Points[i - 1].Height + 1e-9);
            Assert.True(river.Points[i].FlowKm2 >= river.Points[i - 1].FlowKm2 - 1e-9);
        }

        Assert.Equal(river.Points.Max(p => p.FlowKm2), river.MaxFlowKm2);
    }

    [Fact]
    public void TwoTributaries_JoinIntoOneRiver_WithAConfluence()
    {
        const int Side = 11;
        // Everything drains west and toward row 5: off-centre cells slide diagonally to the central row.
        float[] h = Grid(Side, (x, z) => (x * 10f) + (Math.Abs(z - 5) * 15f));
        double[] rain = Uniform(Side * Side, 0);
        rain[(1 * Side) + 8] = 1;
        rain[(9 * Side) + 8] = 1;
        (FlowRouting r, double[] flow) = Route(h, Side, rain);

        var rivers = RiverExtractor.ExtractRivers(r, h, flow, 0.5, -1000);

        Assert.Equal(2, rivers.Count);
        Assert.Equal(RiverMouth.WorldEdge, rivers[0].Mouth);
        Assert.Null(rivers[0].JoinsRiver);
        Assert.Equal(RiverMouth.Confluence, rivers[1].Mouth);
        Assert.Equal(0, rivers[1].JoinsRiver);
        Assert.Contains(rivers[0].Points, p => p.X == rivers[1].Points[^1].X && p.Z == rivers[1].Points[^1].Z);
        // After the junction the main river carries both tributaries; the tributary itself carried 1 km2 until the
        // junction vertex, which belongs to the main river and holds the combined 2 km2.
        Assert.Equal(2.0, rivers[0].MaxFlowKm2, 9);
        Assert.Equal(1.0, rivers[1].Points[^2].FlowKm2, 9);
        Assert.Equal(2.0, rivers[1].Points[^1].FlowKm2, 9);
    }

    [Fact]
    public void WithRiversReachWater_ARiverThatLosesItsWaterGetsThinButKeepsRunningToTheEdge()
    {
        const int Side = 11;
        float[] h = Grid(Side, (x, _) => x * 10f); // drains west
        double[] rain = Uniform(Side * Side, 0);
        rain[(5 * Side) + 9] = 4;
        double[] loss = Uniform(Side * Side, 0);
        for (int x = 0; x < 6; x++)
        {
            loss[(5 * Side) + x] = 0.9;
        }

        (FlowRouting r, double[] flow) = Route(h, Side, rain, loss);

        River dries = Assert.Single(RiverExtractor.ExtractRivers(r, h, flow, 1.0, -1000));
        River reaches = Assert.Single(RiverExtractor.ExtractRivers(r, h, flow, 1.0, -1000, riversReachWater: true));
        Assert.Equal(RiverMouth.Dried, dries.Mouth);
        Assert.Equal(RiverMouth.WorldEdge, reaches.Mouth);
        Assert.Equal(-5000.0, reaches.Points[^1].X, 9); // all the way to the west edge
        Assert.True(reaches.Points.Count > dries.Points.Count);
        Assert.True(reaches.Points[^1].FlowKm2 < 1.0, "the water that is left is below the river threshold: a trickle, not a river");
    }

    [Fact]
    public void RiverLoss_CanDryARiverUp()
    {
        const int Side = 11;
        float[] h = Grid(Side, (x, _) => x * 10f); // drains west
        double[] rain = Uniform(Side * Side, 0);
        rain[(5 * Side) + 9] = 4;
        double[] loss = Uniform(Side * Side, 0);
        for (int x = 0; x < 6; x++)
        {
            loss[(5 * Side) + x] = 0.9; // a dry region in the west loses 90% per km, i.e. per cell
        }

        (FlowRouting r, double[] flow) = Route(h, Side, rain, loss);

        River river = Assert.Single(RiverExtractor.ExtractRivers(r, h, flow, 1.0, -1000));
        Assert.Equal(RiverMouth.Dried, river.Mouth);
        Assert.True(river.Points[^1].X > -5000 + 0.5 * Spacing, "the river should stop before the west edge");
        Assert.True(river.Points[^1].FlowKm2 >= 1.0);
    }

    [Fact]
    public void RiverReachingTheSea_EndsAtTheShoreline()
    {
        const int Side = 11;
        // Land rises eastward from a sea (height <= 0) in the west two columns.
        float[] h = Grid(Side, (x, _) => (x - 2) * 10f);
        double[] rain = Uniform(Side * Side, 0);
        rain[(5 * Side) + 9] = 3;
        (FlowRouting r, double[] flow) = Route(h, Side, rain, seaLevel: 0);

        River river = Assert.Single(RiverExtractor.ExtractRivers(r, h, flow, 1.0, 0));

        Assert.Equal(RiverMouth.Sea, river.Mouth);
        Assert.True(river.Points[^1].Height <= 0);
        Assert.True(river.Points[^2].Height > 0);
    }

    [Fact]
    public void NoRain_MeansNoRivers()
    {
        const int Side = 9;
        float[] h = Grid(Side, (x, _) => x * 10f);
        (FlowRouting r, double[] flow) = Route(h, Side, Uniform(Side * Side, 0));

        Assert.Empty(RiverExtractor.ExtractRivers(r, h, flow, 0.1, -1000));
    }

    [Fact]
    public void LongerRiversComeFirst_AndLengthIsMeasuredAlongThePolyline()
    {
        const int Side = 11;
        float[] h = Grid(Side, (x, z) => (x * 10f) + (Math.Abs(z - 5) * 15f));
        double[] rain = Uniform(Side * Side, 0);
        rain[(5 * Side) + 9] = 1; // a source on the central row: 9 cells from the outlet
        rain[(1 * Side) + 3] = 1; // a short one elsewhere, joining later
        (FlowRouting r, double[] flow) = Route(h, Side, rain);

        var rivers = RiverExtractor.ExtractRivers(r, h, flow, 0.5, -1000);

        Assert.True(rivers[0].LengthMeters >= rivers[^1].LengthMeters);
        double measured = 0;
        for (int i = 1; i < rivers[0].Points.Count; i++)
        {
            double dx = rivers[0].Points[i].X - rivers[0].Points[i - 1].X;
            double dz = rivers[0].Points[i].Z - rivers[0].Points[i - 1].Z;
            measured += Math.Sqrt((dx * dx) + (dz * dz));
        }

        Assert.Equal(measured, rivers[0].LengthMeters, 6);
    }

    [Fact]
    public void Extraction_IsDeterministic()
    {
        const int Side = 11;
        float[] h = Grid(Side, (x, z) => ((Side - 1 - z) * 10f) + (Math.Abs(x - 5) * 20f) + (((x * 7) + (z * 3)) % 5));
        (FlowRouting r, double[] flow) = Route(h, Side, Uniform(Side * Side, 1));

        var a = RiverExtractor.ExtractRivers(r, h, flow, 3, -1000);
        var b = RiverExtractor.ExtractRivers(r, h, flow, 3, -1000);

        Assert.Equal(a.Count, b.Count);
        for (int i = 0; i < a.Count; i++)
        {
            Assert.Equal(a[i].Points, b[i].Points);
            Assert.Equal(a[i].Mouth, b[i].Mouth);
        }
    }

    [Fact]
    public void Pit_BecomesALake_WithSurfaceAreaAndCentre()
    {
        const int Side = 9;
        float[] h = Grid(Side, (x, z) =>
        {
            int d = Math.Max(Math.Abs(x - 4), Math.Abs(z - 4));
            if (d == 4)
            {
                return 0;
            }

            return d == 3 ? ((x == 4 && z == 1) ? 6f : 10f) : 2f;
        });
        FlowRouting r = FlowRouter.Route(h, Side, Spacing, -1000, 0.5f);

        Lake lake = Assert.Single(RiverExtractor.ExtractLakes(r, 1.0));

        Assert.Equal(6.0, lake.SurfaceHeight);
        Assert.Equal(25, lake.Cells.Count); // the 5 x 5 basin floor
        Assert.Equal(25.0, lake.AreaSquareKilometres, 9);
        Assert.Equal(0.0, lake.CentreX, 9);
        Assert.Equal(0.0, lake.CentreZ, 9);
        Assert.Empty(RiverExtractor.ExtractLakes(r, 26.0)); // 25 km2 is too small to keep
    }

    [Fact]
    public void LakesAtDifferentLevels_AreSeparate()
    {
        const int Side = 15;
        // Two basins side by side with different spill heights, divided by a wall.
        float[] h = Grid(Side, (x, z) =>
        {
            bool edge = x == 0 || z == 0 || x == Side - 1 || z == Side - 1;
            if (edge)
            {
                return 0;
            }

            if (x == 7)
            {
                return 30; // divider
            }

            if (z == 1 && x == 3)
            {
                return 8; // low notch on the west basin's north rim
            }

            if (z == 1 && x == 11)
            {
                return 15; // higher notch on the east basin's north rim
            }

            return z == 1 || z == Side - 2 || x == 1 || x == Side - 2 ? 40f : 2f;
        });
        FlowRouting r = FlowRouter.Route(h, Side, Spacing, -1000, 0.5f);

        var lakes = RiverExtractor.ExtractLakes(r, 1.0);

        Assert.Equal(2, lakes.Count);
        Assert.Equal(new[] { 8.0, 15.0 }, lakes.Select(l => l.SurfaceHeight).OrderBy(v => v).ToArray());
    }

    [Fact]
    public void ValidNetwork_PassesTheValidator()
    {
        const int Side = 11;
        float[] h = Grid(Side, (x, z) => ((Side - 1 - z) * 10f) + (Math.Abs(x - 5) * 20f));
        (FlowRouting r, double[] flow) = Route(h, Side, Uniform(Side * Side, 1));
        var network = new RiverNetwork(RiverExtractor.ExtractRivers(r, h, flow, 6, -1000), new System.Collections.Generic.List<Lake>());
        var report = new ValidationReport();

        RiverValidator.Validate(network, 10000, report);

        Assert.Empty(report.Issues);
    }

    [Fact]
    public void Validator_CatchesUphillRivers_BadMouths_AndBadConfluences()
    {
        River Make(int index, RiverMouth mouth, int? joins, params RiverPoint[] points) => new(index, points, mouth, joins, 100);

        var uphill = Make(0, RiverMouth.Dried, null, new RiverPoint(0, 0, 10, 1), new RiverPoint(1, 0, 12, 1));
        var seaTooHigh = Make(1, RiverMouth.Sea, null, new RiverPoint(0, 0, 10, 1), new RiverPoint(1, 0, 8, 1));
        var notAtEdge = Make(2, RiverMouth.WorldEdge, null, new RiverPoint(0, 0, 10, 1), new RiverPoint(1, 0, 8, 1));
        var badJoin = Make(3, RiverMouth.Confluence, 99, new RiverPoint(0, 0, 10, 1), new RiverPoint(1, 0, 8, 1));
        var missesTarget = Make(4, RiverMouth.Confluence, 0, new RiverPoint(0, 0, 10, 1), new RiverPoint(7, 7, 8, 1));
        var network = new RiverNetwork(new[] { uphill, seaTooHigh, notAtEdge, badJoin, missesTarget }, new System.Collections.Generic.List<Lake>());
        var report = new ValidationReport();

        RiverValidator.Validate(network, 10000, report);

        Assert.Contains(report.Issues, i => i.Code == TerrainCodes.RiverUphill && i.Location == "river 0");
        Assert.Equal(4, report.Issues.Count(i => i.Code == TerrainCodes.RiverDeadEnd));
        Assert.Contains(report.Issues, i => i.Location == "river 1" && i.Message.Contains("above sea level"));
        Assert.Contains(report.Issues, i => i.Location == "river 2" && i.Message.Contains("world edge"));
        Assert.Contains(report.Issues, i => i.Location == "river 3" && i.Message.Contains("no valid river"));
        Assert.Contains(report.Issues, i => i.Location == "river 4" && i.Message.Contains("does not end on a point"));
    }

    [Fact]
    public void DriedRivers_AreNotAnError()
    {
        var dried = new River(0, new[] { new RiverPoint(0, 0, 10, 2), new RiverPoint(1, 0, 8, 1) }, RiverMouth.Dried, null, 100);
        var report = new ValidationReport();

        RiverValidator.Validate(new RiverNetwork(new[] { dried }, new System.Collections.Generic.List<Lake>()), 10000, report);

        Assert.Empty(report.Issues);
    }

    private static float[] PitTerrain(int side, float rim, float floor) => Grid(side, (x, z) =>
    {
        int d = Math.Max(Math.Abs(x - (side / 2)), Math.Abs(z - (side / 2)));
        if (d == side / 2)
        {
            return 0;
        }

        return d >= (side / 2) - 1 ? rim : floor;
    });

    [Fact]
    public void FillSmallBasins_RaisesSmallPitsToTheirWaterLevel()
    {
        const int Side = 9;
        float[] h = PitTerrain(Side, 10, 2);
        FlowRouting r = FlowRouter.Route(h, Side, Spacing, -1000, 0.5f);

        float[] filled = RiverExtractor.FillSmallBasins(r, h, minAreaKm2: 100); // the 9 km2 pit is "small"

        Assert.Equal(10f, filled[(4 * Side) + 4]);
        Assert.All(Enumerable.Range(0, Side * Side), i => Assert.True(filled[i] >= h[i]));
        Assert.Equal(h[0], filled[0]); // non-basin cells keep their height
    }

    [Fact]
    public void FillSmallBasins_LeavesLargeLakesAlone()
    {
        const int Side = 9;
        float[] h = PitTerrain(Side, 10, 2);
        FlowRouting r = FlowRouter.Route(h, Side, Spacing, -1000, 0.5f);

        float[] kept = RiverExtractor.FillSmallBasins(r, h, minAreaKm2: 1.0);

        Assert.Equal(h, kept);
    }

    [Fact]
    public void FillSmallBasins_AlsoFillsShallowDents_AndIsIdempotent()
    {
        const int Side = 9;
        float[] h = PitTerrain(Side, 10, 9.8f); // 0.2 m deep: not a lake under a 0.5 m minimum depth
        FlowRouting r = FlowRouter.Route(h, Side, Spacing, -1000, 0.5f);

        float[] once = RiverExtractor.FillSmallBasins(r, h, minAreaKm2: 1.0);
        FlowRouting again = FlowRouter.Route(once, Side, Spacing, -1000, 0.5f);
        float[] twice = RiverExtractor.FillSmallBasins(again, once, minAreaKm2: 1.0);

        Assert.Equal(10f, once[(4 * Side) + 4]);
        Assert.Equal(once, twice);
    }
}
