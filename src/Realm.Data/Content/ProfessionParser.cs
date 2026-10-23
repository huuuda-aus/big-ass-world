using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Realm.Core.Ids;
using Realm.Core.Validation;

namespace Realm.Data.Content;

/// <summary>Parses and validates one <c>profession</c> YAML record.</summary>
/// <remarks>Determinism: no randomness. Thread-safety: stateless.</remarks>
public static class ProfessionParser
{
    private const string SchemaPage = "docs/reference/content/profession.md";

    /// <summary>Parses one YAML document into a profession, reporting every problem found.</summary>
    /// <param name="yaml">The YAML text.</param>
    /// <param name="location">File location used in issues, for example <c>professions/baker.yaml</c>.</param>
    /// <param name="report">Receives all issues.</param>
    /// <returns>The profession, or null when any error prevented building it.</returns>
    public static ProfessionDefinition? Parse(string yaml, string location, ValidationReport report)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(report);

        if (!ParserSupport.TryDeserialize(yaml, location, SchemaPage, report, out ProfessionDto dto))
        {
            return null;
        }

        int errorsBefore = report.Count(Severity.Error);
        ContentId id = ParserSupport.ParseId(dto.Id, location, report);
        ParserSupport.RequireName(dto.Name, location, report);
        TagSet tags = ParserSupport.ParseTags(dto.Tags, location, report);

        string kind = ProfessionVocabulary.DefaultWorkplaceKind;
        ContentId? service = null;
        if (dto.Workplace is { Count: > 0 })
        {
            if (dto.Workplace.TryGetValue("service", out string? serviceText))
            {
                kind = "service";
                service = ParserSupport.ParseId(serviceText, location, report);
            }
            else if (dto.Workplace.TryGetValue("kind", out string? kindText))
            {
                kind = kindText ?? string.Empty;
                if (!ProfessionVocabulary.WorkplaceKinds.Contains(kind))
                {
                    report.Error(
                        ValidatorCodes.InvalidProfessionField,
                        location,
                        $"Workplace kind '{kind}' is unknown.",
                        $"Use one of: {string.Join(", ", ProfessionVocabulary.WorkplaceKinds)}, or 'service: <id>'.");
                }
            }
            else
            {
                report.Error(ValidatorCodes.InvalidProfessionField, location, "Field 'workplace' needs 'service: <id>' or 'kind: <kind>'.");
            }
        }

        var shares = new SortedDictionary<string, double>(StringComparer.Ordinal);
        if (dto.Share is null || dto.Share.Count == 0)
        {
            report.Error(ValidatorCodes.MissingField, location, "Missing required field 'share'.", "For example: share: { city: 0.05, town: 0.1 }.");
        }
        else
        {
            foreach ((string tier, double share) in dto.Share)
            {
                if (!ProfessionVocabulary.Tiers.Contains(tier))
                {
                    report.Error(ValidatorCodes.InvalidProfessionField, location, $"Field 'share' has unknown tier '{tier}'.", $"Use one of: {string.Join(", ", ProfessionVocabulary.Tiers)}.");
                }
                else if (!double.IsFinite(share) || share < 0 || share > 1)
                {
                    report.Error(ValidatorCodes.InvalidProfessionField, location, $"Share for tier '{tier}' must be between 0 and 1, not {share}.");
                }
                else
                {
                    shares[tier] = share;
                }
            }
        }

        var day = new List<DayBlock>();
        if (dto.Day is null || dto.Day.Count == 0)
        {
            report.Error(ValidatorCodes.MissingField, location, "Missing required field 'day'.", "For example: day: [{ from: \"06:00\", to: \"18:00\", do: work }].");
        }
        else
        {
            foreach (DayDto block in dto.Day)
            {
                int? from = ParseTime(block.From);
                int? to = ParseTime(block.To);
                string activity = block.Do ?? string.Empty;
                if (from is null || to is null)
                {
                    report.Error(ValidatorCodes.InvalidProfessionField, location, "A day block needs 'from' and 'to' as quoted \"HH:MM\" times.", "For example: { from: \"06:00\", to: \"18:00\", do: work }.");
                }
                else if (!ProfessionVocabulary.Activities.Contains(activity))
                {
                    report.Error(ValidatorCodes.InvalidProfessionField, location, $"Day block has unknown activity '{activity}'.", $"Use one of: {string.Join(", ", ProfessionVocabulary.Activities)}.");
                }
                else if (from == to)
                {
                    report.Error(ValidatorCodes.InvalidProfessionField, location, $"Day block '{activity}' starts and ends at the same time.");
                }
                else
                {
                    day.Add(new DayBlock(from.Value, to.Value, activity));
                }
            }

            if (CheckOverlap(day) is { } clash)
            {
                report.Error(ValidatorCodes.InvalidProfessionField, location, clash, "Make the blocks of the day follow one another without overlapping.");
            }
        }

        if (report.Count(Severity.Error) > errorsBefore)
        {
            return null;
        }

        // Safe: a missing or blank name was reported as an error above, which returned null.
        return new ProfessionDefinition(id, dto.Name!, tags, kind, service, shares, day);
    }

    private static int? ParseTime(string? text)
    {
        if (text is null)
        {
            return null;
        }

        string[] parts = text.Split(':');
        if (parts.Length != 2
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int h)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int m)
            || h < 0 || h > 24 || m < 0 || m > 59 || (h == 24 && m != 0))
        {
            return null;
        }

        int minute = (h * 60) + m;
        return minute == ProfessionVocabulary.MinutesPerDay ? 0 : minute;
    }

    // Blocks may not overlap; a block that ends before it starts wraps past midnight.
    private static string? CheckOverlap(List<DayBlock> blocks)
    {
        var covered = new int[ProfessionVocabulary.MinutesPerDay];
        foreach (DayBlock b in blocks)
        {
            int length = b.ToMinute > b.FromMinute ? b.ToMinute - b.FromMinute : ProfessionVocabulary.MinutesPerDay - b.FromMinute + b.ToMinute;
            for (int k = 0; k < length; k++)
            {
                int minute = (b.FromMinute + k) % ProfessionVocabulary.MinutesPerDay;
                if (covered[minute]++ > 0)
                {
                    return $"Day blocks overlap at {minute / 60:00}:{minute % 60:00}.";
                }
            }
        }

        return null;
    }

    private sealed class ProfessionDto
    {
        public string? Id { get; set; }

        public string? Name { get; set; }

        public List<string?>? Tags { get; set; }

        public Dictionary<string, string?>? Workplace { get; set; }

        public Dictionary<string, double>? Share { get; set; }

        public List<DayDto>? Day { get; set; }
    }

    private sealed class DayDto
    {
        public string? From { get; set; }

        public string? To { get; set; }

        public string? Do { get; set; }
    }
}
