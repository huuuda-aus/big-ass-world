using System;
using System.Collections.Generic;
using System.Linq;
using Realm.Core.Ids;
using Realm.Core.Validation;

namespace Realm.Data.Content;

/// <summary>Parses and validates one <c>foliage</c> YAML record.</summary>
/// <remarks>Determinism: no randomness. Thread-safety: stateless.</remarks>
public static class FoliageParser
{
    private const string SchemaPage = "docs/reference/content/foliage.md";
    private const double MinScaleLimit = 0.1;
    private const double MaxScaleLimit = 5;
    private const double MaxSlopeLimit = 89;
    private const double MaxTrunkRadius = 2;
    private const double MinSpacingLimit = 0.5;

    /// <summary>Parses one YAML document into a foliage species, reporting every problem found.</summary>
    /// <param name="yaml">The YAML text.</param>
    /// <param name="location">File location used in issues, for example <c>foliage/oak.yaml</c>.</param>
    /// <param name="report">Receives all issues.</param>
    /// <returns>The species, or null when any error prevented building it.</returns>
    public static FoliageDefinition? Parse(string yaml, string location, ValidationReport report)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(report);

        if (!ParserSupport.TryDeserialize(yaml, location, SchemaPage, report, out FoliageDto dto))
        {
            return null;
        }

        int errorsBefore = report.Count(Severity.Error);
        ContentId id = ParserSupport.ParseId(dto.Id, location, report);
        ParserSupport.RequireName(dto.Name, location, report);
        TagSet tags = ParserSupport.ParseTags(dto.Tags, location, report);

        string kind = dto.Kind ?? string.Empty;
        if (!FoliageVocabulary.Kinds.Contains(kind))
        {
            report.Error(
                ValidatorCodes.InvalidFoliageField,
                location,
                dto.Kind is null ? "Missing required field 'kind'." : $"Field 'kind' has unknown value '{dto.Kind}'.",
                $"Use one of: {string.Join(", ", FoliageVocabulary.Kinds)}.");
        }

        bool grass = kind == FoliageVocabulary.Grass;
        double limit = grass ? FoliageVocabulary.MaxTuftsPerHectare : FoliageVocabulary.MaxPlantsPerHectare;
        var densities = new SortedDictionary<ContentId, double>();
        if (dto.PerHectare is null || dto.PerHectare.Count == 0)
        {
            report.Error(ValidatorCodes.MissingField, location, "Missing required field 'per_hectare'.", "For example: per_hectare: { biome.temperate_forest: 40 }.");
        }
        else
        {
            foreach ((string biomeText, double density) in dto.PerHectare)
            {
                ContentId biome = ParserSupport.ParseId(biomeText, location, report);
                if (!double.IsFinite(density) || density <= 0 || density > limit)
                {
                    report.Error(
                        ValidatorCodes.InvalidFoliageField,
                        location,
                        $"Density for '{biomeText}' must be above 0 and at most {limit:0} per hectare, not {density}.",
                        "Leave the biome out to grow nothing there.");
                }
                else
                {
                    densities[biome] = density;
                }
            }
        }

        double spacing = dto.MinSpacingM ?? (grass ? 0 : FoliageVocabulary.DefaultMinSpacingMeters);
        if (!grass && (!double.IsFinite(spacing) || spacing < MinSpacingLimit))
        {
            report.Error(ValidatorCodes.InvalidFoliageField, location, $"Field 'min_spacing_m' must be at least {MinSpacingLimit} metres.");
        }

        (double minHeight, double maxHeight) = ParsePair(dto.HeightRangeM, "height_range_m", 0, FoliageVocabulary.DefaultMaxHeightMeters, location, report, -500, 10000);
        (double minScale, double maxScale) = ParsePair(dto.ScaleRange, "scale_range", 0.8, 1.2, location, report, MinScaleLimit, MaxScaleLimit);
        double slope = dto.MaxSlopeDeg ?? FoliageVocabulary.DefaultMaxSlopeDegrees;
        Check(slope, 0, MaxSlopeLimit, "max_slope_deg", location, report);
        double cluster = dto.ClusterM ?? 0;
        Check(cluster, 0, 5000, "cluster_m", location, report);
        double strength = dto.ClusterStrength ?? 0.6;
        Check(strength, 0, 1, "cluster_strength", location, report);
        double trunk = dto.TrunkRadiusM ?? 0;
        Check(trunk, 0, MaxTrunkRadius, "trunk_radius_m", location, report);

        if (report.Count(Severity.Error) > errorsBefore)
        {
            return null;
        }

        // Safe: a missing or blank name was reported as an error above, which returned null.
        return new FoliageDefinition(id, dto.Name!, kind, tags, densities, spacing, minHeight, maxHeight, slope, minScale, maxScale, cluster, strength, trunk);
    }

    private static (double Low, double High) ParsePair(List<double>? raw, string field, double defaultLow, double defaultHigh, string location, ValidationReport report, double min, double max)
    {
        if (raw is null)
        {
            return (defaultLow, defaultHigh);
        }

        if (raw.Count != 2 || raw.Any(v => !double.IsFinite(v) || v < min || v > max) || raw[0] > raw[1])
        {
            report.Error(
                ValidatorCodes.InvalidFoliageField,
                location,
                $"Field '{field}' must be two numbers, low then high, between {min} and {max}.",
                $"For example: {field}: [{defaultLow}, {defaultHigh}].");
            return (defaultLow, defaultHigh);
        }

        return (raw[0], raw[1]);
    }

    private static void Check(double value, double min, double max, string field, string location, ValidationReport report)
    {
        if (!double.IsFinite(value) || value < min || value > max)
        {
            report.Error(ValidatorCodes.InvalidFoliageField, location, $"Field '{field}' must be between {min} and {max}, not {value}.");
        }
    }

    private sealed class FoliageDto
    {
        public string? Id { get; set; }

        public string? Name { get; set; }

        public string? Kind { get; set; }

        public List<string?>? Tags { get; set; }

        public Dictionary<string, double>? PerHectare { get; set; }

        public double? MinSpacingM { get; set; }

        public List<double>? HeightRangeM { get; set; }

        public double? MaxSlopeDeg { get; set; }

        public List<double>? ScaleRange { get; set; }

        public double? ClusterM { get; set; }

        public double? ClusterStrength { get; set; }

        public double? TrunkRadiusM { get; set; }
    }
}
