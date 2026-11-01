using System;
using System.Linq;
using Realm.Gen.Settlements.Geometry;
using Xunit;

namespace Realm.Gen.Tests.Settlements;

public class ConvexPolyTests
{
    private static ConvexPoly Square() => new ConvexPoly(
        new[] { new Vec2(0, 0), new Vec2(10, 0), new Vec2(10, 10), new Vec2(0, 10) },
        new[] { 1, 2, 3, 4 });

    [Fact]
    public void ClipThroughAVertexTagsTheCutEdgeNotTheOldOne()
    {
        // The diagonal passes exactly through (0,0) and (10,10); keep the upper-left triangle.
        ConvexPoly? tri = Square().Clip(new Vec2(0, 0), new Vec2(1, -1), 9);

        Assert.NotNull(tri);
        Assert.Equal(3, tri!.Count);

        int edgeFromOrigin = Enumerable.Range(0, tri.Count).Single(i => tri.Points[i].Equals(new Vec2(0, 0)));
        Assert.Equal(9, tri.Tags[edgeFromOrigin]); // (0,0) -> (10,10) is the cut
        Assert.Equal(new[] { 3, 4 }, tri.Tags.Where(t => t != 9).OrderBy(t => t).ToArray());
    }

    [Fact]
    public void ClipKeepsOriginalTagsOnSurvivingEdges()
    {
        ConvexPoly? half = Square().Clip(new Vec2(5, 0), new Vec2(1, 0), 7);

        Assert.NotNull(half);
        Assert.Equal(5, half!.Area / 10, 6);
        Assert.Equal(new[] { 1, 3, 4, 7 }, half.Tags.OrderBy(t => t).ToArray());
    }

    [Fact]
    public void OutsetMovesEveryEdgeOutByTheSameDistance_EvenOnAnElongatedRectangle()
    {
        // 20 x 4: a per-vertex centroid-direction offset would under-shoot the perpendicular distance from the
        // long edges (their normal has a small component along the centroid-to-corner diagonal), leaving a gap
        // between the wall and the eave there. A correct offset keeps every edge at exactly `distance`.
        var rect = new ConvexPoly(
            new[] { new Vec2(0, 0), new Vec2(20, 0), new Vec2(20, 4), new Vec2(0, 4) },
            new[] { 0, 1, 2, 3 });

        ConvexPoly outset = rect.Outset(0.5);

        for (int i = 0; i < rect.Count; i++)
        {
            Vec2 a = rect.Points[i];
            Vec2 n = rect.OutwardNormal(i);
            foreach (Vec2 p in outset.Points)
            {
                // The offset polygon is the intersection of every edge's own outward-shifted half-plane, so no
                // corner (not even the ones on the far side) can lie beyond 0.5 m past any original edge.
                Assert.True(Vec2.Dot(n, p - a) <= 0.5 + 1e-6, $"edge {i}: corner {p} is {Vec2.Dot(n, p - a):F3} m out, more than the 0.5 m overhang");
            }

            // And the two corners adjacent to this edge touch its offset line exactly: the overhang is not short-changed.
            Assert.Contains(outset.Points, p => Math.Abs(Vec2.Dot(n, p - a) - 0.5) < 1e-6);
        }
    }

    [Fact]
    public void OutsetOfASquareGrowsItBySymmetrically()
    {
        ConvexPoly outset = Square().Outset(2);

        Assert.Equal(4, outset.Count);
        Assert.Equal(196, outset.Area, 6); // 14 x 14
        Assert.Equal(Square().Centroid.X, outset.Centroid.X, 6);
        Assert.Equal(Square().Centroid.Y, outset.Centroid.Y, 6);
    }
}
