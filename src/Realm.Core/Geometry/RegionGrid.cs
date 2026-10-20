using System;
using System.Collections.Generic;

namespace Realm.Core.Geometry;

/// <summary>
/// Maths for a square grid of equally sized regions on the ground plane: which regions lie within a distance of a
/// point. The streaming layer uses it to decide which terrain regions must be loaded (CLAUDE.md section 4.9).
/// </summary>
/// <remarks>
/// Region (0, 0) covers x in [0, extent) and z in [0, extent); region (-1, 0) covers x in [-extent, 0), and so on.
/// Determinism: pure functions; ties are ordered by region coordinates. Thread-safety: stateless.
/// </remarks>
public static class RegionGrid
{
    /// <summary>Distance in metres from a point to a region's square; 0 when the point is inside it.</summary>
    /// <param name="x">East coordinate of the point.</param>
    /// <param name="z">South coordinate of the point.</param>
    /// <param name="regionX">Region coordinate, east.</param>
    /// <param name="regionZ">Region coordinate, south.</param>
    /// <param name="extent">Width of a region in metres; positive.</param>
    /// <returns>The straight-line distance to the nearest point of the region.</returns>
    public static double DistanceToRegion(double x, double z, int regionX, int regionZ, double extent)
    {
        double dx = Math.Max(Math.Max((regionX * extent) - x, x - ((regionX + 1) * extent)), 0);
        double dz = Math.Max(Math.Max((regionZ * extent) - z, z - ((regionZ + 1) * extent)), 0);
        return Math.Sqrt((dx * dx) + (dz * dz));
    }

    /// <summary>The region coordinate containing a point.</summary>
    /// <param name="value">Coordinate in metres.</param>
    /// <param name="extent">Width of a region in metres; positive.</param>
    /// <returns>The region coordinate along that axis.</returns>
    public static int RegionOf(double value, double extent) => (int)Math.Floor(value / extent);

    /// <summary>Lists the regions within a distance of a point, nearest first.</summary>
    /// <param name="x">East coordinate of the point.</param>
    /// <param name="z">South coordinate of the point.</param>
    /// <param name="distance">Maximum distance in metres from the point to a region's square; not negative.</param>
    /// <param name="extent">Width of a region in metres; positive.</param>
    /// <param name="minCoordinate">Lowest valid region coordinate on each axis.</param>
    /// <param name="maxCoordinate">Highest valid region coordinate on each axis.</param>
    /// <returns>
    /// The valid regions whose squares come within <paramref name="distance"/> of the point, nearest first, ties by
    /// east then south coordinate. The region containing the point is always first when it is valid.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">Distance is negative or extent is not positive.</exception>
    public static List<(int X, int Z, double Distance)> RegionsWithin(
        double x, double z, double distance, double extent, int minCoordinate, int maxCoordinate)
    {
        if (distance < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(distance), "Distance must not be negative.");
        }

        if (!(extent > 0))
        {
            throw new ArgumentOutOfRangeException(nameof(extent), "Extent must be positive.");
        }

        int west = Math.Max(minCoordinate, RegionOf(x - distance, extent));
        int east = Math.Min(maxCoordinate, RegionOf(x + distance, extent));
        int north = Math.Max(minCoordinate, RegionOf(z - distance, extent));
        int south = Math.Min(maxCoordinate, RegionOf(z + distance, extent));

        var result = new List<(int X, int Z, double Distance)>();
        for (int rz = north; rz <= south; rz++)
        {
            for (int rx = west; rx <= east; rx++)
            {
                double d = DistanceToRegion(x, z, rx, rz, extent);
                if (d <= distance)
                {
                    result.Add((rx, rz, d));
                }
            }
        }

        result.Sort((a, b) =>
        {
            int byDistance = a.Distance.CompareTo(b.Distance);
            if (byDistance != 0)
            {
                return byDistance;
            }

            int byX = a.X.CompareTo(b.X);
            return byX != 0 ? byX : a.Z.CompareTo(b.Z);
        });
        return result;
    }
}
