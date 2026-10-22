using System;
using System.Collections.Generic;
using System.Linq;
using Realm.Core.Ids;
using Realm.Core.Validation;

namespace Realm.Data.Content;

/// <summary>Parses and validates one <c>service</c> YAML record.</summary>
/// <remarks>Determinism: no randomness. Thread-safety: stateless.</remarks>
public static class ServiceBuildingParser
{
    private const string SchemaPage = "docs/reference/content/service.md";

    /// <summary>Parses one YAML document into a service building, reporting every problem found.</summary>
    /// <param name="yaml">The YAML text.</param>
    /// <param name="location">File location used in issues, for example <c>services/inn.yaml</c>.</param>
    /// <param name="report">Receives all issues.</param>
    /// <returns>The service building, or null when any error prevented building it.</returns>
    public static ServiceBuildingDefinition? Parse(string yaml, string location, ValidationReport report)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(report);

        if (!ParserSupport.TryDeserialize(yaml, location, SchemaPage, report, out ServiceDto dto))
        {
            return null;
        }

        int errorsBefore = report.Count(Severity.Error);

        ContentId id = ParserSupport.ParseId(dto.Id, location, report);
        ParserSupport.RequireName(dto.Name, location, report);
        TagSet tags = ParserSupport.ParseTags(dto.Tags, location, report);
        IReadOnlyDictionary<string, int> counts = ParseCounts(dto.Counts, location, report);
        IReadOnlyList<string> wards = ParseWords(dto.Wards, "wards", ServiceVocabulary.Wards, location, report);
        string near = ParseWord(dto.Near, "near", ServiceVocabulary.Near, ServiceVocabulary.DefaultNear, location, report);
        string mainRoom = ParseWord(dto.MainRoom, "main_room", ServiceVocabulary.Rooms, ServiceVocabulary.DefaultMainRoom, location, report);
        string? foundation = dto.Foundation is null ? null : ParseWord(dto.Foundation, "foundation", ServiceVocabulary.Foundations, string.Empty, location, report);

        double minArea = dto.MinAreaM2 ?? ServiceVocabulary.DefaultMinAreaMeters2;
        if (!double.IsFinite(minArea) || minArea <= 0)
        {
            report.Error(ValidatorCodes.InvalidServiceField, location, "Field 'min_area_m2' must be a positive number.");
        }

        bool outside = dto.OutsideWall ?? false;
        if (outside && wards.Count > 0)
        {
            report.Error(
                ValidatorCodes.InvalidServiceField,
                location,
                "A service with 'outside_wall: true' cannot list 'wards'.",
                "Remove 'wards', or set 'outside_wall' to false.");
        }

        if (report.Count(Severity.Error) > errorsBefore)
        {
            return null;
        }

        // Safe: a missing or blank name was reported as an error above, which returned null.
        return new ServiceBuildingDefinition(id, dto.Name!, tags, counts, wards, near, outside, minArea, string.IsNullOrEmpty(foundation) ? null : foundation, mainRoom);
    }

    private static IReadOnlyDictionary<string, int> ParseCounts(Dictionary<string, int>? raw, string location, ValidationReport report)
    {
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        if (raw is null || raw.Count == 0)
        {
            report.Error(ValidatorCodes.MissingField, location, "Missing required field 'counts'.", "For example: counts: { city: 1, town: 1 }.");
            return counts;
        }

        foreach ((string tier, int count) in raw)
        {
            if (!ServiceVocabulary.Tiers.Contains(tier))
            {
                report.Error(
                    ValidatorCodes.InvalidServiceField,
                    location,
                    $"Field 'counts' has unknown tier '{tier}'.",
                    $"Use one of: {string.Join(", ", ServiceVocabulary.Tiers)}.");
            }
            else if (count < 1 || count > ServiceVocabulary.MaxCount)
            {
                report.Error(
                    ValidatorCodes.InvalidServiceField,
                    location,
                    $"Count for tier '{tier}' must be 1 to {ServiceVocabulary.MaxCount}, not {count}.",
                    "Leave the tier out to give it none.");
            }
            else
            {
                counts[tier] = count;
            }
        }

        return counts;
    }

    private static IReadOnlyList<string> ParseWords(
        List<string?>? raw, string field, IReadOnlyList<string> allowed, string location, ValidationReport report)
    {
        if (raw is null || raw.Count == 0)
        {
            return Array.Empty<string>();
        }

        var valid = new HashSet<string>(StringComparer.Ordinal);
        var ordered = new List<string>();
        foreach (string? word in raw)
        {
            if (word is null || !allowed.Contains(word))
            {
                report.Error(
                    ValidatorCodes.InvalidServiceField,
                    location,
                    $"Field '{field}' has unknown value '{word}'.",
                    $"Use one of: {string.Join(", ", allowed)}.");
            }
            else if (valid.Add(word))
            {
                ordered.Add(word);
            }
        }

        // Wards keep the author's order (most preferred first).
        return ordered;
    }

    private static string ParseWord(
        string? raw, string field, IReadOnlyList<string> allowed, string fallback, string location, ValidationReport report)
    {
        if (raw is null)
        {
            return fallback;
        }

        if (!allowed.Contains(raw))
        {
            report.Error(
                ValidatorCodes.InvalidServiceField,
                location,
                $"Field '{field}' has unknown value '{raw}'.",
                $"Use one of: {string.Join(", ", allowed)}.");
            return fallback;
        }

        return raw;
    }

    private sealed class ServiceDto
    {
        public string? Id { get; set; }

        public string? Name { get; set; }

        public List<string?>? Tags { get; set; }

        public Dictionary<string, int>? Counts { get; set; }

        public List<string?>? Wards { get; set; }

        public string? Near { get; set; }

        public bool? OutsideWall { get; set; }

        public double? MinAreaM2 { get; set; }

        public string? Foundation { get; set; }

        public string? MainRoom { get; set; }
    }
}
