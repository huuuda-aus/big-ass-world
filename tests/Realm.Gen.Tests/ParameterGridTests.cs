using System;
using System.Linq;
using Realm.Gen.Terrain;
using Xunit;

namespace Realm.Gen.Tests;

public class ParameterGridTests
{
    private const int Side = 16;
    private const int Cell = 64;

    private static ParameterGrid Grid(Func<int, int, double> value)
    {
        var values = new double[Side * Side];
        for (int z = 0; z < Side; z++)
        {
            for (int x = 0; x < Side; x++)
            {
                values[(z * Side) + x] = value(x, z);
            }
        }

        return new ParameterGrid(Side, Cell, values);
    }

    [Fact]
    public void ConstantGrid_IsUnchangedByBlur()
    {
        ParameterGrid g = Grid((_, _) => 7.5);
        g.Blur(3);

        Assert.All(g.Values.ToArray(), v => Assert.Equal(7.5, v, 12));
    }

    [Fact]
    public void ZeroRadius_DoesNothing()
    {
        ParameterGrid g = Grid((x, _) => x < 8 ? 0 : 100);
        g.Blur(0);

        Assert.Equal(0, g.Values[7]);
        Assert.Equal(100, g.Values[8]);
    }

    [Fact]
    public void Step_BecomesASmoothMonotonicRamp_WithinTheOriginalRange()
    {
        ParameterGrid g = Grid((x, _) => x < 8 ? 0 : 100);
        g.Blur(2);

        double previous = double.MinValue;
        for (int x = 0; x < Side; x++)
        {
            double v = g.Values[(5 * Side) + x];
            Assert.InRange(v, 0, 100);
            Assert.True(v >= previous, $"not monotonic at {x}");
            previous = v;
        }

        // The jump of 100 between neighbours is now spread: no neighbouring pair differs by more than a third of it.
        for (int x = 0; x + 1 < Side; x++)
        {
            Assert.True(g.Values[(5 * Side) + x + 1] - g.Values[(5 * Side) + x] < 100 / 3.0);
        }
    }

    [Fact]
    public void Step_IsSymmetricAroundTheBorder()
    {
        ParameterGrid g = Grid((x, _) => x < 8 ? 0 : 100);
        g.Blur(2);

        // Cells 7 and 8 straddle the border, so they mirror each other: values add up to 100.
        Assert.Equal(100.0, g.Values[(5 * Side) + 7] + g.Values[(5 * Side) + 8], 9);
        Assert.Equal(100.0, g.Values[(5 * Side) + 6] + g.Values[(5 * Side) + 9], 9);
    }

    [Fact]
    public void Blur_PreservesTheMean_WhenFarFromTheEdges()
    {
        // A single bright cell in the middle: the box blur only spreads it.
        ParameterGrid g = Grid((x, z) => x == 8 && z == 8 ? 100 : 0);
        double before = g.Values.ToArray().Sum();
        g.Blur(2);

        Assert.Equal(before, g.Values.ToArray().Sum(), 9);
    }

    [Fact]
    public void Sample_AtCellCentre_ReturnsTheCellValue()
    {
        ParameterGrid g = Grid((x, z) => (x * 10) + z);
        double half = Side * Cell / 2.0;

        // Centre of cell (3, 5) is at -half + 3.5 * Cell.
        Assert.Equal(35.0, g.Sample(-half + (3.5 * Cell), -half + (5.5 * Cell)), 12);
    }

    [Fact]
    public void Sample_BetweenCentres_Interpolates()
    {
        ParameterGrid g = Grid((x, _) => x * 10);
        double half = Side * Cell / 2.0;

        // Halfway between the centres of cells 3 and 4: value between 30 and 40.
        Assert.Equal(35.0, g.Sample(-half + (4.0 * Cell), 0), 12);
    }

    [Fact]
    public void Sample_OutsideTheGrid_IsClamped()
    {
        ParameterGrid g = Grid((x, _) => x);
        double half = Side * Cell / 2.0;

        Assert.Equal(0.0, g.Sample(-half - 1000, 0), 12);
        Assert.Equal(Side - 1.0, g.Sample(half + 1000, 0), 12);
    }
}
