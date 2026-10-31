using System;
using System.Numerics;

namespace Realm.Gen.Bake;

/// <summary>
/// Finds where a ray meets a <see cref="RegionTile"/>'s own height grid, so the terrain editor can turn a mouse
/// click into a brush centre without depending on Godot physics (CLAUDE.md section 4.1: the Godot-free pieces carry
/// the logic; the presentation layer only supplies the ray).
/// </summary>
/// <remarks>
/// Coordinates are local to the tile: X east and Z south in metres from the tile's north-west vertex (0, 0), Y up.
/// Only this tile's own vertices are considered; a ray that leaves the region before it would have hit the ground
/// misses, even if the real terrain continues into a neighbouring region. Determinism: a pure function of the tile
/// and the ray. Thread-safety: stateless.
/// </remarks>
public static class RegionTileRaycast
{
    private const int MaxSteps = 4096;
    private const int BisectionSteps = 16;

    /// <summary>Marches a ray through the tile's height grid and finds the first point where it meets the ground.</summary>
    /// <param name="tile">The tile.</param>
    /// <param name="origin">Ray start, in tile-local metres.</param>
    /// <param name="direction">Ray direction; need not be normalised.</param>
    /// <param name="maxDistance">Longest distance along <paramref name="direction"/> to search, in multiples of its length.</param>
    /// <param name="hit">The point where the ray meets the ground, in tile-local metres, when found.</param>
    /// <returns><c>true</c> when the ray crosses the ground within the tile and within <paramref name="maxDistance"/>.</returns>
    public static bool TryIntersect(RegionTile tile, Vector3 origin, Vector3 direction, float maxDistance, out Vector3 hit)
    {
        ArgumentNullException.ThrowIfNull(tile);
        hit = default;
        if (direction == Vector3.Zero || maxDistance <= 0)
        {
            return false;
        }

        float boundsMax = (tile.Size - 1) * tile.SpacingMeters;
        // Coarse enough to run many times a frame, fine enough not to step over a thin ridge at this grid resolution.
        float step = tile.SpacingMeters * 0.5f;
        int steps = Math.Min(MaxSteps, (int)Math.Ceiling(maxDistance / step) + 1);

        bool havePrevious = false;
        float previousT = 0;
        float previousDiff = 0;
        for (int i = 0; i <= steps; i++)
        {
            float t = Math.Min(i * step, maxDistance);
            Vector3 point = origin + (direction * t);
            if (point.X < 0 || point.X > boundsMax || point.Z < 0 || point.Z > boundsMax)
            {
                havePrevious = false;
                continue;
            }

            float diff = SampleHeight(tile, point.X, point.Z) - point.Y; // > 0: ray is below ground here
            if (havePrevious && Math.Sign(diff) != Math.Sign(previousDiff))
            {
                hit = Bisect(tile, origin, direction, previousT, t);
                return true;
            }

            havePrevious = true;
            previousT = t;
            previousDiff = diff;
            if (t >= maxDistance)
            {
                break;
            }
        }

        return false;
    }

    private static Vector3 Bisect(RegionTile tile, Vector3 origin, Vector3 direction, float lowT, float highT)
    {
        float low = lowT;
        float high = highT;
        for (int i = 0; i < BisectionSteps; i++)
        {
            float mid = (low + high) / 2;
            Vector3 point = origin + (direction * mid);
            float diff = SampleHeight(tile, point.X, point.Z) - point.Y;
            if (diff > 0)
            {
                high = mid;
            }
            else
            {
                low = mid;
            }
        }

        float final = (low + high) / 2;
        Vector3 result = origin + (direction * final);
        result.Y = SampleHeight(tile, result.X, result.Z);
        return result;
    }

    /// <summary>Bilinearly samples the tile's height at a local metre position.</summary>
    /// <param name="tile">The tile.</param>
    /// <param name="localX">East metres from the tile's west edge; clamped into range.</param>
    /// <param name="localZ">South metres from the tile's north edge; clamped into range.</param>
    /// <returns>The interpolated height in metres.</returns>
    public static float SampleHeight(RegionTile tile, float localX, float localZ)
    {
        ArgumentNullException.ThrowIfNull(tile);
        int size = tile.Size;
        double vx = Math.Clamp(localX / tile.SpacingMeters, 0, size - 1);
        double vz = Math.Clamp(localZ / tile.SpacingMeters, 0, size - 1);
        int x0 = (int)vx;
        int z0 = (int)vz;
        int x1 = Math.Min(x0 + 1, size - 1);
        int z1 = Math.Min(z0 + 1, size - 1);
        double fx = vx - x0;
        double fz = vz - z0;

        float[] heights = tile.Heights;
        double h00 = heights[(z0 * size) + x0];
        double h10 = heights[(z0 * size) + x1];
        double h01 = heights[(z1 * size) + x0];
        double h11 = heights[(z1 * size) + x1];
        double top = h00 + ((h10 - h00) * fx);
        double bottom = h01 + ((h11 - h01) * fx);
        return (float)(top + ((bottom - top) * fz));
    }

    /// <summary>Converts a tile-local metre position to fractional vertex coordinates, as <see cref="BrushStroke"/> needs.</summary>
    /// <param name="tile">The tile.</param>
    /// <param name="localX">East metres from the tile's west edge.</param>
    /// <param name="localZ">South metres from the tile's north edge.</param>
    /// <returns>Vertex column and row.</returns>
    public static (double VertexX, double VertexZ) ToVertexCoordinates(RegionTile tile, float localX, float localZ) =>
        (localX / tile.SpacingMeters, localZ / tile.SpacingMeters);
}
