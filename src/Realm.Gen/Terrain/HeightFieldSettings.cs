using System;

namespace Realm.Gen.Terrain;

/// <summary>
/// Tuning knobs of the height-field step. The defaults are look-tuning placeholders to be adjusted by the
/// developer once the terrain can be seen; they are not final design.
/// </summary>
/// <remarks>Thread-safety: immutable.</remarks>
public sealed record HeightFieldSettings
{
    /// <summary>Default vertex spacing for development iteration (26 MB for a 20 km world). The final spacing is a proposal, not decided.</summary>
    public const int DefaultSpacingMeters = 8;

    /// <summary>Default distance over which biome parameters blend to each side of a region border.</summary>
    public const int DefaultBlendRadiusMeters = 384;

    /// <summary>Default number of noise octaves.</summary>
    public const int DefaultOctaves = 6;

    /// <summary>Default wavelength of the lowest-frequency noise octave in metres.</summary>
    public const double DefaultFeatureWavelengthMeters = 2048.0;

    /// <summary>Default frequency multiplier per octave.</summary>
    public const double DefaultLacunarity = 2.0;

    /// <summary>Default amplitude multiplier per octave at roughness 0 (fine detail fades fast).</summary>
    public const double DefaultMinGain = 0.35;

    /// <summary>Default amplitude multiplier per octave at roughness 1 (fine detail stays strong).</summary>
    public const double DefaultMaxGain = 0.60;

    /// <summary>Vertex spacing in metres; must divide the world size.</summary>
    public int SpacingMeters { get; init; } = DefaultSpacingMeters;

    /// <summary>
    /// How far biome parameters blend to each side of a region border, in metres, rounded up to whole macro
    /// cells; 0 gives hard steps at borders.
    /// </summary>
    public int BlendRadiusMeters { get; init; } = DefaultBlendRadiusMeters;

    /// <summary>Number of noise octaves, 1 to 12.</summary>
    public int Octaves { get; init; } = DefaultOctaves;

    /// <summary>Wavelength of the first octave in metres; positive.</summary>
    public double FeatureWavelengthMeters { get; init; } = DefaultFeatureWavelengthMeters;

    /// <summary>Frequency multiplier per octave; greater than 1.</summary>
    public double Lacunarity { get; init; } = DefaultLacunarity;

    /// <summary>Amplitude multiplier per octave at roughness 0; between 0 and <see cref="MaxGain"/>.</summary>
    public double MinGain { get; init; } = DefaultMinGain;

    /// <summary>Amplitude multiplier per octave at roughness 1; at most 1.</summary>
    public double MaxGain { get; init; } = DefaultMaxGain;

    /// <summary>Checks the settings against each other and a world size.</summary>
    /// <param name="worldSizeMeters">World side length in metres.</param>
    /// <param name="problem">The first problem found; otherwise null.</param>
    /// <returns><c>true</c> when valid.</returns>
    public bool TryValidate(int worldSizeMeters, out string? problem)
    {
        if (SpacingMeters <= 0 || worldSizeMeters % SpacingMeters != 0)
        {
            problem = $"SpacingMeters {SpacingMeters} must be positive and divide the world size {worldSizeMeters}.";
        }
        else if (BlendRadiusMeters < 0)
        {
            problem = "BlendRadiusMeters must not be negative.";
        }
        else if (Octaves < 1 || Octaves > 12)
        {
            problem = "Octaves must be between 1 and 12.";
        }
        else if (!(FeatureWavelengthMeters > 0) || !double.IsFinite(FeatureWavelengthMeters))
        {
            problem = "FeatureWavelengthMeters must be a positive number.";
        }
        else if (!(Lacunarity > 1) || !double.IsFinite(Lacunarity))
        {
            problem = "Lacunarity must be greater than 1.";
        }
        else if (!(MinGain >= 0 && MinGain <= MaxGain && MaxGain <= 1))
        {
            problem = "Gains must satisfy 0 <= MinGain <= MaxGain <= 1.";
        }
        else
        {
            problem = null;
            return true;
        }

        return false;
    }
}
