using System;
using Realm.Gen.Terrain;

namespace Realm.Gen.Bake;

/// <summary>
/// Applies one hand-edit brush dab to a baked <see cref="RegionTile"/> (CLAUDE.md section 4.4: generate, bake, edit).
/// Pure and Godot-free so it is unit-testable; the Godot-side terrain editor only handles the viewport, the brush
/// cursor and writing the result back to disk.
/// </summary>
/// <remarks>
/// Determinism: a pure function of the tile and the stroke. Thread-safety: stateless; never mutates the input tile's
/// arrays (<see cref="RegionTile"/> documents them as immutable once built).
/// Colour: reshaded the same way as <see cref="Bake.TerrainBaker"/> (<see cref="HillShading"/>), but using only this
/// region's own vertices for neighbour slopes, so shading can mismatch by up to one vertex at a region border after
/// an edit near it (known limitation; the border itself needs no lock to still look continuous in practice, since
/// slopes rarely change sign there). A vertex already painted as water (<see cref="TerrainColouring.WaterPacked"/>)
/// is never reshaded, matching the baker, which paints water without shading it.
/// </remarks>
public static class TerrainTileEditor
{
    /// <summary>Applies one brush dab.</summary>
    /// <param name="tile">The tile before the dab.</param>
    /// <param name="stroke">Where and how the brush pushes.</param>
    /// <param name="paletteTop">Height mapped to the top of the colour palette (usually the world's highest point, from the manifest).</param>
    /// <param name="paletteDepth">Depth mapped to the deepest water colour (usually minus the world's lowest point, from the manifest).</param>
    /// <returns>A new tile with the dab applied; <paramref name="tile"/> is not modified.</returns>
    /// <exception cref="ArgumentException"><see cref="BrushStroke.RadiusVertices"/> is not positive.</exception>
    public static RegionTile ApplyBrush(RegionTile tile, BrushStroke stroke, double paletteTop, double paletteDepth)
    {
        ArgumentNullException.ThrowIfNull(tile);
        ArgumentNullException.ThrowIfNull(stroke);
        stroke.Validate();

        int size = tile.Size;
        var heights = (float[])tile.Heights.Clone();
        var colours = (uint[])tile.Colours.Clone();

        int xMin = Math.Max(0, (int)Math.Floor(stroke.CenterX - stroke.RadiusVertices) - 1);
        int xMax = Math.Min(size - 1, (int)Math.Ceiling(stroke.CenterX + stroke.RadiusVertices) + 1);
        int zMin = Math.Max(0, (int)Math.Floor(stroke.CenterZ - stroke.RadiusVertices) - 1);
        int zMax = Math.Min(size - 1, (int)Math.Ceiling(stroke.CenterZ + stroke.RadiusVertices) + 1);
        double rate = Math.Clamp(stroke.Strength, 0, 1);

        for (int iz = zMin; iz <= zMax; iz++)
        {
            for (int ix = xMin; ix <= xMax; ix++)
            {
                double dx = ix - stroke.CenterX;
                double dz = iz - stroke.CenterZ;
                double distance = Math.Sqrt((dx * dx) + (dz * dz));
                if (distance > stroke.RadiusVertices)
                {
                    continue;
                }

                double weight = SmoothFalloff(distance / stroke.RadiusVertices);
                int i = (iz * size) + ix;
                heights[i] = stroke.Mode switch
                {
                    BrushMode.Raise => heights[i] + (float)(stroke.Strength * weight),
                    BrushMode.Lower => heights[i] - (float)(stroke.Strength * weight),
                    BrushMode.Flatten => heights[i] + (float)((stroke.FlattenHeight - heights[i]) * rate * weight),
                    BrushMode.Smooth => heights[i] + (float)((NeighbourAverage(tile.Heights, size, ix, iz) - heights[i]) * rate * weight),
                    _ => throw new ArgumentOutOfRangeException(nameof(stroke), stroke.Mode, "Unknown brush mode."),
                };
            }
        }

        for (int iz = zMin; iz <= zMax; iz++)
        {
            for (int ix = xMin; ix <= xMax; ix++)
            {
                int i = (iz * size) + ix;
                if (tile.Colours[i] == TerrainColouring.WaterPacked)
                {
                    continue;
                }

                (double slopeX, double slopeZ) = RegionSlopes(heights, size, ix, iz, tile.SpacingMeters);
                (double r, double g, double b) = TerrainColouring.Albedo(
                    heights[i], paletteTop, paletteDepth, Math.Sqrt((slopeX * slopeX) + (slopeZ * slopeZ)));
                double shade = HillShading.ShadeFactor(slopeX, slopeZ);
                colours[i] = TerrainColouring.Pack(r * shade, g * shade, b * shade);
            }
        }

        return new RegionTile(tile.RegionX, tile.RegionZ, size, tile.SpacingMeters, heights, colours);
    }

    // Smoothstep: 1 at the centre, 0 at the edge, so adjoining dabs blend instead of showing a hard rim.
    private static double SmoothFalloff(double t)
    {
        double clamped = Math.Clamp(1 - t, 0, 1);
        return clamped * clamped * (3 - (2 * clamped));
    }

    private static float NeighbourAverage(float[] heights, int size, int ix, int iz)
    {
        float sum = 0;
        int count = 0;
        for (int dz = -1; dz <= 1; dz++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = Math.Clamp(ix + dx, 0, size - 1);
                int nz = Math.Clamp(iz + dz, 0, size - 1);
                sum += heights[(nz * size) + nx];
                count++;
            }
        }

        return sum / count;
    }

    // Slope within this region only (clamped at the region's own edges), unlike TerrainBaker.Slopes which reads the
    // whole field. A tile's border vertices therefore shade using a duplicated edge instead of the true neighbour
    // in the next region (see the remarks on this type).
    private static (double X, double Z) RegionSlopes(float[] heights, int size, int ix, int iz, float spacing)
    {
        int last = size - 1;
        double slopeX = (heights[(iz * size) + Math.Min(ix + 1, last)] - heights[(iz * size) + Math.Max(ix - 1, 0)])
                        / (2.0 * spacing);
        double slopeZ = (heights[(Math.Min(iz + 1, last) * size) + ix] - heights[(Math.Max(iz - 1, 0) * size) + ix])
                        / (2.0 * spacing);
        return (slopeX, slopeZ);
    }
}
