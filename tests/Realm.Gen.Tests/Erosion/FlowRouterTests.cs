using System;
using System.Linq;
using Realm.Gen.Terrain.Erosion;
using Xunit;

namespace Realm.Gen.Tests.Erosion;

public class FlowRouterTests
{
    private const double Spacing = 10;

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

    private static FlowRouting Route(float[] h, int side, float seaLevel = -1000, float lakeMin = 0.5f) =>
        FlowRouter.Route(h, side, Spacing, seaLevel, lakeMin);

    private static void AssertWellFormed(FlowRouting r)
    {
        int count = r.Side * r.Side;
        Assert.Equal(count, r.Order.Length);
        Assert.Equal(count, r.Order.Distinct().Count());

        var rank = new int[count];
        for (int i = 0; i < count; i++)
        {
            rank[r.Order[i]] = i;
        }

        for (int i = 0; i < count; i++)
        {
            if (r.IsOutlet[i])
            {
                Assert.Equal(-1, r.Receiver[i]);
                continue;
            }

            int to = r.Receiver[i];
            Assert.InRange(to, 0, count - 1);
            Assert.True(rank[to] < rank[i], $"receiver of {i} is not earlier in the order");
            Assert.True(r.Filled[to] <= r.Filled[i], $"water at {i} would flow uphill");
        }
    }

    [Fact]
    public void TiltedPlane_DrainsStraightDownhill()
    {
        const int Side = 9;
        float[] h = Grid(Side, (x, _) => x * 5f); // rises to the east; lowest at the west edge
        FlowRouting r = Route(h, Side);

        AssertWellFormed(r);
        for (int z = 1; z < Side - 1; z++)
        {
            for (int x = 1; x < Side - 1; x++)
            {
                Assert.Equal((z * Side) + x - 1, r.Receiver[(z * Side) + x]); // due west beats the diagonals
            }
        }
    }

    [Fact]
    public void FilledHeights_NeverDropBelowTheTerrain_AndNonLakesAreUntouched()
    {
        const int Side = 11;
        float[] h = Grid(Side, (x, z) => ((x * 7) + (z * 13)) % 11);
        FlowRouting r = Route(h, Side);

        AssertWellFormed(r);
        for (int i = 0; i < h.Length; i++)
        {
            Assert.True(r.Filled[i] >= h[i]);
        }
    }

    [Fact]
    public void Pit_IsFilledToItsLowestSpillPoint_AndBecomesALake()
    {
        const int Side = 9;
        // Boundary 0 (outlets), a rim of height 10 around a basin floor at height 2, one notch in the rim at height 6.
        float[] h = Grid(Side, (x, z) =>
        {
            int d = Math.Max(Math.Abs(x - 4), Math.Abs(z - 4));
            if (d == 4)
            {
                return 0;
            }

            if (d == 3)
            {
                return (x == 4 && z == 1) ? 6 : 10; // notch on the north rim
            }

            return 2; // the whole basin floor (d <= 2) is one pit at height 2
        });
        FlowRouting r = Route(h, Side);

        AssertWellFormed(r);
        int pit = (4 * Side) + 4;
        Assert.Equal(6f, r.Filled[pit]);
        Assert.True(r.IsLake[pit]);

        // The pit's water must reach the boundary through the notch at (4, 1) and the outlet above it at (4, 0).
        int at = pit;
        int steps = 0;
        bool passedNotch = false;
        while (!r.IsOutlet[at] && steps++ < 100)
        {
            passedNotch |= at == (1 * Side) + 4;
            at = r.Receiver[at];
        }

        Assert.True(r.IsOutlet[at]);
        Assert.True(passedNotch, "the pit should drain through the notch");
    }

    [Fact]
    public void ShallowPits_AreFilledButNotReportedAsLakes()
    {
        const int Side = 7;
        float[] h = Grid(Side, (x, z) => (x == 3 && z == 3) ? 9.8f : 10f);
        // Make the centre a 0.2 m dent in a plateau whose edge is the outlet ring at 0.
        for (int i = 0; i < Side; i++)
        {
            h[i] = h[(Side - 1) * Side + i] = h[i * Side] = h[(i * Side) + Side - 1] = 0;
        }

        FlowRouting r = Route(h, Side, lakeMin: 0.5f);

        Assert.Equal(10f, r.Filled[(3 * Side) + 3]);
        Assert.False(r.IsLake[(3 * Side) + 3]);
        Assert.True(Route(h, Side, lakeMin: 0.1f).IsLake[(3 * Side) + 3]);
    }

    [Fact]
    public void FlatPlane_IsRoutedDeterministically_WithNoCycles()
    {
        const int Side = 9;
        float[] h = Grid(Side, (_, _) => 5f);

        FlowRouting a = Route(h, Side);
        FlowRouting b = Route(h, Side);

        AssertWellFormed(a);
        Assert.Equal(a.Receiver, b.Receiver);
        Assert.Equal(a.Order, b.Order);
        Assert.DoesNotContain(true, a.IsLake);
    }

    [Fact]
    public void WorldEdge_AndSeaCells_AreOutlets()
    {
        const int Side = 9;
        // Land at 5 with a sea cell (height -3) inside it.
        float[] h = Grid(Side, (x, z) => (x == 4 && z == 4) ? -3f : 5f);
        FlowRouting r = Route(h, Side, seaLevel: 0);

        Assert.True(r.IsOutlet[0]);
        Assert.True(r.IsOutlet[(Side * Side) - 1]);
        Assert.True(r.IsOutlet[(4 * Side) + 4]);
        Assert.False(r.IsOutlet[(2 * Side) + 2]);
        AssertWellFormed(r);
    }

    [Fact]
    public void Island_DrainsToTheSurroundingSea()
    {
        const int Side = 13;
        float[] h = Grid(Side, (x, z) =>
        {
            double d = Math.Sqrt(((x - 6) * (x - 6)) + ((z - 6) * (z - 6)));
            return d < 4 ? 20 - (float)(d * 4) : -10;
        });
        FlowRouting r = Route(h, Side, seaLevel: 0);

        AssertWellFormed(r);
        int summit = (6 * Side) + 6;
        int at = summit;
        for (int steps = 0; steps < 100 && !r.IsOutlet[at]; steps++)
        {
            at = r.Receiver[at];
        }

        Assert.True(r.IsOutlet[at]);
        Assert.True(h[at] <= 0, "water from the summit should end in the sea");
    }

    [Fact]
    public void DistanceToReceiver_IsSpacing_OrSpacingTimesRootTwo()
    {
        const int Side = 9;
        float[] h = Grid(Side, (x, z) => (x + z) * 5f); // steepest descent is diagonal toward the north-west
        FlowRouting r = Route(h, Side);

        int interior = (4 * Side) + 4;
        Assert.Equal(interior - Side - 1, r.Receiver[interior]);
        Assert.Equal(Spacing * Math.Sqrt(2), r.DistanceToReceiver(interior), 9);

        float[] west = Grid(Side, (x, _) => x * 5f);
        FlowRouting rw = Route(west, Side);
        Assert.Equal(Spacing, rw.DistanceToReceiver(interior));
    }
}
