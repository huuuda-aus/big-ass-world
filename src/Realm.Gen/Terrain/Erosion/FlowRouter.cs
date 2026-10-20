using System;
using System.Collections.Generic;

namespace Realm.Gen.Terrain.Erosion;

/// <summary>
/// Priority-flood routing (Barnes, Lehman and Mulla): visits vertices from the outlets upward in order of
/// height, which fills every depression to its lowest spill point, then lets each vertex drain to its
/// steepest lower neighbour among those visited before it.
/// </summary>
/// <remarks>
/// Determinism: the queue key is (height, index), so equal heights are visited in index order and the
/// result never depends on insertion order. Thread-safety: stateless.
/// </remarks>
internal static class FlowRouter
{
    private const double DiagonalFactor = 1.4142135623730951;

    private static readonly (int Dx, int Dz)[] Neighbours =
    {
        (-1, -1), (0, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (0, 1), (1, 1),
    };

    /// <summary>Routes water over a square height grid.</summary>
    /// <param name="heights">Heights in metres, row by row.</param>
    /// <param name="side">Vertices per side.</param>
    /// <param name="spacing">Metres between neighbouring vertices.</param>
    /// <param name="seaLevel">Vertices at or below this height are outlets.</param>
    /// <param name="lakeMinDepth">Depressions shallower than this are not reported as lakes.</param>
    /// <returns>The routing.</returns>
    public static FlowRouting Route(float[] heights, int side, double spacing, float seaLevel, float lakeMinDepth)
    {
        int count = side * side;
        var filled = new float[count];
        var receiver = new int[count];
        var isOutlet = new bool[count];
        var visited = new bool[count];
        var rank = new int[count];
        var order = new int[count];
        int popped = 0;

        var queue = new PriorityQueue<int, (float Height, int Index)>(count / 4);
        for (int z = 0; z < side; z++)
        {
            for (int x = 0; x < side; x++)
            {
                int index = (z * side) + x;
                bool edge = x == 0 || z == 0 || x == side - 1 || z == side - 1;
                if (edge || heights[index] <= seaLevel)
                {
                    isOutlet[index] = true;
                    visited[index] = true;
                    filled[index] = heights[index];
                    receiver[index] = -1;
                    queue.Enqueue(index, (filled[index], index));
                }
            }
        }

        while (queue.TryDequeue(out int current, out _))
        {
            rank[current] = popped;
            order[popped++] = current;
            int cx = current % side;
            int cz = current / side;
            foreach ((int dx, int dz) in Neighbours)
            {
                int nx = cx + dx;
                int nz = cz + dz;
                if ((uint)nx >= (uint)side || (uint)nz >= (uint)side)
                {
                    continue;
                }

                int n = (nz * side) + nx;
                if (visited[n])
                {
                    continue;
                }

                visited[n] = true;
                filled[n] = Math.Max(heights[n], filled[current]);
                receiver[n] = current;
                queue.Enqueue(n, (filled[n], n));
            }
        }

        // Replace the discovering neighbour by the steepest earlier-visited one; earlier rank keeps it acyclic.
        for (int i = 0; i < count; i++)
        {
            if (isOutlet[i])
            {
                continue;
            }

            int x = i % side;
            int z = i / side;
            int best = receiver[i];
            double bestSlope = Slope(filled, i, best, side, spacing);
            foreach ((int dx, int dz) in Neighbours)
            {
                int nx = x + dx;
                int nz = z + dz;
                if ((uint)nx >= (uint)side || (uint)nz >= (uint)side)
                {
                    continue;
                }

                int n = (nz * side) + nx;
                if (rank[n] >= rank[i])
                {
                    continue;
                }

                double slope = Slope(filled, i, n, side, spacing);
                if (slope > bestSlope || (slope == bestSlope && n < best))
                {
                    best = n;
                    bestSlope = slope;
                }
            }

            receiver[i] = best;
        }

        var isLake = new bool[count];
        for (int i = 0; i < count; i++)
        {
            isLake[i] = !isOutlet[i] && filled[i] - heights[i] >= lakeMinDepth;
        }

        return new FlowRouting(side, spacing, filled, receiver, order, isOutlet, isLake);
    }

    private static double Slope(float[] filled, int from, int to, int side, double spacing)
    {
        bool diagonal = (from % side != to % side) && (from / side != to / side);
        double distance = diagonal ? spacing * DiagonalFactor : spacing;
        return (filled[from] - filled[to]) / distance;
    }
}
