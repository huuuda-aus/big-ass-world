using System;
using System.Collections.Generic;
using System.Linq;

namespace Realm.Gen.Settlements.Geometry;

/// <summary>
/// Bounded Voronoi diagram built by clipping a box against every bisector.
/// O(n^2), fine for the few hundred sites a city needs. Edge tags hold the index of the
/// neighbouring site, or -1 for the bounding box.
/// </summary>
public static class Voronoi
{
    /// <summary>Computes the Voronoi cell of every site inside a square.</summary>
    /// <param name="sites">Cell centres.</param>
    /// <param name="halfSize">Half the side of the bounding square, centred on the origin.</param>
    /// <returns>One cell per site; edge tags hold the index of the neighbouring site, or -1 for the bounding square.</returns>
    public static ConvexPoly[] Cells(IReadOnlyList<Vec2> sites, double halfSize)
    {
        var cells = new ConvexPoly[sites.Count];
        for (int i = 0; i < sites.Count; i++)
        {
            ConvexPoly? cell = ConvexPoly.Rect(-halfSize, -halfSize, halfSize, halfSize);
            Vec2 s = sites[i];

            foreach (int j in Enumerable.Range(0, sites.Count)
                         .Where(j => j != i)
                         .OrderBy(j => Vec2.Distance(s, sites[j])))
            {
                Vec2 o = sites[j];
                cell = cell!.Clip(Vec2.Lerp(s, o, 0.5), o - s, j);
                if (cell == null) break;
            }

            cells[i] = cell ?? throw new InvalidOperationException("degenerate Voronoi cell");
        }
        return cells;
    }

    /// <summary>Lloyd relaxation: move each site to the centroid of its cell.</summary>
    public static Vec2[] Relax(IReadOnlyList<Vec2> sites, double halfSize, int iterations)
    {
        Vec2[] cur = sites.ToArray();
        for (int it = 0; it < iterations; it++)
        {
            ConvexPoly[] cells = Cells(cur, halfSize);
            for (int i = 0; i < cur.Length; i++)
                cur[i] = cells[i].Centroid;
        }
        return cur;
    }
}
