using System;
using System.Collections.Generic;

namespace Realm.Gen.Terrain.Erosion;

/// <summary>Turns routed flow into river polylines and lakes.</summary>
/// <remarks>
/// A river vertex is a land vertex whose flow reaches the river threshold. Rivers start at river vertices
/// with no river vertex upstream and follow their receivers. Sources are traced longest path first, so the
/// longest river keeps shared vertices and shorter ones end in a confluence with it.
/// Determinism: ties are broken by vertex index. Thread-safety: stateless.
/// </remarks>
internal static class RiverExtractor
{
    private const double MetresPerKilometre = 1000.0;
    private const float SameLevelTolerance = 1e-3f;

    private static readonly (int Dx, int Dz)[] Neighbours =
    {
        (-1, -1), (0, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (0, 1), (1, 1),
    };

    /// <summary>Extracts rivers.</summary>
    /// <param name="routing">Routing of <paramref name="heights"/>.</param>
    /// <param name="heights">Heights used to tell land from sea.</param>
    /// <param name="flowKm2">Accumulated flow.</param>
    /// <param name="thresholdKm2">Minimum flow for a river vertex.</param>
    /// <param name="seaLevel">Height at or below which a vertex is sea.</param>
    /// <param name="riversReachWater">When true a river never dries up: once a vertex is part of a river, every vertex downstream of it is too, down to the sea, the world edge or a river it joins, however little flow is left (water loss in a dry biome makes it thin, not extinct). When false a river ends where its flow falls below the threshold (<see cref="RiverMouth.Dried"/>).</param>
    /// <returns>Rivers, longest first.</returns>
    public static List<River> ExtractRivers(FlowRouting routing, float[] heights, double[] flowKm2, double thresholdKm2, float seaLevel, bool riversReachWater = false)
    {
        int side = routing.Side;
        int count = side * side;
        double half = (side - 1) * routing.Spacing / 2.0;

        var isRiver = new bool[count];
        for (int i = 0; i < count; i++)
        {
            isRiver[i] = heights[i] > seaLevel && flowKm2[i] >= thresholdKm2;
        }

        if (riversReachWater)
        {
            // Routing lists receivers before their donors, so walking it backwards visits every donor before its receiver.
            for (int k = routing.Order.Length - 1; k >= 0; k--)
            {
                int i = routing.Order[k];
                int receiver = routing.Receiver[i];
                if (isRiver[i] && receiver >= 0 && heights[receiver] > seaLevel)
                {
                    isRiver[receiver] = true;
                }
            }
        }

        var hasRiverDonor = new bool[count];
        for (int i = 0; i < count; i++)
        {
            int receiver = routing.Receiver[i];
            if (isRiver[i] && receiver >= 0 && isRiver[receiver])
            {
                hasRiverDonor[receiver] = true;
            }
        }

        // Distance from each vertex to the outlet it finally drains to, receivers first.
        var distanceToOutlet = new double[count];
        foreach (int i in routing.Order)
        {
            int receiver = routing.Receiver[i];
            distanceToOutlet[i] = receiver < 0 ? 0 : distanceToOutlet[receiver] + routing.DistanceToReceiver(i);
        }

        var sources = new List<int>();
        for (int i = 0; i < count; i++)
        {
            if (isRiver[i] && !hasRiverDonor[i])
            {
                sources.Add(i);
            }
        }

        sources.Sort((a, b) =>
        {
            int byLength = distanceToOutlet[b].CompareTo(distanceToOutlet[a]);
            return byLength != 0 ? byLength : a.CompareTo(b);
        });

        var claimedBy = new int[count];
        Array.Fill(claimedBy, -1);
        var rivers = new List<River>();

        foreach (int source in sources)
        {
            int index = rivers.Count;
            var points = new List<RiverPoint>();
            double length = 0;
            RiverMouth mouth;
            int? joins = null;
            int current = source;

            while (true)
            {
                points.Add(Point(routing, heights, flowKm2, current, half));
                claimedBy[current] = index;
                int next = routing.Receiver[current];

                if (next < 0)
                {
                    mouth = RiverMouth.WorldEdge;
                    break;
                }

                double segment = routing.DistanceToReceiver(current);

                if (heights[next] <= seaLevel)
                {
                    length += segment;
                    points.Add(Point(routing, heights, flowKm2, next, half));
                    mouth = RiverMouth.Sea;
                    break;
                }

                if (!isRiver[next])
                {
                    mouth = RiverMouth.Dried;
                    break;
                }

                if (claimedBy[next] >= 0)
                {
                    length += segment;
                    points.Add(Point(routing, heights, flowKm2, next, half));
                    mouth = RiverMouth.Confluence;
                    joins = claimedBy[next];
                    break;
                }

                length += segment;
                current = next;
            }

            if (points.Count >= 2)
            {
                rivers.Add(new River(index, RiverWidth.Apply(points, index, mouth), mouth, joins, length));
            }
            else
            {
                // A one-point river is not kept; release its vertex so later rivers can pass through it.
                claimedBy[current] = -1;
            }
        }

        return rivers;
    }

    /// <summary>Finds lakes: connected groups of filled vertices with the same water level.</summary>
    /// <param name="routing">Routing with lake flags.</param>
    /// <param name="minAreaKm2">Lakes smaller than this surface area are dropped.</param>
    /// <returns>Lakes in scan order.</returns>
    public static List<Lake> ExtractLakes(FlowRouting routing, double minAreaKm2)
    {
        int side = routing.Side;
        double half = (side - 1) * routing.Spacing / 2.0;
        double cellAreaKm2 = CellAreaKm2(routing);
        var lakes = new List<Lake>();

        foreach ((int[] cells, float level) in FindLakeComponents(routing))
        {
            double area = cells.Length * cellAreaKm2;
            if (area < minAreaKm2)
            {
                continue;
            }

            var centres = new List<(double X, double Z)>(cells.Length);
            double sumX = 0;
            double sumZ = 0;
            foreach (int cell in cells)
            {
                double x = -half + ((cell % side) * routing.Spacing);
                double z = -half + ((cell / side) * routing.Spacing);
                centres.Add((x, z));
                sumX += x;
                sumZ += z;
            }

            lakes.Add(new Lake(lakes.Count, level, area, sumX / cells.Length, sumZ / cells.Length, centres));
        }

        return lakes;
    }

    /// <summary>
    /// Raises every basin that is not a kept lake to its water level: small and shallow depressions stand for
    /// sediment-filled floodplains that rivers cross. Kept lakes (surface area at least <paramref name="minAreaKm2"/>
    /// and filled by at least the routing's lake depth) are left as they are.
    /// </summary>
    /// <param name="routing">Routing of <paramref name="heights"/>.</param>
    /// <param name="heights">Heights; not modified.</param>
    /// <param name="minAreaKm2">Smallest lake to keep.</param>
    /// <returns>The heights with the other basins filled.</returns>
    public static float[] FillSmallBasins(FlowRouting routing, float[] heights, double minAreaKm2)
    {
        var keep = new bool[heights.Length];
        double cellAreaKm2 = CellAreaKm2(routing);
        foreach ((int[] cells, _) in FindLakeComponents(routing))
        {
            if (cells.Length * cellAreaKm2 >= minAreaKm2)
            {
                foreach (int cell in cells)
                {
                    keep[cell] = true;
                }
            }
        }

        float[] result = (float[])heights.Clone();
        for (int i = 0; i < result.Length; i++)
        {
            if (!keep[i] && routing.Filled[i] > result[i])
            {
                result[i] = routing.Filled[i];
            }
        }

        return result;
    }

    private static double CellAreaKm2(FlowRouting routing) =>
        (routing.Spacing / MetresPerKilometre) * (routing.Spacing / MetresPerKilometre);

    private static List<(int[] Cells, float Level)> FindLakeComponents(FlowRouting routing)
    {
        int side = routing.Side;
        var seen = new bool[side * side];
        var components = new List<(int[], float)>();
        var stack = new Stack<int>();

        for (int start = 0; start < seen.Length; start++)
        {
            if (seen[start] || !routing.IsLake[start])
            {
                continue;
            }

            float level = routing.Filled[start];
            var cells = new List<int>();
            seen[start] = true;
            stack.Push(start);
            while (stack.Count > 0)
            {
                int cell = stack.Pop();
                cells.Add(cell);
                int cx = cell % side;
                int cz = cell / side;
                foreach ((int dx, int dz) in Neighbours)
                {
                    int nx = cx + dx;
                    int nz = cz + dz;
                    if ((uint)nx >= (uint)side || (uint)nz >= (uint)side)
                    {
                        continue;
                    }

                    int n = (nz * side) + nx;
                    if (!seen[n] && routing.IsLake[n] && Math.Abs(routing.Filled[n] - level) <= SameLevelTolerance)
                    {
                        seen[n] = true;
                        stack.Push(n);
                    }
                }
            }

            cells.Sort();
            components.Add((cells.ToArray(), level));
        }

        return components;
    }

    private static RiverPoint Point(FlowRouting routing, float[] heights, double[] flowKm2, int index, double half)
    {
        int side = routing.Side;
        double x = -half + ((index % side) * routing.Spacing);
        double z = -half + ((index / side) * routing.Spacing);
        return new RiverPoint(x, z, Math.Max(heights[index], routing.Filled[index]), flowKm2[index]);
    }
}
