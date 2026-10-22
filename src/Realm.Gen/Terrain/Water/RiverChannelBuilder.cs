using System;
using System.Collections.Generic;
using Realm.Gen.Terrain.Erosion;

namespace Realm.Gen.Terrain.Water;

/// <summary>
/// Gives every river a water level that falls steadily from its source to its mouth, and cuts a channel below that level so the
/// water sits in a bed instead of floating on a slope (the rivers used to be colour painted on whatever ground was there).
/// </summary>
/// <remarks>
/// Order: sample the terrain beside the river to find the highest level the banks can hold, smooth it, let it only fall along
/// the flow, tie the end to the sea, the lake or the river it joins (no cascade steeper than <see cref="MaxDescent"/>), then
/// cut the bed. Lakes are flat at their own level and their cells are cut below it.
/// Determinism: pure; every vertex takes the minimum over all segments, so the result does not depend on processing order.
/// Thread-safety: not safe to call concurrently on the same array.
/// </remarks>
internal static class RiverChannelBuilder
{
    /// <summary>Steepest fall of the water surface between two points, metres per metre; steeper drops are spread upstream.</summary>
    public const double MaxDescent = 1.0;

    private const int SmoothingPasses = 2;
    private const int LevelWindow = 2;
    private const double FreeboardMeters = 0.35;
    private const double BankProbeMeters = 4;
    private const double MinimumDepthMeters = 1.2;
    private const double DepthPerHalfWidth = 0.12;
    private const double BankSlope = 0.6;
    private const double BankRunMeters = 12;
    private const double MaxBankRunMeters = 90;
    private const double LakeDepthMeters = 1.5;
    private const double SeaSurface = 0;

    /// <summary>Computes the water profile of every river on the given terrain.</summary>
    /// <param name="terrain">The terrain beside the rivers; read only.</param>
    /// <param name="rivers">The rivers, parents before the rivers that join them.</param>
    /// <param name="lakes">The lakes.</param>
    /// <param name="workingSpacingMeters">Spacing of the grid the lake cells sit on.</param>
    /// <returns>One channel per river, in the same order.</returns>
    public static IReadOnlyList<RiverChannel> Profile(HeightField terrain, IReadOnlyList<River> rivers, IReadOnlyList<Lake> lakes, int workingSpacingMeters)
    {
        double spacing = Math.Max(1, workingSpacingMeters);
        var lakeLevel = new Dictionary<(int, int), double>();
        foreach (Lake lake in lakes)
        {
            foreach ((double x, double z) in lake.Cells)
            {
                lakeLevel[((int)Math.Round(x / spacing), (int)Math.Round(z / spacing))] = lake.SurfaceHeight;
            }
        }

        var channels = new List<RiverChannel>(rivers.Count);
        foreach (River river in rivers)
        {
            channels.Add(ProfileOne(terrain, river, lakeLevel, spacing, channels));
        }

        return channels;
    }

    /// <summary>Cuts the beds of the rivers and the floors of the lakes into the heights.</summary>
    /// <param name="heights">Heights row by row, <paramref name="side"/> x <paramref name="side"/>; modified in place.</param>
    /// <param name="side">Vertices per side.</param>
    /// <param name="spacingMeters">Metres between vertices.</param>
    /// <param name="worldSizeMeters">World side length in metres.</param>
    /// <param name="channels">River profiles from <see cref="Profile"/>.</param>
    /// <param name="lakes">The lakes.</param>
    /// <param name="workingSpacingMeters">Spacing of the lake cells.</param>
    public static void Carve(float[] heights, int side, int spacingMeters, int worldSizeMeters, IReadOnlyList<RiverChannel> channels, IReadOnlyList<Lake> lakes, int workingSpacingMeters)
    {
        double half = worldSizeMeters / 2.0;
        var shape = new Dictionary<int, Shape>();
        foreach (RiverChannel channel in channels)
        {
            for (int i = 0; i + 1 < channel.Points.Count; i++)
            {
                ChannelPoint a = channel.Points[i];
                ChannelPoint b = channel.Points[i + 1];
                if (!(a.InLake && b.InLake))
                {
                    CarveSegment(heights, side, spacingMeters, half, a, b, shape);
                }
            }
        }

        foreach ((int index, Shape s) in shape)
        {
            // Sea floor is only ever cut, never built up: a shelf at the river mouth would stand as a cliff over the open sea.
            heights[index] = heights[index] < SeaSurface ? (float)Math.Min(heights[index], s.Highest) : (float)Math.Clamp(heights[index], s.Lowest, s.Highest);
        }

        double halfCell = workingSpacingMeters / 2.0;
        foreach (Lake lake in lakes)
        {
            float floor = (float)(lake.SurfaceHeight - LakeDepthMeters);
            foreach ((double cx, double cz) in lake.Cells)
            {
                int col0 = Math.Max(0, (int)Math.Ceiling((cx - halfCell + half) / spacingMeters));
                int col1 = Math.Min(side - 1, (int)Math.Floor((cx + halfCell + half) / spacingMeters));
                int row0 = Math.Max(0, (int)Math.Ceiling((cz - halfCell + half) / spacingMeters));
                int row1 = Math.Min(side - 1, (int)Math.Floor((cz + halfCell + half) / spacingMeters));
                for (int row = row0; row <= row1; row++)
                {
                    for (int col = col0; col <= col1; col++)
                    {
                        int i = (row * side) + col;
                        heights[i] = Math.Min(heights[i], floor);
                    }
                }
            }
        }
    }

