using System;
using System.Collections.Generic;

namespace Realm.Gen.Terrain.Erosion;

/// <summary>
/// Cuts gorges with steep cliff walls along the larger rivers. The river centre line is smoothed, a flat channel floor
/// sits <c>depth</c> metres below the ground beside the river, and the ground rises from the channel edge at the
/// configured wall slope until it meets the original terrain. Depth comes from the biome and grows with river flow.
/// </summary>
/// <remarks>
/// Determinism: every vertex takes the minimum over all river segments of a value that depends only on that segment
/// and the untouched input heights, so the result does not depend on processing order. Thread-safety: not safe to call
/// concurrently on the same array.
/// </remarks>
internal static class GorgeCarver
{
    private const int SmoothingPasses = 2;
    private const double ReachPerDepth = 3.0;
    private const double MinimumDepthMeters = 0.05;

    /// <summary>Carves the gorges into the heights.</summary>
    /// <param name="heights">Heights, row by row, <paramref name="side"/> x <paramref name="side"/> values; modified in place.</param>
    /// <param name="side">Vertices per side.</param>
    /// <param name="spacingMeters">Metres between vertices.</param>
    /// <param name="worldSizeMeters">World side length in metres (centred on the origin).</param>
    /// <param name="rivers">The rivers to follow.</param>
    /// <param name="gorgeDepth">Biome gorge depth in metres, blurred across region borders, sampled by world position.</param>
    /// <param name="settings">Wall slope, widths and fade.</param>
    /// <returns>Number of vertices whose height was lowered.</returns>
    public static int Carve(
        float[] heights,
        int side,
        int spacingMeters,
        int worldSizeMeters,
        IReadOnlyList<River> rivers,
        ParameterGrid gorgeDepth,
        ErosionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(heights);
        ArgumentNullException.ThrowIfNull(rivers);
        ArgumentNullException.ThrowIfNull(gorgeDepth);
        ArgumentNullException.ThrowIfNull(settings);

        double half = worldSizeMeters / 2.0;
        float[] reference = (float[])heights.Clone();
        var lowered = new bool[heights.Length];

        foreach (River river in rivers)
        {
            IReadOnlyList<RiverPoint> points = Smooth(river.Points);
            for (int i = 0; i + 1 < points.Count; i++)
            {
                CarveSegment(heights, reference, lowered, side, spacingMeters, half, points[i], points[i + 1], gorgeDepth, settings);
            }
        }

        int count = 0;
        foreach (bool changed in lowered)
        {
            count += changed ? 1 : 0;
        }

        return count;
    }

    /// <summary>The channel half-width in metres for a river of the given flow.</summary>
    /// <param name="flowKm2">Flow in square kilometres of catchment.</param>
    /// <param name="settings">Width settings.</param>
    /// <returns>Half of the channel floor width.</returns>
    public static double HalfWidth(double flowKm2, ErosionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return (settings.RiverBaseWidthMeters + (settings.RiverWidthPerSqrtKm2 * Math.Sqrt(Math.Max(0, flowKm2)))) / 2.0;
    }

    /// <summary>The gorge depth in metres for a river of the given flow and a biome depth.</summary>
    /// <param name="biomeDepth">The biome's full gorge depth in metres.</param>
    /// <param name="flowKm2">Flow of the river here.</param>
    /// <param name="groundHeight">Height of the ground beside the river, in metres above sea level.</param>
    /// <param name="settings">Flow and fade settings.</param>
    /// <returns>Metres of cut below the ground beside the river; 0 for small rivers and near the coast.</returns>
    public static double Depth(double biomeDepth, double flowKm2, double groundHeight, ErosionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        double flowShare = Math.Clamp(flowKm2 / settings.GorgeFullFlowKm2, 0.0, 1.0);
        double flowFactor = flowShare * flowShare * (3 - (2 * flowShare));
        double coastFactor = Math.Clamp(groundHeight / settings.GorgeFadeHeightMeters, 0.0, 1.0);
        return biomeDepth * flowFactor * coastFactor;
    }

