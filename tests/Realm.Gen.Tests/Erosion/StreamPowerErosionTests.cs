using System;
using System.Linq;
using Realm.Gen.Terrain.Erosion;
using Xunit;

namespace Realm.Gen.Tests.Erosion;

public class StreamPowerErosionTests
{
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
    public void SingleLink_MatchesTheClosedFormByHand()
    {
        // 3 x 3 grid, spacing 10 m. Centre vertex 4 (height 100) drains west into vertex 3 (height 0, an outlet).
        // flow 4 km2, erodibility 0.5, strength 1: factor = 0.5 * sqrt(4) / 10 = 0.1, so new = (100 + 0.1 * 0) / 1.1.
        var filled = new float[9];
        filled[4] = 100;
        var receiver = new int[9];
        Array.Fill(receiver, -1);
        receiver[4] = 3;
        var outlet = new bool[9];
        Array.Fill(outlet, true);
        outlet[4] = false;
        var routing = new FlowRouting(3, 10, filled, receiver, new[] { 0, 1, 2, 3, 5, 6, 7, 8, 4 }, outlet, new bool[9]);
        double[] flow = Uniform(9, 0);
        flow[4] = 4;

        float[] result = StreamPowerEroder.Step(routing, filled, flow, Uniform(9, 0.5), 1);

        Assert.Equal(100 / 1.1, result[4], 4);
        Assert.Equal(0, result[3]);
    }

    [Fact]
    public void NeverRaisesHeights_AndLeavesOutletsAlone()
    {
        const int Side = 13;
        float[] h = Grid(Side, (x, z) => 30 + (((x * 7) + (z * 13)) % 11) + (x * 3f));
        FlowRouting r = FlowRouter.Route(h, Side, 20, 0, 0.5f);
        double[] flow = FlowAccumulator.Accumulate(r, Uniform(Side * Side, 1), Uniform(Side * Side, 0));

        float[] result = StreamPowerEroder.Step(r, h, flow, Uniform(Side * Side, 1), 2);

        for (int i = 0; i < h.Length; i++)
        {
            Assert.True(result[i] <= h[i] + 1e-5f, $"vertex {i} rose");
            if (r.IsOutlet[i])
            {
                Assert.Equal(h[i], result[i]);
            }
        }
    }

    [Fact]
    public void NoErodibility_NoChange_AndNoFlow_NoChange()
    {
        const int Side = 9;
        float[] h = Grid(Side, (x, _) => 10 + (x * 5f));
        FlowRouting r = FlowRouter.Route(h, Side, 20, 0, 0.5f);
        double[] flow = FlowAccumulator.Accumulate(r, Uniform(Side * Side, 1), Uniform(Side * Side, 0));

        Assert.Equal(h, StreamPowerEroder.Step(r, h, flow, Uniform(Side * Side, 0), 5));
        Assert.Equal(h, StreamPowerEroder.Step(r, h, Uniform(Side * Side, 0), Uniform(Side * Side, 1), 5));
    }

    [Fact]
    public void NeverCutsBelowTheReceiver_NoMatterHowStrong()
    {
        const int Side = 13;
        float[] h = Grid(Side, (x, z) => 30 + (((x * 7) + (z * 13)) % 11) + (x * 3f));
        FlowRouting r = FlowRouter.Route(h, Side, 20, 0, 0.5f);
        double[] flow = FlowAccumulator.Accumulate(r, Uniform(Side * Side, 1), Uniform(Side * Side, 0));

        float[] result = StreamPowerEroder.Step(r, h, flow, Uniform(Side * Side, 1), 1e6);

        for (int i = 0; i < h.Length; i++)
        {
            int to = r.Receiver[i];
            if (to >= 0 && r.Filled[i] <= h[i])
            {
                float receiverLevel = r.Filled[to] > h[to] ? r.Filled[to] : result[to];
                Assert.True(result[i] >= receiverLevel - 1e-3f, $"vertex {i} cut below its receiver");
            }
        }
    }

    [Fact]
    public void MoreFlow_CarvesDeeper_OnTheSameSlope()
    {
        // Two parallel channels with the same slope; the second collects ten times the rain.
        const int Side = 9;
        float[] h = Grid(Side, (x, _) => x * 10f);
        FlowRouting r = FlowRouter.Route(h, Side, 10, -1000, 0.5f);
        double[] rain = Uniform(Side * Side, 0);
        for (int x = 1; x < Side - 1; x++)
        {
            rain[(2 * Side) + x] = 1;
            rain[(6 * Side) + x] = 10;
        }

        double[] flow = FlowAccumulator.Accumulate(r, rain, Uniform(Side * Side, 0));
        float[] result = StreamPowerEroder.Step(r, h, flow, Uniform(Side * Side, 1), 1);

        double dry = h[(2 * Side) + 6] - result[(2 * Side) + 6];
        double wet = h[(6 * Side) + 6] - result[(6 * Side) + 6];
        Assert.True(dry > 0);
        Assert.True(wet > dry * 2, $"wet channel lowered {wet}, dry {dry}");
    }

    [Fact]
    public void Lakes_AreNotCarved()
    {
        const int Side = 9;
        float[] h = Grid(Side, (x, z) =>
        {
            int d = Math.Max(Math.Abs(x - 4), Math.Abs(z - 4));
            return d == 4 ? 0 : d == 3 ? 10 : 2;
        });
        FlowRouting r = FlowRouter.Route(h, Side, 10, -1000, 0.5f);
        double[] flow = FlowAccumulator.Accumulate(r, Uniform(Side * Side, 1), Uniform(Side * Side, 0));

        float[] result = StreamPowerEroder.Step(r, h, flow, Uniform(Side * Side, 1), 10);

        for (int i = 0; i < h.Length; i++)
        {
            if (r.IsLake[i])
            {
                Assert.Equal(h[i], result[i]);
            }
        }
    }

    [Fact]
    public void Repeated_Steps_ConvergeTowardAGradedProfile()
    {
        const int Side = 9;
        float[] h = Grid(Side, (x, z) => 100 + (((x * 5) + (z * 3)) % 7) + (x * 4f));
        FlowRouting r = FlowRouter.Route(h, Side, 10, 0, 0.5f);
        double[] flow = FlowAccumulator.Accumulate(r, Uniform(Side * Side, 1), Uniform(Side * Side, 0));

        float[] current = h;
        double previousTotal = h.Sum(v => (double)v);
        for (int i = 0; i < 20; i++)
        {
            current = StreamPowerEroder.Step(r, current, flow, Uniform(Side * Side, 1), 1);
            double total = current.Sum(v => (double)v);
            Assert.True(total <= previousTotal + 1e-3, "terrain should only lose material");
            previousTotal = total;
        }

        Assert.True(previousTotal < h.Sum(v => (double)v));
    }
}