    private static RiverChannel ProfileOne(HeightField terrain, River river, Dictionary<(int, int), double> lakeLevel, double spacing, List<RiverChannel> done)
    {
        List<ChannelPoint> points = Smooth(river.Points);
        int n = points.Count;
        var inLake = new bool[n];
        var level = new double[n];
        for (int i = 0; i < n; i++)
        {
            inLake[i] = lakeLevel.TryGetValue(((int)Math.Round(points[i].X / spacing), (int)Math.Round(points[i].Z / spacing)), out level[i]);
        }

        // The highest level the banks can hold at each point.
        var target = new double[n];
        for (int i = 0; i < n; i++)
        {
            target[i] = inLake[i] ? level[i] : BankLevel(terrain, points, i) - FreeboardMeters;
        }

        // Smooth, but never above the local limit, then let the water only fall along the flow.
        var surface = new double[n];
        for (int i = 0; i < n; i++)
        {
            double sum = 0;
            int count = 0;
            for (int k = Math.Max(0, i - LevelWindow); k <= Math.Min(n - 1, i + LevelWindow); k++)
            {
                sum += target[k];
                count++;
            }

            surface[i] = inLake[i] ? level[i] : Math.Min(target[i], sum / count);
        }

        for (int i = 1; i < n; i++)
        {
            surface[i] = Math.Min(surface[i], surface[i - 1]);
            if (inLake[i])
            {
                surface[i] = level[i];
            }
        }

        double? end = river.Mouth switch
        {
            RiverMouth.Sea => SeaSurface,
            RiverMouth.Confluence when river.JoinsRiver is int parent && parent < done.Count => SurfaceNear(done[parent], points[n - 1]),
            _ => null,
        };
        if (end is double endLevel)
        {
            surface[n - 1] = endLevel;
        }

        // From the mouth upstream: no rise, and no waterfall steeper than MaxDescent.
        for (int i = n - 2; i >= 0; i--)
        {
            double step = Distance(points[i], points[i + 1]);
            surface[i] = Math.Clamp(surface[i], surface[i + 1], surface[i + 1] + (MaxDescent * step));
        }

        var result = new List<ChannelPoint>(n);
        double along = 0;
        for (int i = 0; i < n; i++)
        {
            if (i > 0)
            {
                along += Distance(points[i - 1], points[i]);
            }

            ChannelPoint p = points[i];
            result.Add(p with { Surface = surface[i], DistanceMeters = along, InLake = inLake[i] });
        }

        return new RiverChannel(river.Index, result);
    }

    // The average of the ground at the centre and at both sides just outside the water: on a slope the bed is cut into the high
    // side and the low side is built up, so neither the trench nor the embankment gets deep.
    private static double BankLevel(HeightField terrain, List<ChannelPoint> points, int i)
    {
        ChannelPoint p = points[i];
        ChannelPoint before = points[Math.Max(0, i - 1)];
        ChannelPoint after = points[Math.Min(points.Count - 1, i + 1)];
        double dx = after.X - before.X;
        double dz = after.Z - before.Z;
        double length = Math.Sqrt((dx * dx) + (dz * dz));
        double centre = Sample(terrain, p.X, p.Z);
        if (length <= 1e-6)
        {
            return centre;
        }

        double offset = p.HalfWidth + BankProbeMeters;
        double nx = -dz / length * offset;
        double nz = dx / length * offset;
        return (centre + Sample(terrain, p.X + nx, p.Z + nz) + Sample(terrain, p.X - nx, p.Z - nz)) / 3.0;
    }

    private static double Sample(HeightField terrain, double x, double z)
    {
        double half = terrain.WorldSizeMeters / 2.0;
        return terrain.Sample(Math.Clamp(x, -half, half), Math.Clamp(z, -half, half));
    }

    private static double SurfaceNear(RiverChannel parent, ChannelPoint at)
    {
        double best = double.MaxValue;
        double surface = SeaSurface;
        foreach (ChannelPoint p in parent.Points)
        {
            double d = Distance(p, at);
            if (d < best)
            {
                best = d;
                surface = p.Surface;
            }
        }

        return surface;
    }