    // Chaikin corner cutting rounds the 32 m stair-steps of the routing grid; flow and height are interpolated alongside.
    private static IReadOnlyList<RiverPoint> Smooth(IReadOnlyList<RiverPoint> points)
    {
        IReadOnlyList<RiverPoint> current = points;
        for (int pass = 0; pass < SmoothingPasses && current.Count > 2; pass++)
        {
            var next = new List<RiverPoint>(current.Count * 2) { current[0] };
            for (int i = 0; i + 1 < current.Count; i++)
            {
                RiverPoint a = current[i];
                RiverPoint b = current[i + 1];
                next.Add(Lerp(a, b, 0.25));
                next.Add(Lerp(a, b, 0.75));
            }

            next.Add(current[^1]);
            current = next;
        }

        return current;
    }

    private static RiverPoint Lerp(RiverPoint a, RiverPoint b, double t) =>
        new(a.X + ((b.X - a.X) * t), a.Z + ((b.Z - a.Z) * t), a.Height + ((b.Height - a.Height) * t), a.FlowKm2 + ((b.FlowKm2 - a.FlowKm2) * t));

    private static void CarveSegment(
        float[] heights, float[] reference, bool[] lowered, int side, int spacing, double half,
        RiverPoint a, RiverPoint b, ParameterGrid gorgeDepth, ErosionSettings settings)
    {
        double depthA = gorgeDepth.Sample(a.X, a.Z);
        double depthB = gorgeDepth.Sample(b.X, b.Z);
        double maxDepth = Math.Max(depthA, depthB);
        if (maxDepth < MinimumDepthMeters)
        {
            return;
        }

        double maxHalfWidth = Math.Max(HalfWidth(a.FlowKm2, settings), HalfWidth(b.FlowKm2, settings));
        double reach = maxHalfWidth + (ReachPerDepth * maxDepth);
        int col0 = Math.Max(0, (int)Math.Floor((Math.Min(a.X, b.X) - reach + half) / spacing));
        int col1 = Math.Min(side - 1, (int)Math.Ceiling((Math.Max(a.X, b.X) + reach + half) / spacing));
        int row0 = Math.Max(0, (int)Math.Floor((Math.Min(a.Z, b.Z) - reach + half) / spacing));
        int row1 = Math.Min(side - 1, (int)Math.Ceiling((Math.Max(a.Z, b.Z) + reach + half) / spacing));

        double sx = b.X - a.X;
        double sz = b.Z - a.Z;
        double lengthSquared = (sx * sx) + (sz * sz);
        for (int row = row0; row <= row1; row++)
        {
            double z = -half + (row * (double)spacing);
            for (int col = col0; col <= col1; col++)
            {
                double x = -half + (col * (double)spacing);
                double t = lengthSquared <= 0 ? 0 : Math.Clamp((((x - a.X) * sx) + ((z - a.Z) * sz)) / lengthSquared, 0.0, 1.0);
                double px = a.X + (sx * t);
                double pz = a.Z + (sz * t);
                double distance = Math.Sqrt(((x - px) * (x - px)) + ((z - pz) * (z - pz)));
                if (distance > reach)
                {
                    continue;
                }

                double flow = a.FlowKm2 + ((b.FlowKm2 - a.FlowKm2) * t);
                double ground = Bilinear(reference, side, spacing, half, px, pz);
                double depth = Depth(depthA + ((depthB - depthA) * t), flow, ground, settings);
                if (depth < MinimumDepthMeters)
                {
                    continue;
                }

                double floor = ground - depth;
                double candidate = floor + (settings.GorgeWallSlope * Math.Max(0, distance - HalfWidth(flow, settings)));
                int index = (row * side) + col;
                if (candidate < heights[index])
                {
                    heights[index] = (float)candidate;
                    lowered[index] = true;
                }
            }
        }
    }

    private static double Bilinear(float[] heights, int side, int spacing, double half, double x, double z)
    {
        double gx = Math.Clamp((x + half) / spacing, 0, side - 1.000001);
        double gz = Math.Clamp((z + half) / spacing, 0, side - 1.000001);
        int x0 = (int)gx;
        int z0 = (int)gz;
        double tx = gx - x0;
        double tz = gz - z0;
        double top = heights[(z0 * side) + x0] + (tx * (heights[(z0 * side) + x0 + 1] - heights[(z0 * side) + x0]));
        double bottom = heights[((z0 + 1) * side) + x0] + (tx * (heights[((z0 + 1) * side) + x0 + 1] - heights[((z0 + 1) * side) + x0]));
        return top + (tz * (bottom - top));
    }
}
