using System;
using System.Collections.Generic;

namespace Realm.Gen.Settlements.Geometry;

/// <summary>
/// Convex counter-clockwise polygon. Every edge carries an integer tag so callers can
/// tell which original edges survive clipping (edge i runs from Points[i] to Points[i+1]).
/// </summary>
public sealed class ConvexPoly
{
    /// <summary>Corner points, counter-clockwise.</summary>
    public IReadOnlyList<Vec2> Points => _points;
    /// <summary>One integer tag per edge; edge i runs from <c>Points[i]</c> to <c>Points[i + 1]</c>.</summary>
    public IReadOnlyList<int> Tags => _tags;
    /// <summary>Number of corners (and edges).</summary>
    public int Count => _points.Count;

    private readonly List<Vec2> _points;
    private readonly List<int> _tags;

    /// <summary>Creates a polygon.</summary>
    /// <param name="points">Corners in counter-clockwise order.</param>
    /// <param name="tags">One tag per edge.</param>
    public ConvexPoly(IEnumerable<Vec2> points, IEnumerable<int> tags)
    {
        _points = new List<Vec2>(points);
        _tags = new List<int>(tags);
        if (_points.Count != _tags.Count)
            throw new ArgumentException("one tag per edge required");
    }

    /// <summary>Creates an axis-aligned rectangle.</summary>
    /// <param name="minX">Smallest X.</param>
    /// <param name="minY">Smallest Y.</param>
    /// <param name="maxX">Largest X.</param>
    /// <param name="maxY">Largest Y.</param>
    /// <param name="tag">Tag given to all four edges.</param>
    /// <returns>The rectangle.</returns>
    public static ConvexPoly Rect(double minX, double minY, double maxX, double maxY, int tag = -1)
    {
        return new ConvexPoly(
            new[] { new Vec2(minX, minY), new Vec2(maxX, minY), new Vec2(maxX, maxY), new Vec2(minX, maxY) },
            new[] { tag, tag, tag, tag });
    }

    /// <summary>The vector along an edge.</summary>
    /// <param name="i">Edge index.</param>
    /// <returns>End minus start of edge <paramref name="i"/>.</returns>
    public Vec2 Edge(int i) => _points[(i + 1) % _points.Count] - _points[i];
    /// <summary>The end point of an edge.</summary>
    /// <param name="i">Edge index.</param>
    /// <returns>The point edge <paramref name="i"/> ends at.</returns>
    public Vec2 EdgeEnd(int i) => _points[(i + 1) % _points.Count];
    /// <summary>The length of an edge.</summary>
    /// <param name="i">Edge index.</param>
    /// <returns>Length in metres.</returns>
    public double EdgeLength(int i) => Edge(i).Length;

    /// <summary>Area in square metres (positive for counter-clockwise winding).</summary>
    public double Area
    {
        get
        {
            double a = 0;
            for (int i = 0; i < _points.Count; i++)
                a += Vec2.Cross(_points[i], EdgeEnd(i));
            return a * 0.5;
        }
    }

    /// <summary>Centre of mass of the polygon area.</summary>
    public Vec2 Centroid
    {
        get
        {
            double a = 0, cx = 0, cy = 0;
            for (int i = 0; i < _points.Count; i++)
            {
                Vec2 p = _points[i], q = EdgeEnd(i);
                double c = Vec2.Cross(p, q);
                a += c;
                cx += (p.X + q.X) * c;
                cy += (p.Y + q.Y) * c;
            }
            if (Math.Abs(a) < 1e-9) return _points[0];
            return new Vec2(cx / (3 * a), cy / (3 * a));
        }
    }

    /// <summary>Outward unit normal of edge i (valid for counter-clockwise winding).</summary>
    public Vec2 OutwardNormal(int i)
    {
        Vec2 e = Edge(i);
        return new Vec2(e.Y, -e.X).Normalized();
    }

    /// <summary>Finds the longest edge.</summary>
    /// <returns>Index of the longest edge (the first one on ties).</returns>
    public int LongestEdge()
    {
        int best = 0;
        double bestLen = -1;
        for (int i = 0; i < _points.Count; i++)
        {
            double l = EdgeLength(i);
            if (l > bestLen) { bestLen = l; best = i; }
        }
        return best;
    }

    /// <summary>
    /// Keeps the part where dot(normal, p - origin) &lt;= 0. The edge created along the cut
    /// gets <paramref name="newTag"/>. Returns null when nothing meaningful remains.
    /// </summary>
    public ConvexPoly? Clip(Vec2 origin, Vec2 normal, int newTag)
    {
        var pts = new List<Vec2>();
        var tags = new List<int>();
        int n = _points.Count;
        for (int i = 0; i < n; i++)
        {
            Vec2 cur = _points[i], next = _points[(i + 1) % n];
            double dc = Vec2.Dot(normal, cur - origin);
            double dn = Vec2.Dot(normal, next - origin);
            bool curIn = dc <= 0, nextIn = dn <= 0;

            if (curIn)
            {
                pts.Add(cur);
                tags.Add(_tags[i]);
                if (!nextIn)
                {
                    pts.Add(Vec2.Lerp(cur, next, dc / (dc - dn)));
                    tags.Add(newTag);
                }
            }
            else if (nextIn)
            {
                pts.Add(Vec2.Lerp(cur, next, dc / (dc - dn)));
                tags.Add(_tags[i]);
            }
        }

        if (pts.Count < 3) return null;
        var result = new ConvexPoly(pts, tags);
        result.RemoveDuplicates();
        return result._points.Count < 3 || result.Area < 1e-6 ? null : result;
    }

    /// <summary>
    /// Shrinks the polygon, moving edge i inward by distances[i]. Edges keep their tag.
    /// </summary>
    public ConvexPoly? Inset(IReadOnlyList<double> distances)
    {
        ConvexPoly? cur = this;
        for (int i = 0; i < _points.Count && cur != null; i++)
        {
            Vec2 n = OutwardNormal(i);
            Vec2 origin = _points[i] - n * distances[i];
            cur = cur.Clip(origin, n, _tags[i]);
        }
        return cur;
    }

    private void RemoveDuplicates()
    {
        for (int i = _points.Count - 1; i >= 0 && _points.Count > 1; i--)
        {
            int next = (i + 1) % _points.Count;
            if (Vec2.Distance(_points[i], _points[next]) < 1e-7)
            {
                // edge i has zero length; the merged point starts the edge that left the duplicate
                _tags[i] = _tags[next];
                _points.RemoveAt(next);
                _tags.RemoveAt(next);
                if (next < i) i--;
            }
        }
    }
}
