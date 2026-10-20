using System;
using Realm.Core.Validation;
using Realm.Data.Content;

namespace Realm.Gen.Terrain;

/// <summary>
/// What the height-field step reads from a <see cref="BiomeDefinition"/>. Vocabulary approved by the
/// developer on 2026-10-20: <c>base_height_m</c>, <c>relief_m</c>, <c>roughness</c> and (ocean only)
/// <c>water_depth_m</c>. Sea level is 0 m.
/// </summary>
/// <param name="BaseHeightMeters">Typical height above sea level; for ocean biomes this is minus the water depth.</param>
/// <param name="ReliefMeters">How far terrain rises above and falls below the base (half the peak-to-peak range).</param>
/// <param name="Roughness">0 for smooth rolling terrain to 1 for jagged; how slowly fine detail fades out.</param>
/// <param name="IsOcean">True for biomes tagged <c>ocean</c>.</param>
public sealed record BiomeTerrainParameters(double BaseHeightMeters, double ReliefMeters, double Roughness, bool IsOcean)
{
    /// <summary>Property name: typical height above sea level in metres (land biomes).</summary>
    public const string BaseHeightProperty = "base_height_m";

    /// <summary>Property name: height variation in metres (all biomes).</summary>
    public const string ReliefProperty = "relief_m";

    /// <summary>Property name: roughness from 0 to 1 (all biomes).</summary>
    public const string RoughnessProperty = "roughness";

    /// <summary>Property name: water depth below sea level in metres (ocean biomes).</summary>
    public const string WaterDepthProperty = "water_depth_m";

    /// <summary>The tag that marks a water biome.</summary>
    public const string OceanTag = "ocean";

    /// <summary>Reads and validates the terrain properties of a biome.</summary>
    /// <param name="biome">The biome.</param>
    /// <param name="report">Receives every problem found.</param>
    /// <returns>The parameters, or null when a required property is missing or out of range.</returns>
    /// <remarks>
    /// Land biomes need <c>base_height_m</c>, <c>relief_m</c> (not negative) and <c>roughness</c> (0 to 1).
    /// Ocean biomes need <c>water_depth_m</c> (positive), <c>relief_m</c> and <c>roughness</c>.
    /// A property that does not apply (<c>water_depth_m</c> on land, <c>base_height_m</c> on ocean) is
    /// ignored with a warning.
    /// </remarks>
    public static BiomeTerrainParameters? TryFrom(BiomeDefinition biome, ValidationReport report)
    {
        ArgumentNullException.ThrowIfNull(biome);
        ArgumentNullException.ThrowIfNull(report);

        bool ocean = biome.Tags.Contains(OceanTag);
        int errorsBefore = report.Count(Severity.Error);

        double relief = BiomePropertyReader.Require(biome, ReliefProperty, report, value => value >= 0, "must not be negative");
        double roughness = BiomePropertyReader.Require(biome, RoughnessProperty, report, value => value >= 0 && value <= 1, "must be between 0 and 1");
        double baseHeight = 0;

        if (ocean)
        {
            double depth = BiomePropertyReader.Require(biome, WaterDepthProperty, report, value => value > 0, "must be positive");
            baseHeight = -depth;
            BiomePropertyReader.WarnIgnored(biome, BaseHeightProperty, "ocean", report);
        }
        else
        {
            baseHeight = BiomePropertyReader.Require(biome, BaseHeightProperty, report, _ => true, string.Empty);
            BiomePropertyReader.WarnIgnored(biome, WaterDepthProperty, "land", report);
        }

        if (report.Count(Severity.Error) > errorsBefore)
        {
            return null;
        }

        return new BiomeTerrainParameters(baseHeight, relief, roughness, ocean);
    }
}
