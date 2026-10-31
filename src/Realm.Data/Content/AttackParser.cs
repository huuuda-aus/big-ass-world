using System;
using System.Collections.Generic;
using Realm.Core.Ids;
using Realm.Core.Random;
using Realm.Core.Validation;

namespace Realm.Data.Content;

/// <summary>Parses and validates one <c>attack</c> YAML record.</summary>
/// <remarks>Determinism: no randomness. Thread-safety: stateless.</remarks>
public static class AttackParser
{
    private const string SchemaPage = "docs/reference/content/attack.md";
    private const string Code = ValidatorCodes.InvalidAttackField;
    private const string ConditionType = "condition";

    /// <summary>Parses one YAML document into an attack, reporting every problem found.</summary>
    /// <param name="yaml">The YAML text.</param>
    /// <param name="location">File location used in issues, for example <c>attacks/longsword.yaml</c>.</param>
    /// <param name="report">Receives all issues.</param>
    /// <returns>The attack, or null when any error prevented building it.</returns>
    /// <remarks>Whether a referenced condition exists is checked later by <see cref="ContentValidator"/>.</remarks>
    public static AttackDefinition? Parse(string yaml, string location, ValidationReport report)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(report);

        if (!ParserSupport.TryDeserialize(yaml, location, SchemaPage, report, out AttackDto dto))
        {
            return null;
        }

        int errorsBefore = report.Count(Severity.Error);
        ContentId id = ParserSupport.ParseId(dto.Id, location, report);
        ParserSupport.RequireName(dto.Name, location, report);
        TagSet tags = ParserSupport.ParseTags(dto.Tags, location, report);

        double recovery = Required(dto.RecoveryS, "recovery_s", "recovery_s: 1.2", location, report);
        CombatParserSupport.Range(recovery, 0, CombatVocabulary.MaxRecoverySeconds, true, "recovery_s", Code, location, report);
        double range = Required(dto.RangeM, "range_m", "range_m: 2", location, report);
        CombatParserSupport.Range(range, 0, CombatVocabulary.MaxRangeMeters, true, "range_m", Code, location, report);

        Dice damage = default;
        if (dto.Damage is null)
        {
            report.Error(ValidatorCodes.MissingField, location, "Missing required field 'damage'.", "For example: damage: 1d8+2.");
        }
        else if (!Dice.TryParse(dto.Damage, out damage))
        {
            report.Error(Code, location, $"Field 'damage' is not a dice expression: '{dto.Damage}'.", "Use NdS, NdS+B, NdS-B or a whole number, for example 2d6+1.");
        }

        string damageType = CombatParserSupport.DamageType(dto.DamageType, "damage_type", Code, location, report);
        CombatParserSupport.Range(dto.AttackBonus, -CombatVocabulary.MaxStat, CombatVocabulary.MaxStat, false, "attack_bonus", Code, location, report);
        CombatParserSupport.Range(dto.AreaRadiusM, 0, CombatVocabulary.MaxAreaRadiusMeters, false, "area_radius_m", Code, location, report);
        CombatParserSupport.Range(dto.ApCost, 0, CombatVocabulary.MaxActionPointCost, false, "ap_cost", Code, location, report);

        AttackCondition? applies = null;
        if (dto.Applies is { } a)
        {
            if (a.Condition is null || a.Chance is not { } chance)
            {
                report.Error(Code, location, "Field 'applies' needs 'condition' and 'chance'.", "For example: applies: { condition: condition.poisoned, chance: 20 }.");
            }
            else
            {
                CombatParserSupport.Range(chance, 0, CombatVocabulary.PercentScale, false, "applies.chance", Code, location, report);
                if (ParserSupport.ParseReference(a.Condition, "applies.condition", ConditionType, location, report) is { } condition)
                {
                    applies = new AttackCondition(condition, chance);
                }
            }
        }

        if (report.Count(Severity.Error) > errorsBefore)
        {
            return null;
        }

        // Safe: a missing or blank name was reported as an error above, which returned null.
        return new AttackDefinition(id, dto.Name!, tags, recovery, range, damage, damageType, dto.AttackBonus, dto.AreaRadiusM, dto.ApCost, applies);
    }

    private static double Required(double? value, string field, string example, string location, ValidationReport report)
    {
        if (value is { } v)
        {
            return v;
        }

        report.Error(ValidatorCodes.MissingField, location, $"Missing required field '{field}'.", $"For example: {example}.");
        return 1;
    }

    private sealed class AttackDto
    {
        public string? Id { get; set; }

        public string? Name { get; set; }

        public List<string?>? Tags { get; set; }

        public double? RecoveryS { get; set; }

        public double? RangeM { get; set; }

        public string? Damage { get; set; }

        public string? DamageType { get; set; }

        public int AttackBonus { get; set; }

        public double AreaRadiusM { get; set; }

        public int ApCost { get; set; } = 1;

        public AppliesDto? Applies { get; set; }
    }

    private sealed class AppliesDto
    {
        public string? Condition { get; set; }

        public int? Chance { get; set; }
    }
}
