namespace Realm.Gen.Terrain.Erosion;

/// <summary>
/// Tuning knobs of the erosion and river step. The defaults are look-tuning placeholders for the developer
/// to adjust once the result can be seen; they are not final design.
/// </summary>
/// <remarks>Thread-safety: immutable.</remarks>
public sealed record ErosionSettings
{
    /// <summary>Default spacing of the coarse grid that hydraulic erosion and river routing run on.</summary>
    public const int DefaultWorkingSpacingMeters = 32;

    /// <summary>Default number of hydraulic erosion iterations.</summary>
    public const int DefaultIterations = 20;

    /// <summary>Default overall erosion strength per iteration (placeholder).</summary>
    public const double DefaultStrength = 2.0;

    /// <summary>Default number of thermal relaxation passes on the full-resolution field.</summary>
    public const int DefaultThermalIterations = 20;

    /// <summary>Default minimum flow (effective km2 of catchment) for a vertex to count as river.</summary>
    public const double DefaultRiverThresholdKm2 = 2.0;

    /// <summary>Default minimum depth in metres for a filled depression to count as a lake.</summary>
    public const double DefaultLakeMinDepthMeters = 2.0;

    /// <summary>Default minimum surface area in km2 for a lake to be kept; smaller basins are filled in.</summary>
    public const double DefaultMinLakeAreaKm2 = 4.0;

    /// <summary>Spacing of the coarse working grid in metres; must be a multiple of the height-field spacing and divide the world size.</summary>
    public int WorkingSpacingMeters { get; init; } = DefaultWorkingSpacingMeters;

    /// <summary>Number of hydraulic erosion iterations, 0 to 1000; 0 skips hydraulic erosion.</summary>
    public int Iterations { get; init; } = DefaultIterations;

    /// <summary>Overall multiplier of the stream-power erosion per iteration; not negative.</summary>
    public double Strength { get; init; } = DefaultStrength;

    /// <summary>How far biome erosion properties blend to each side of a region border, in metres.</summary>
    public int BlendRadiusMeters { get; init; } = HeightFieldSettings.DefaultBlendRadiusMeters;

    /// <summary>Number of thermal relaxation passes, 0 to 10000; 0 skips thermal relaxation.</summary>
    public int ThermalIterations { get; init; } = DefaultThermalIterations;

    /// <summary>Flow in effective km2 at which a vertex becomes part of a river; positive.</summary>
    public double RiverThresholdKm2 { get; init; } = DefaultRiverThresholdKm2;

    /// <summary>Minimum fill depth in metres for a basin to count as a lake; positive (0 would make every vertex a lake).</summary>
    public double LakeMinDepthMeters { get; init; } = DefaultLakeMinDepthMeters;

    /// <summary>
    /// Basins with a smaller surface area are filled in (as sediment) instead of kept as lakes, so rivers cross
    /// them; not negative. 0 keeps every basin at least <see cref="LakeMinDepthMeters"/> deep.
    /// </summary>
    public double MinLakeAreaKm2 { get; init; } = DefaultMinLakeAreaKm2;

    /// <summary>Checks the settings against each other, the world and the height-field spacing.</summary>
    /// <param name="worldSizeMeters">World side length in metres.</param>
    /// <param name="fieldSpacingMeters">Vertex spacing of the height field.</param>
    /// <param name="problem">The first problem found; otherwise null.</param>
    /// <returns><c>true</c> when valid.</returns>
    public bool TryValidate(int worldSizeMeters, int fieldSpacingMeters, out string? problem)
    {
        if (WorkingSpacingMeters <= 0 || worldSizeMeters % WorkingSpacingMeters != 0 || WorkingSpacingMeters % fieldSpacingMeters != 0)
        {
            problem = $"WorkingSpacingMeters {WorkingSpacingMeters} must be positive, divide the world size {worldSizeMeters} " +
                      $"and be a multiple of the height-field spacing {fieldSpacingMeters}.";
        }
        else if (Iterations < 0 || Iterations > 1000)
        {
            problem = "Iterations must be between 0 and 1000.";
        }
        else if (!(Strength >= 0) || !double.IsFinite(Strength))
        {
            problem = "Strength must be a number that is not negative.";
        }
        else if (BlendRadiusMeters < 0)
        {
            problem = "BlendRadiusMeters must not be negative.";
        }
        else if (ThermalIterations < 0 || ThermalIterations > 10_000)
        {
            problem = "ThermalIterations must be between 0 and 10000.";
        }
        else if (!(RiverThresholdKm2 > 0) || !double.IsFinite(RiverThresholdKm2))
        {
            problem = "RiverThresholdKm2 must be a positive number.";
        }
        else if (!(LakeMinDepthMeters > 0) || !double.IsFinite(LakeMinDepthMeters))
        {
            problem = "LakeMinDepthMeters must be a positive number.";
        }
        else if (!(MinLakeAreaKm2 >= 0) || !double.IsFinite(MinLakeAreaKm2))
        {
            problem = "MinLakeAreaKm2 must be a number that is not negative.";
        }
        else
        {
            problem = null;
            return true;
        }

        return false;
    }
}
