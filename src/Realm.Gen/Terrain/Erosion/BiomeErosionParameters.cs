using System;
using Realm.Core.Validation;
using Realm.Data.Content;

namespace Realm.Gen.Terrain.Erosion;

/// <summary>
/// What the erosion and river steps read from a <see cref="BiomeDefinition"/>. Vocabulary approved by the
/// developer on 2026-10-20: <c>rainfall</c>, <c>erodibility</c>, <c>max_slope_deg</c> and <c>river_loss</c>.
/// Ocean biomes ignore all four: the sea floor is neither carved nor relaxed and receives no rain.
/// </summary>
/// <param name="Rainfall">0 to 1: how much water each area adds to the flow. Drives how many rivers there are.</param>
/// <param name="Erodibility">0 to 1: how strongly flowing water carves the ground; 0 is bedrock.</param>
/// <param name="MaxSlopeDegrees">Steepest slope that stays stable, in degrees; slopes beyond it slump.</param>
/// <param name="RiverLoss">0 to 1: fraction of a river's flow lost per kilometre while crossing this biome.</param>
/// <param name="IsOcean">True for biomes tagged <c>ocean</c>.</param>
public sealed record BiomeErosionParameters(
    double Rainfall,
    double Erodibility,
    double MaxSlopeDegrees,
    double RiverLoss,
    bool IsOcean)
{
    /// <summary>Property name: rainfall, 0 to 1 (land biomes).</summary>
    public const string RainfallProperty = "rainfall";

    /// <summary>Property name: erodibility, 0 to 1 (land biomes).</summary>
    public const string ErodibilityProperty = "erodibility";

    /// <summary>Property name: steepest stable slope in degrees (land biomes).</summary>
    public const string MaxSlopeProperty = "max_slope_deg";

    /// <summary>Property name: fraction of river flow lost per kilometre, 0 to 1 (land biomes).</summary>
    public const string RiverLossProperty = "river_loss";

    /// <summary>Property name: how deep the biome's big rivers cut gorges with cliff walls, in metres (optional, 0 = none).</summary>
    public const string GorgeDepthProperty = "gorge_depth_m";

    /// <summary>Largest allowed gorge depth in metres.</summary>
    public const double MaxGorgeDepthMeters = 200;

    /// <summary>Depth in metres that the biome's largest rivers cut below the surrounding ground; 0 when the biome sets none.</summary>
    public double GorgeDepthMeters { get; init; }

    /// <summary>Slope limit used for ocean biomes: effectively none, so the sea floor is never relaxed.</summary>
    public const double OceanMaxSlopeDegrees = 89.0;

    /// <summary>Reads and validates the erosion properties of a biome.</summary>
    /// <param name="biome">The biome.</param>
    /// <param name="report">Receives every problem found.</param>
    /// <returns>The parameters, or null when a required property is missing or out of range.</returns>
    /// <remarks>
    /// Land biomes need all four properties. Ocean biomes need none; any given is ignored with a warning.
    /// </remarks>
    public static BiomeErosionParameters? TryFrom(BiomeDefinition biome, ValidationReport report)
    {
        ArgumentNullException.ThrowIfNull(biome);
        ArgumentNullException.ThrowIfNull(report);

        if (biome.Tags.Contains(BiomeTerrainParameters.OceanTag))
        {
            foreach (string name in new[] { RainfallProperty, ErodibilityProperty, MaxSlopeProperty, RiverLossProperty, GorgeDepthProperty })
            {
                BiomePropertyReader.WarnIgnored(biome, name, "ocean", report);
            }

            return new BiomeErosionParameters(0, 0, OceanMaxSlopeDegrees, 0, true);
        }

        int errorsBefore = report.Count(Severity.Error);
        double rainfall = BiomePropertyReader.Require(biome, RainfallProperty, report, v => v >= 0 && v <= 1, "must be between 0 and 1");
        double erodibility = BiomePropertyReader.Require(biome, ErodibilityProperty, report, v => v >= 0 && v <= 1, "must be between 0 and 1");
        double maxSlope = BiomePropertyReader.Require(biome, MaxSlopeProperty, report, v => v > 0 && v <= 90, "must be above 0 and at most 90 degrees");
        double riverLoss = BiomePropertyReader.Require(biome, RiverLossProperty, report, v => v >= 0 && v <= 1, "must be between 0 and 1");

        double gorge = BiomePropertyReader.Optional(biome, GorgeDepthProperty, report, v => v >= 0 && v <= MaxGorgeDepthMeters, $"must be between 0 and {MaxGorgeDepthMeters} m");

        if (report.Count(Severity.Error) > errorsBefore)
        {
            return null;
        }

        return new BiomeErosionParameters(rainfall, erodibility, maxSlope, riverLoss, false) { GorgeDepthMeters = gorge };
    }
}
