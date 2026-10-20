using System;
using System.Linq;
using Realm.Gen.Terrain.Erosion;
using Xunit;

namespace Realm.Gen.Tests.Erosion;

public class FlowAccumulatorTests
{
    private const double Spacing = 100; // 0.1 km, so one cell is 0.01 km2

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

    [Fact]
    public void TiltedPlane_AccumulatesAlongEachRow()
    {
        const int Side = 7;
        FlowRouting r = FlowRouter.Route(Grid(Side, (x, _) => x * 5f), Side, Spacing, -1000, 0.5f);

        double[] flow = FlowAccumulator.Accumulate(r, Uniform(Side * Side, 1), Uniform(Side * Side, 0));

        // Row z = 3 drains west. Columns 1..5 are interior; the east edge (column 6) is an outlet that sends nothing
        // back, so vertex x collects the rain of columns x..5: (Side - 1 - x) cells. The west edge (an outlet) adds its own.
        const double Cell = 0.01;
        for (int x = 1; x < Side - 1; x++)
        {
            Assert.Equal((Side - 1 - x) * Cell, flow[(3 * Side) + x], 9);
        }

        Assert.Equal((Side - 1) * Cell, flow[3 * Side], 9); // five upstream cells plus its own rain
    }

    [Fact]
    public void NoRain_MeansNoFlow()
    {
        const int Side = 7;
        FlowRouting r = FlowRouter.Route(Grid(Side, (x, _) => x * 5f), Side, Spacing, -1000, 0.5f);

        Assert.All(FlowAccumulator.Accumulate(r, Uniform(Side * Side, 0), Uniform(Side * Side, 0)), q => Assert.Equal(0, q));
    }

    [Fact]
    public void TotalFlowAtOutlets_EqualsTotalRain_WhenNothingIsLost()
    {
        const int Side = 11;
        float[] h = Grid(Side, (x, z) => ((x * 7) + (z * 13)) % 11);
        FlowRouting r = FlowRouter.Route(h, Side, Spacing, -1000, 0.5f);
        double[] rain = Enumerable.Range(0, Side * Side).Select(i => (i % 5) / 4.0).ToArray();

        double[] flow = FlowAccumulator.Accumulate(r, rain, Uniform(Side * Side, 0));

        double arrived = Enumerable.Range(0, Side * Side).Where(i => r.IsOutlet[i]).Sum(i => flow[i]);
        Assert.Equal(rain.Sum() * 0.01, arrived, 9);
    }

    [Fact]
    public void RiverLoss_ShrinksFlowByTheStatedFractionPerKilometre()
    {
        // 1 km spacing so loss 0.5 per km halves the flow at every cardinal step.
        const int Side = 5;
        float[] h = Grid(Side, (x, _) => x * 5f);
        FlowRouting r = FlowRouter.Route(h, Side, 1000, -1000, 0.5f);
        double[] rain = Uniform(Side * Side, 0);
        rain[(2 * Side) + 3] = 1; // one wet cell, 1 km2, three cells east of the outlet column 0... drains west

        double[] flow = FlowAccumulator.Accumulate(r, rain, Uniform(Side * Side, 0.5));

        Assert.Equal(1.0, flow[(2 * Side) + 3], 9);
        Assert.Equal(0.5, flow[(2 * Side) + 2], 9);
        Assert.Equal(0.25, flow[(2 * Side) + 1], 9);
        Assert.Equal(0.125, flow[2 * Side], 9);
    }

    [Fact]
    public void TotalLoss_StopsTheRiver()
    {
        const int Side = 5;
        float[] h = Grid(Side, (x, _) => x * 5f);
        FlowRouting r = FlowRouter.Route(h, Side, 1000, -1000, 0.5f);
        double[] rain = Uniform(Side * Side, 0);
        rain[(2 * Side) + 3] = 1;

        double[] flow = FlowAccumulator.Accumulate(r, rain, Uniform(Side * Side, 1));

        Assert.Equal(1.0, flow[(2 * Side) + 3], 9);
        Assert.Equal(0.0, flow[(2 * Side) + 2], 9);
        Assert.Equal(0.0, flow[2 * Side], 9);
    }

    [Fact]
    public void LossOnlyAppliesWhereTheWaterTravels()
    {
        // Loss 0.5 in the destination cell does not affect water leaving a lossless cell: loss is charged by the
        // cell the water drains FROM.
        const int Side = 5;
        float[] h = Grid(Side, (x, _) => x * 5f);
        FlowRouting r = FlowRouter.Route(h, Side, 1000, -1000, 0.5f);
        double[] rain = Uniform(Side * Side, 0);
        rain[(2 * Side) + 3] = 1;
        double[] loss = Uniform(Side * Side, 0);
        loss[(2 * Side) + 2] = 0.5;

        double[] flow = FlowAccumulator.Accumulate(r, rain, loss);

        Assert.Equal(1.0, flow[(2 * Side) + 2], 9);
        Assert.Equal(0.5, flow[(2 * Side) + 1], 9);
        Assert.Equal(0.5, flow[2 * Side], 9);
    }
}
