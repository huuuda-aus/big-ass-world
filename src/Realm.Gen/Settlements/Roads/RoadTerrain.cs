using System;
using System.Collections.Generic;
using Realm.Gen.Terrain;

namespace Realm.Gen.Settlements.Roads;

/// <summary>Cuts and fills the terrain under roads so the surface follows the graded profile.</summary>
/// <remarks>
/// Determinism: each road is applied in list order; within a road every vertex is moved once, toward the profile height of its closest
/// segment. Bridge spans are left alone (the river channel stays). Thread-safety: pure.
/// </remarks>
public static class RoadTerrain
{
    /// <summary>Moves terrain under each road to the road surface, blending into the terrain over the shoulder.</summary>
    /// <param name="field">The terrain.</param>
    /// <param name="roads">The roads.</param>
    /// <param name="settings">Width and shoulder; defaults when null.</param>
    /// <returns>A new height field; the input is not changed.</returns>
    public static HeightField Carve(HeightField field, IReadOnlyList<Road> roads, RoadSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(roads);
        settings ??= new RoadSettings();

        float[] heights = field.Heights.ToArray();
        int side = field.VerticesPerSide;
        int spacing = field.SpacingMeters;
        double half = field.WorldSizeMeters / 2.0;
        double halfWidth = settings.WidthMeters / 2;
        double reach = halfWidth + settings.ShoulderMeters;

        foreach (Road road in roads)
        {
            var closest = new Dictionary<int, (double Distance, double Height)>();
            for (int i = 0; i + 1 < road.Points.Count; i++)
            {
                if (InsideBridge(road, i))
                {
                    continue;
                }

                RoadPoint a = road.Points[i];
                RoadPoint b = road.Points[i + 1];
                int col0 = Math.Max(0, (int)Math.Floor((Math.Min(a.X, b.X) - reach + half) / spacing));
                int col1 = Math.Min(side - 1, (int)Math.Ceiling((Math.Max(a.X, b.X) + reach + half) / spacing));
                int row0 = Math.Max(0, (int)Math.Floor((Math.Min(a.Z, b.Z) - reach + half) / spacing));
                int row1 = Math.Min(side - 1, (int)Math.Ceiling((Math.Max(a.Z, b.Z) + reach + half) / spacing));
                double dx = b.X - a.X;
                double dz = b.Z - a.Z;
                double lengthSquared = (dx * dx) + (dz * dz);
                for (int row = row0; row <= row1; row++)
                {
                    double z = -half + (row * (double)spacing);
                    for (int col = col0; col <= col1; col++)
                    {
                        double x = -half + (col * (double)spacing);
                        double t = lengthSquared <= 0 ? 0 : Math.Clamp((((x - a.X) * dx) + ((z - a.Z) * dz)) / lengthSquared, 0, 1);
                        double distance = Math.Sqrt(((x - (a.X + (dx * t))) * (x - (a.X + (dx * t)))) + ((z - (a.Z + (dz * t))) * (z - (a.Z + (dz * t)))));
                        if (distance > reach)
                        {
                            continue;
                        }

                        int index = (row * side) + col;
                        double surface = a.Height + ((b.Height - a.Height) * t);
                        if (!closest.TryGetValue(index, out (double Distance, double Height) best) || distance < best.Distance)
                        {
                            closest[index] = (distance, surface);
                        }
                    }
                }
            }

            foreach ((int index, (double distance, double surface)) in closest)
            {
                double weight = Weight(distance, halfWidth, settings.ShoulderMeters);
                heights[index] = (float)(heights[index] + (weight * (surface - heights[index])));
            }
        }

        return new HeightField(field.WorldSizeMeters, field.SpacingMeters, heights);
    }

    /// <summary>How strongly a vertex at a distance from the road centre line is pulled to the road surface.</summary>
    /// <param name="distance">Distance from the centre line in metres.</param>
    /// <param name="halfWidth">Half the width of the road surface.</param>
    /// <param name="shoulder">Width of the blend beyond the road surface.</param>
    /// <returns>1 on the road surface, falling smoothly to 0 at <c>halfWidth + shoulder</c>.</returns>
    public static double Weight(double distance, double halfWidth, double shoulder)
    {
        if (distance <= halfWidth)
        {
            return 1;
        }

        if (shoulder <= 0 || distance >= halfWidth + shoulder)
        {
            return 0;
        }

        double t = (distance - halfWidth) / shoulder;
        return 1 - (t * t * (3 - (2 * t)));
    }

    // A segment lies on a bridge when both of its points are within the same bridge span.
    internal static bool InsideBridge(Road road, int segment)
    {
        foreach (RoadBridge bridge in road.Bridges)
        {
            if (segment >= bridge.FirstPoint && segment + 1 <= bridge.LastPoint)
            {
                return true;
            }
        }

        return false;
    }
}
