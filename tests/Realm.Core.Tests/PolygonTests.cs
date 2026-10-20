using System;
using System.Collections.Generic;
using System.Linq;
using Realm.Core.Geometry;
using Xunit;

namespace Realm.Core.Tests;

public class PolygonTests
{
    private static Point2D[] Rect(double x0, double z0, double x1, double z1) =>
        new[] { new Point2D(x0, z0), new Point2D(x1, z0), new Point2D(x1, z1), new Point2D(x0, z1) };

    private static Polygon Make(IReadOnlyList<Point2D> points)
    {
        Assert.True(Polygon.TryCreate(points, out Polygon? polygon, out string? problem), problem);
        return polygon!;
    }

    [Fact]
    public void Rectangle_HasAreaAndBounds()
    {
        Polygon p = Make(Rect(-10, -5, 10, 5));

        Assert.Equal(200, p.Area);
        Assert.Equal(-10, p.MinX);
        Assert.Equal(10, p.MaxX);
        Assert.Equal(-5, p.MinZ);
        Assert.Equal(5, p.MaxZ);
        Assert.Equal(4, p.Points.Count);
    }

    [Fact]
    public void Winding_DoesNotMatter()
    {
        Polygon cw = Make(Rect(0, 0, 4, 4));
        Polygon ccw = Make(Rect(0, 0, 4, 4).Reverse().ToArray());

        Assert.Equal(cw.Area, ccw.Area);
        Assert.Equal(cw.Contains(new Point2D(1, 1)), ccw.Contains(new Point2D(1, 1)));
    }

    [Fact]
    public void Triangle_Area()
    {
        Polygon p = Make(new[] { new Point2D(0, 0), new Point2D(6, 0), new Point2D(0, 4) });

        Assert.Equal(12, p.Area);
    }

    [Fact]
    public void Concave_Contains_RespectsTheNotch()
    {
        // An "L" shape.
        Polygon p = Make(new[]
        {
            new Point2D(0, 0), new Point2D(10, 0), new Point2D(10, 4),
            new Point2D(4, 4), new Point2D(4, 10), new Point2D(0, 10),
        });

        Assert.True(p.Contains(new Point2D(2, 8)));
        Assert.True(p.Contains(new Point2D(8, 2)));
        Assert.False(p.Contains(new Point2D(8, 8)));
        Assert.False(p.Contains(new Point2D(-1, 5)));
        Assert.False(p.Contains(new Point2D(5, 11)));
    }

    [Fact]
    public void TryCreate_RejectsTooFewVertices()
    {
        Assert.False(Polygon.TryCreate(new[] { new Point2D(0, 0), new Point2D(1, 1) }, out Polygon? p, out string? problem));
        Assert.Null(p);
        Assert.Contains("at least 3", problem);
    }

    [Fact]
    public void TryCreate_RejectsNonFinite()
    {
        var pts = new[] { new Point2D(0, 0), new Point2D(1, 0), new Point2D(double.NaN, 1) };

        Assert.False(Polygon.TryCreate(pts, out _, out string? problem));
        Assert.Contains("non-finite", problem);
    }

    [Fact]
    public void TryCreate_RejectsRepeatedVertex()
    {
        var pts = new[] { new Point2D(0, 0), new Point2D(1, 0), new Point2D(1, 0), new Point2D(1, 1) };

        Assert.False(Polygon.TryCreate(pts, out _, out string? problem));
        Assert.Contains("identical", problem);
    }

    [Fact]
    public void TryCreate_RejectsZeroArea()
    {
        var pts = new[] { new Point2D(0, 0), new Point2D(1, 1), new Point2D(2, 2) };

        Assert.False(Polygon.TryCreate(pts, out _, out string? problem));
        Assert.Contains("zero area", problem);
    }

    [Fact]
    public void TryCreate_RejectsBowTie()
    {
        var pts = new[] { new Point2D(0, 0), new Point2D(4, 4), new Point2D(4, 0), new Point2D(0, 4) };

        Assert.False(Polygon.TryCreate(pts, out _, out string? problem));
        Assert.Contains("cross or touch", problem);
    }

    [Fact]
    public void TryCreate_RejectsSpike()
    {
        // The last edge goes back over the previous one.
        var pts = new[] { new Point2D(0, 0), new Point2D(4, 0), new Point2D(4, 4), new Point2D(4, 2), new Point2D(0, 4) };

        Assert.False(Polygon.TryCreate(pts, out _, out string? problem));
        Assert.NotNull(problem);
    }

    [Fact]
    public void TryCreate_RejectsTouchingEdges()
    {
        // A vertex lying exactly on a non-neighbouring edge.
        var pts = new[]
        {
            new Point2D(0, 0), new Point2D(10, 0), new Point2D(10, 10), new Point2D(5, 0), new Point2D(0, 10),
        };

        Assert.False(Polygon.TryCreate(pts, out _, out string? problem));
        Assert.NotNull(problem);
    }

    [Fact]
    public void TryCreate_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => Polygon.TryCreate(null!, out _, out _));
    }

    [Fact]
    public void TilingPolygons_EveryPointBelongsToExactlyOne_IncludingSharedEdgesAndVertices()
    {
        // Four rectangles tiling a 2x2 square, sharing edges and the centre vertex.
        Polygon[] tiles =
        {
            Make(Rect(0, 0, 5, 5)), Make(Rect(5, 0, 10, 5)),
            Make(Rect(0, 5, 5, 10)), Make(Rect(5, 5, 10, 10)),
        };

        // Interior, shared vertical edge, shared horizontal edge, centre vertex, outer edges.
        var probes = new List<Point2D>();
        for (double x = 0; x <= 10; x += 2.5)
        {
            for (double z = 0; z <= 10; z += 2.5)
            {
                probes.Add(new Point2D(x, z));
            }
        }

        foreach (Point2D probe in probes)
        {
            int owners = tiles.Count(t => t.Contains(probe));
            bool insideWorld = probe.X < 10 && probe.Z < 10; // far edges are outside by the half-open rule
            Assert.True(owners == (insideWorld ? 1 : 0), $"{probe} owned by {owners}");
        }
    }

    [Fact]
    public void TilingByIrregularSharedBorder_HasNoGapsOrOverlaps()
    {
        // Two polygons sharing a zig-zag border.
        Polygon top = Make(new[]
        {
            new Point2D(0, 0), new Point2D(10, 0), new Point2D(10, 4), new Point2D(5, 7), new Point2D(0, 3),
        });
        Polygon bottom = Make(new[]
        {
            new Point2D(0, 3), new Point2D(5, 7), new Point2D(10, 4), new Point2D(10, 10), new Point2D(0, 10),
        });

        for (double x = 0.5; x < 10; x += 0.5)
        {
            for (double z = 0.25; z < 10; z += 0.25)
            {
                var p = new Point2D(x, z);
                int owners = (top.Contains(p) ? 1 : 0) + (bottom.Contains(p) ? 1 : 0);
                Assert.True(owners == 1, $"{p} owned by {owners}");
            }
        }
    }

    [Fact]
    public void Points_AreCopied_SoLaterChangesDoNotAffectThePolygon()
    {
        Point2D[] source = Rect(0, 0, 4, 4);
        Polygon p = Make(source);
        source[0] = new Point2D(-100, -100);

        Assert.Equal(16, p.Area);
        Assert.Equal(0, p.MinX);
    }
}
