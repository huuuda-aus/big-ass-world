using System;
using System.Linq;
using Realm.Core.Geometry;
using Xunit;

namespace Realm.Core.Tests;

public class RegionGridTests
{
    private const double Extent = 1024;

    [Theory]
    [InlineData(0.0, 0, 0)]
    [InlineData(1023.9, 0, 0)]
    [InlineData(1024.0, 1, 0)]
    [InlineData(-0.1, -1, 0)]
    [InlineData(-1024.0, -1, 0)]
    [InlineData(-1024.1, -2, 0)]
    public void RegionOf_FloorsTowardMinusInfinity(double value, int expected, int unused)
    {
        Assert.Equal(expected, RegionGrid.RegionOf(value, Extent));
        Assert.Equal(0, unused);
    }

    [Fact]
    public void Distance_IsZeroInside_AndStraightLineOutside()
    {
        Assert.Equal(0, RegionGrid.DistanceToRegion(500, 500, 0, 0, Extent));
        Assert.Equal(0, RegionGrid.DistanceToRegion(0, 1024, 0, 0, Extent)); // on the shared edge: touches (0,0)'s boundary
        Assert.Equal(100, RegionGrid.DistanceToRegion(1124, 500, 0, 0, Extent), 9);          // 100 m east of the east edge
        Assert.Equal(100, RegionGrid.DistanceToRegion(500, -100, 0, 0, Extent), 9);          // 100 m north of the north edge
        Assert.Equal(Math.Sqrt((30 * 30) + (40 * 40)), RegionGrid.DistanceToRegion(1054, 1064, 0, 0, Extent), 9); // diagonal off a corner
    }

    [Fact]
    public void Distance_WorksForNegativeRegions()
    {
        Assert.Equal(0, RegionGrid.DistanceToRegion(-500, -500, -1, -1, Extent));
        Assert.Equal(100, RegionGrid.DistanceToRegion(100, -500, -1, -1, Extent), 9);
    }

    [Fact]
    public void RegionsWithin_InTheMiddleOfARegion_ReturnsOnlyThatRegion()
    {
        var regions = RegionGrid.RegionsWithin(512, 512, 400, Extent, -16, 15);

        Assert.Equal((0, 0), (regions.Single().X, regions.Single().Z));
    }

    [Fact]
    public void RegionsWithin_NearACorner_ReturnsTheFourRegionsMeetingThere()
    {
        var regions = RegionGrid.RegionsWithin(10, 10, 300, Extent, -16, 15);

        Assert.Equal(
            new[] { (0, 0), (-1, 0), (0, -1), (-1, -1) }.OrderBy(r => r).ToArray(),
            regions.Select(r => (r.X, r.Z)).OrderBy(r => r).ToArray());
        Assert.Equal((0, 0), (regions[0].X, regions[0].Z)); // the containing region comes first
    }

    [Fact]
    public void RegionsWithin_NearAnEdge_AddsOnlyTheNeighbourInReach()
    {
        // 200 m from the east edge: a 300 m view reaches the next region east but not the ones diagonally.
        var near = RegionGrid.RegionsWithin(824, 512, 300, Extent, -16, 15);
        var far = RegionGrid.RegionsWithin(824, 512, 100, Extent, -16, 15);

        Assert.Equal(new[] { (0, 0), (1, 0) }, near.Select(r => (r.X, r.Z)).OrderBy(r => r).ToArray());
        Assert.Single(far);
    }

    [Fact]
    public void RegionsWithin_IsOrderedNearestFirst_AndDeterministic()
    {
        var a = RegionGrid.RegionsWithin(300, 700, 2000, Extent, -16, 15);
        var b = RegionGrid.RegionsWithin(300, 700, 2000, Extent, -16, 15);

        Assert.Equal(a, b);
        for (int i = 1; i < a.Count; i++)
        {
            Assert.True(a[i - 1].Distance <= a[i].Distance);
        }
    }

    [Fact]
    public void RegionsWithin_IsClippedToTheValidGrid()
    {
        var atEdge = RegionGrid.RegionsWithin(-16 * Extent + 10, 0, 1500, Extent, -16, 15);

        Assert.All(atEdge, r => Assert.InRange(r.X, -16, 15));
        Assert.DoesNotContain(atEdge, r => r.X < -16);
        Assert.Empty(RegionGrid.RegionsWithin(100 * Extent, 0, 500, Extent, -16, 15));
    }

    [Fact]
    public void ZeroDistance_ReturnsTheContainingRegion()
    {
        Assert.Single(RegionGrid.RegionsWithin(5, 5, 0, Extent, -16, 15));
    }

    [Fact]
    public void BadArguments_AreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RegionGrid.RegionsWithin(0, 0, -1, Extent, -16, 15));
        Assert.Throws<ArgumentOutOfRangeException>(() => RegionGrid.RegionsWithin(0, 0, 10, 0, -16, 15));
    }
}