    private static double Distance(ChannelPoint a, ChannelPoint b) => Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Z - b.Z) * (a.Z - b.Z)));

    // Chaikin corner cutting rounds the stair-steps of the routing grid; width and flow are interpolated alongside.
    private static List<ChannelPoint> Smooth(IReadOnlyList<RiverPoint> source)
    {
        var current = new List<ChannelPoint>(source.Count);
        foreach (RiverPoint p in source)
        {
            current.Add(new ChannelPoint(p.X, p.Z, p.Height, p.HalfWidth, p.FlowKm2, 0, false));
        }

        for (int pass = 0; pass < SmoothingPasses && current.Count > 2; pass++)
        {
            var next = new List<ChannelPoint>(current.Count * 2) { current[0] };
            for (int i = 0; i + 1 < current.Count; i++)
            {
                next.Add(Lerp(current[i], current[i + 1], 0.25));
                next.Add(Lerp(current[i], current[i + 1], 0.75));
            }

            next.Add(current[^1]);
            current = next;
        }

        return current;
    }

    private static ChannelPoint Lerp(ChannelPoint a, ChannelPoint b, double t) =>
        new(a.X + ((b.X - a.X) * t), a.Z + ((b.Z - a.Z) * t), a.Surface + ((b.Surface - a.Surface) * t), a.HalfWidth + ((b.HalfWidth - a.HalfWidth) * t), a.FlowKm2 + ((b.FlowKm2 - a.FlowKm2) * t), 0, false);

    // The band of heights a vertex may have so the water stands in a bed with a bank beside it.
    private readonly record struct Shape(double Distance, double Lowest, double Highest);

    private static void CarveSegment(float[] heights, int side, int spacing, double half, ChannelPoint a, ChannelPoint b, Dictionary<int, Shape> shape)
    {
        // The bank climbs or falls at BankSlope until it meets the ground, so a deep cut or a high fill needs a long run.
        double maxHalfWidth = Math.Max(a.HalfWidth, b.HalfWidth);
        double surfaceHere = (a.Surface + b.Surface) / 2;
        double run = BankRunMeters;
        for (int pass = 0; pass < 2; pass++)
        {
            double probe = maxHalfWidth + run;
            double gap = 0;
            int c0 = Math.Max(0, (int)Math.Floor((Math.Min(a.X, b.X) - probe + half) / spacing));
            int c1 = Math.Min(side - 1, (int)Math.Ceiling((Math.Max(a.X, b.X) + probe + half) / spacing));
            int r0 = Math.Max(0, (int)Math.Floor((Math.Min(a.Z, b.Z) - probe + half) / spacing));
            int r1 = Math.Min(side - 1, (int)Math.Ceiling((Math.Max(a.Z, b.Z) + probe + half) / spacing));
            for (int row = r0; row <= r1; row++)
            {
                for (int col = c0; col <= c1; col++)
                {
                    gap = Math.Max(gap, Math.Abs(heights[(row * side) + col] - surfaceHere));
                }
            }

            run = Math.Clamp(gap / BankSlope, BankRunMeters, MaxBankRunMeters);
        }

        double reach = maxHalfWidth + run;
        int col0 = Math.Max(0, (int)Math.Floor((Math.Min(a.X, b.X) - reach + half) / spacing));
        int col1 = Math.Min(side - 1, (int)Math.Ceiling((Math.Max(a.X, b.X) + reach + half) / spacing));
        int row0 = Math.Max(0, (int)Math.Floor((Math.Min(a.Z, b.Z) - reach + half) / spacing));
        int row1 = Math.Min(side - 1, (int)Math.Ceiling((Math.Max(a.Z, b.Z) + reach + half) / spacing));
        double sx = b.X - a.X;
        double sz = b.Z - a.Z;
        double lengthSquared = (sx * sx) + (sz * sz);
        for (int row = row0; row <= row1; row++)
        {
            for (int col = col0; col <= col1; col++)
            {
                double x = -half + (col * (double)spacing);
                double z = -half + (row * (double)spacing);
                double t = lengthSquared < 1e-9 ? 0 : Math.Clamp((((x - a.X) * sx) + ((z - a.Z) * sz)) / lengthSquared, 0, 1);
                double dx = x - (a.X + (sx * t));
                double dz = z - (a.Z + (sz * t));
                double d = Math.Sqrt((dx * dx) + (dz * dz));
                double hw = a.HalfWidth + ((b.HalfWidth - a.HalfWidth) * t);
                if (d > hw + run)
                {
                    continue;
                }

                double surface = a.Surface + ((b.Surface - a.Surface) * t);
                double lowest;
                double highest;
                if (d <= hw)
                {
                    double depth = Math.Max(MinimumDepthMeters, DepthPerHalfWidth * hw);
                    double u = d / hw;
                    lowest = highest = surface - (depth * (1 - (u * u)));
                }
                else
                {
                    // At the water's edge the bank stands FreeboardMeters above the water; it may rise or fall from there at BankSlope.
                    double away = (d - hw) * BankSlope;
                    highest = surface + FreeboardMeters + away;
                    lowest = surface + FreeboardMeters - away;
                }

                int i = (row * side) + col;
                if (!shape.TryGetValue(i, out Shape existing) || d < existing.Distance)
                {
                    shape[i] = new Shape(d, lowest, highest);
                }
            }
        }
    }
}
