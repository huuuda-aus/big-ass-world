using System;
using System.Linq;
using Realm.Gen.Terrain;
using Realm.Gen.Terrain.Erosion;
using Xunit;

namespace Realm.Gen.Tests.Erosion;

public class GorgeCarverTests
{
    private const int World = 512;
    private const int Spacing = 8;
    private const int Side = (World / Spacing) + 1;
    private const float Ground = 100f;

    private static readonly ErosionSettings Settings = new();

    private static ParameterGrid DepthGrid(double depth) =>
        new(World / 64, 64, Enumerable.Repeat(depth, (World / 64) * (World / 64)).ToArray());

    private static River StraightRiver(double flow, int index = 0) =>
        new(index, new[] { new RiverPoint(-200, 0, 100, flow), new RiverPoint(200, 0, 100, flow) }, RiverMouth.Sea, null, 400);

    private static float[] Flat() => Enumerable.Repeat(Ground, Side * Side).ToArray();

    private static float At(float[] heights, double x, double z) =>
        heights[(((int)Math.Round((z + (World / 2.0)) / Spacing)) * Side) + (int)Math.Round((x + (World / 2.0)) / Spacing)];

    [Fact]
    public void BigRiver_CutsAFlatChannel_WithWallsRisingAtTheConfiguredSlope()
    {
        float[] heights = Flat();

        GorgeCarver.Carve(heights, Side, Spacing, World, new[] { StraightRiver(20) }, DepthGrid(30), Settings);

        // Flow 20 km2 is above the full-depth flow, so the floor is the full 30 m below the ground; the channel is
        // (4 + 2 * sqrt(20)) / 2 = 6.47 m either side of the centre line.
        Assert.Equal(70.0, At(heights, 0, 0), 3);
        double expected8 = 70 + (2.0 * (8 - 6.472135955)); // 8 m away is just outside the 6.47 m half width: the wall has begun
        Assert.Equal(expected8, At(heights, 0, 8), 2);
        double expected16 = 70 + (2.0 * (16 - 6.472135955));
        Assert.Equal(expected16, At(heights, 0, 16), 2);
        Assert.Equal(expected16, At(heights, 0, -16), 2); // symmetric about the centre line
    }

    [Fact]
    public void Wall_MeetsTheGround_AtDepthOverSlopePlusHalfWidth_AndNothingBeyondChangesAnyHeight()
    {
        float[] heights = Flat();

        GorgeCarver.Carve(heights, Side, Spacing, World, new[] { StraightRiver(20) }, DepthGrid(30), Settings);

        // 30 m deep at slope 2 reaches the ground 15 m beyond the half width: 6.47 + 15 = 21.5 m.
        Assert.Equal(Ground, At(heights, 0, 24));
        Assert.Equal(Ground, At(heights, 0, 80));
        Assert.Equal(Ground, At(heights, 0, -200));
    }

    [Fact]
    public void Carving_NeverRaisesAnything()
    {
        float[] heights = Flat();
        for (int i = 0; i < heights.Length; i++)
        {
            heights[i] = Ground + ((i % 7) * 3) - 9;
        }

        float[] before = (float[])heights.Clone();
        GorgeCarver.Carve(heights, Side, Spacing, World, new[] { StraightRiver(20) }, DepthGrid(30), Settings);

        for (int i = 0; i < heights.Length; i++)
        {
            Assert.True(heights[i] <= before[i], $"vertex {i} was raised");
        }
    }

    [Fact]
    public void SmallRiver_CutsOnlyAShallowChannel()
    {
        float[] heights = Flat();

        GorgeCarver.Carve(heights, Side, Spacing, World, new[] { StraightRiver(1) }, DepthGrid(30), Settings);

        // 1 km2 is a tenth of the full flow: smoothstep(0.1) = 0.028, so under a metre.
        Assert.InRange(Ground - At(heights, 0, 0), 0.01, 1.0);
    }

    [Fact]
    public void ZeroBiomeDepth_ChangesNothing()
    {
        float[] heights = Flat();

        int lowered = GorgeCarver.Carve(heights, Side, Spacing, World, new[] { StraightRiver(20) }, DepthGrid(0), Settings);

        Assert.Equal(0, lowered);
        Assert.All(heights, h => Assert.Equal(Ground, h));
    }

    [Fact]
    public void RiverOrder_DoesNotMatter()
    {
        River a = StraightRiver(20, 0);
        River b = new(1, new[] { new RiverPoint(0, -200, 100, 15), new RiverPoint(0, 200, 100, 15) }, RiverMouth.Sea, null, 400);
        float[] one = Flat();
        float[] two = Flat();

        GorgeCarver.Carve(one, Side, Spacing, World, new[] { a, b }, DepthGrid(30), Settings);
        GorgeCarver.Carve(two, Side, Spacing, World, new[] { b, a }, DepthGrid(30), Settings);

        Assert.Equal(one, two);
    }

    [Fact]
    public void Depth_FollowsASmoothstepOfFlow_AndFadesTowardTheCoast()
    {
        Assert.Equal(0, GorgeCarver.Depth(30, 0, 100, Settings), 9);
        Assert.Equal(15, GorgeCarver.Depth(30, 5, 100, Settings), 9);   // half the full flow: smoothstep(0.5) = 0.5
        Assert.Equal(30, GorgeCarver.Depth(30, 50, 100, Settings), 9);  // beyond the full flow
        Assert.Equal(15, GorgeCarver.Depth(30, 50, 10, Settings), 9);   // 10 m above sea level is half the 20 m fade height
        Assert.Equal(0, GorgeCarver.Depth(30, 50, 0, Settings), 9);     // at sea level: no gorge
        Assert.Equal(0, GorgeCarver.Depth(30, 50, -5, Settings), 9);
    }

    [Fact]
    public void HalfWidth_IsHalfTheBaseWidthPlusTheFlowTerm()
    {
        Assert.Equal(2.0, GorgeCarver.HalfWidth(0, Settings), 9);
        Assert.Equal((4 + (2 * 3)) / 2.0, GorgeCarver.HalfWidth(9, Settings), 9);
    }
}
