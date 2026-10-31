using System;

namespace Realm.Gen.Terrain;

/// <summary>
/// The hill-shading factor baked into terrain colour maps: lit from the north-west, so a surface rising toward the
/// south-east brightens and one falling toward the south-east darkens (ADR 0010). Shared by <c>Realm.Gen.Bake.TerrainBaker</c>
/// (shades the whole field) and the terrain editor (reshades one edited region) so both agree on the same look.
/// </summary>
/// <remarks>Determinism: a pure function of the two slopes. Thread-safety: stateless.</remarks>
public static class HillShading
{
    /// <summary>How strongly the slope toward the light brightens (and away darkens) the colour.</summary>
    public const double Strength = 0.9;

    /// <summary>Darkest multiplier a slope away from the light can reach.</summary>
    public const double Minimum = 0.45;

    /// <summary>Brightest multiplier a slope toward the light can reach.</summary>
    public const double Maximum = 1.35;

    /// <summary>The multiplier to apply to a vertex's unshaded colour.</summary>
    /// <param name="slopeX">Ground slope in X, rise over run.</param>
    /// <param name="slopeZ">Ground slope in Z, rise over run.</param>
    /// <returns>A value between <see cref="Minimum"/> and <see cref="Maximum"/>.</returns>
    public static double ShadeFactor(double slopeX, double slopeZ) =>
        Math.Clamp(1.0 + (Strength * (slopeX + slopeZ)), Minimum, Maximum);
}
