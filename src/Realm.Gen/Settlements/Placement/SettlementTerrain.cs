using System;
using System.Collections.Generic;
using Realm.Gen.Terrain;

namespace Realm.Gen.Settlements.Placement;

/// <summary>Flattens the ground under settlements so streets and houses can stand on level land.</summary>
/// <remarks>Determinism: pure. Thread-safety: pure.</remarks>
public static class SettlementTerrain
{
    /// <summary>
    /// Inside each settlement's pad the ground is set to the settlement's ground height; beyond the pad it blends back to the
    /// natural terrain over <see cref="SettlementSettings.BlendMeters"/> with a smoothstep.
    /// </summary>
    /// <param name="field">The terrain.</param>
    /// <param name="settlements">The settlements (their pads must not overlap, which the placer guarantees).</param>
    /// <param name="settings">Blend settings; defaults when null.</param>
    /// <returns>A new height field; the input is not changed.</returns>
    public static HeightField Flatten(HeightField field, IReadOnlyList<Settlement> settlements, SettlementSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(settlements);
        settings ??= new SettlementSettings();

        float[] heights = field.Heights.ToArray();
        int side = field.VerticesPerSide;
        int spacing = field.SpacingMeters;
        double half = field.WorldSizeMeters / 2.0;
        foreach (Settlement s in settlements)
        {
            double reach = s.PadRadiusMeters + settings.BlendMeters;
            int col0 = Math.Max(0, (int)Math.Floor((s.X - reach + half) / spacing));
            int col1 = Math.Min(side - 1, (int)Math.Ceiling((s.X + reach + half) / spacing));
            int row0 = Math.Max(0, (int)Math.Floor((s.Z - reach + half) / spacing));
            int row1 = Math.Min(side - 1, (int)Math.Ceiling((s.Z + reach + half) / spacing));
            for (int row = row0; row <= row1; row++)
            {
                double z = -half + (row * (double)spacing);
                for (int col = col0; col <= col1; col++)
                {
                    double d = s.DistanceTo(-half + (col * (double)spacing), z);
                    double weight = Weight(d, s.PadRadiusMeters, settings.BlendMeters);
                    if (weight > 0)
                    {
                        int i = (row * side) + col;
                        heights[i] = (float)(heights[i] + (weight * (s.GroundHeightMeters - heights[i])));
                    }
                }
            }
        }

        return new HeightField(field.WorldSizeMeters, field.SpacingMeters, heights);
    }

    /// <summary>How strongly a vertex at a distance from the centre is pulled to the flat ground level.</summary>
    /// <param name="distance">Distance from the settlement centre in metres.</param>
    /// <param name="padRadius">Radius of the flat pad.</param>
    /// <param name="blend">Width of the blend zone beyond the pad.</param>
    /// <returns>1 inside the pad, falling smoothly to 0 at <c>padRadius + blend</c>.</returns>
    public static double Weight(double distance, double padRadius, double blend)
    {
        if (distance <= padRadius)
        {
            return 1;
        }

        if (distance >= padRadius + blend)
        {
            return 0;
        }

        double t = (distance - padRadius) / blend;
        return 1 - (t * t * (3 - (2 * t)));
    }
}
