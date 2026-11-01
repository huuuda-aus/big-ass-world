using System.Collections.Generic;

namespace Realm.Gen.Settlements.Geometry;

/// <summary>Point tests against a closed polygon that need not be convex (unlike <see cref="ConvexPoly"/>).</summary>
public static class PolygonMath
{
    /// <summary>Whether a point lies inside a closed, simple (non-self-intersecting) polygon.</summary>
    /// <param name="polygon">Corners in order, closed (the last implicitly joins the first).</param>
    /// <param name="point">The point to test.</param>
    /// <returns>True if inside, by the standard even-odd ray-casting rule.</returns>
    public static bool Contains(IReadOnlyList<Vec2> polygon, Vec2 point)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            Vec2 a = polygon[i];
            Vec2 b = polygon[j];
            if ((a.Y > point.Y) != (b.Y > point.Y) && point.X < ((b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y)) + a.X)
            {
                inside = !inside;
            }
        }

        return inside;
    }
}
