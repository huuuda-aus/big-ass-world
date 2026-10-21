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
}
