using System;
using System.Collections.Generic;
using Realm.Core.Ids;
using Realm.Core.Validation;

namespace Realm.Data.Content;

/// <summary>Parses and validates one <c>condition</c> YAML record.</summary>
/// <remarks>Determinism: no randomness. Thread-safety: stateless.</remarks>
public static class ConditionParser
{
    private const string SchemaPage = "docs/reference/content/condition.md";
    private const string Code = ValidatorCodes.InvalidConditionField;

    /// <summary>Parses one YAML document into a condition, reporting every problem found.</summary>
    /// <param name="yaml">The YAML text.</param>
    /// <param name="location">File location used in issues, for example <c>conditions/poisoned.yaml</c>.</param>
    /// <param name="report">Receives all issues.</param>
    /// <returns>The condition, or null when any error prevented building it.</returns>
    public static ConditionDefinition? Parse(string yaml, string location, ValidationReport report)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(report);

        if (!ParserSupport.TryDeserialize(yaml, location, SchemaPage, report, out ConditionDto dto))
        {
            return null;
        }

        int errorsBefore = report.Count(Severity.Error);
        ContentId id = ParserSupport.ParseId(dto.Id, location, report);
        ParserSupport.RequireName(dto.Name, location, report);
        TagSet tags = ParserSupport.ParseTags(dto.Tags, location, report);

        double duration = 0;
        if (dto.DurationS is not { } d)
        {
            report.Error(ValidatorCodes.MissingField, location, "Missing required field 'duration_s'.", "For example: duration_s: 10.");
        }
        else
        {
            duration = d;
            CombatParserSupport.Range(d, 0, CombatVocabulary.MaxDurationSeconds, true, "duration_s", Code, location, report);
        }

        ConditionPulse? pulse = null;
        if (dto.Pulse is { } p)
        {
            if (p.EveryS is not { } every || p.Damage is not { } damage)
            {
                report.Error(Code, location, "Field 'pulse' needs 'every_s' and 'damage'.", "For example: pulse: { every_s: 1, damage: 2, type: poison }.");
            }
            else
            {
                CombatParserSupport.Range(every, 0, Math.Max(duration, double.Epsilon), true, "pulse.every_s", Code, location, report);
                CombatParserSupport.Range(damage, 0, CombatVocabulary.MaxStat, false, "pulse.damage", Code, location, report);
                string type = CombatParserSupport.DamageType(p.Type, "pulse.type", Code, location, report);
                pulse = new ConditionPulse(every, damage, type);
            }
        }

        if (report.Count(Severity.Error) > errorsBefore)
        {
            return null;
        }

        // Safe: a missing or blank name was reported as an error above, which returned null.
        return new ConditionDefinition(id, dto.Name!, tags, duration, dto.PreventsActions, dto.BreaksOnDamage, pulse);
    }

    private sealed class ConditionDto
    {
        public string? Id { get; set; }

        public string? Name { get; set; }

        public List<string?>? Tags { get; set; }

        public double? DurationS { get; set; }

        public bool PreventsActions { get; set; }

        public bool BreaksOnDamage { get; set; }

        public PulseDto? Pulse { get; set; }
    }

    private sealed class PulseDto
    {
        public double? EveryS { get; set; }

        public int? Damage { get; set; }

        public string? Type { get; set; }
    }
}
