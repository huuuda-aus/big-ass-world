using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Realm.Core.Ids;
using Realm.Core.Validation;
using YamlDotNet.Serialization;

namespace Realm.Data.Content;

/// <summary>
/// Parses and validates one <c>creature</c> YAML record (a family) and resolves its tiers: tier 1 is the family's own
/// values; each entry of <c>tiers</c> is the next tier and overrides only the fields it gives (health, attack rating,
/// defence, speed). A tier's <c>attacks</c>
/// replace the list, its <c>resistances</c> are merged over the family's per damage type, and its <c>tags</c> are added.
/// </summary>
/// <remarks>Determinism: no randomness. Thread-safety: stateless.</remarks>
public static class CreatureParser
{
    private const string SchemaPage = "docs/reference/content/creature.md";
    private const string Code = ValidatorCodes.InvalidCreatureField;
    private const string AttackType = "attack";

    /// <summary>Parses one YAML document into a creature family, reporting every problem found.</summary>
    /// <param name="yaml">The YAML text.</param>
    /// <param name="location">File location used in issues, for example <c>creatures/goblin.yaml</c>.</param>
    /// <param name="report">Receives all issues.</param>
    /// <returns>The creature family, or null when any error prevented building it.</returns>
    /// <remarks>Whether referenced attacks exist is checked later by <see cref="ContentValidator"/>.</remarks>
    public static CreatureDefinition? Parse(string yaml, string location, ValidationReport report)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(report);

        if (!ParserSupport.TryDeserialize(yaml, location, SchemaPage, report, out CreatureDto dto))
        {
            return null;
        }

        int errorsBefore = report.Count(Severity.Error);
        ContentId id = ParserSupport.ParseId(dto.Id, location, report);
        ParserSupport.RequireName(dto.Name, location, report);
        TagSet tags = ParserSupport.ParseTags(dto.Tags, location, report);

        string behaviour = dto.Behaviour ?? string.Empty;
        if (dto.Behaviour is null)
        {
            report.Error(ValidatorCodes.MissingField, location, "Missing required field 'behaviour'.", $"Use one of: {string.Join(", ", CombatVocabulary.Behaviours)}.");
        }
        else if (!CombatVocabulary.Behaviours.Contains(behaviour))
        {
            report.Error(Code, location, $"Behaviour '{behaviour}' is unknown.", $"Use one of: {string.Join(", ", CombatVocabulary.Behaviours)}.");
        }

        int health = 1;
        if (dto.Health is not { } h)
        {
            report.Error(ValidatorCodes.MissingField, location, "Missing required field 'health'.", "For example: health: 14.");
        }
        else
        {
            health = h;
            CombatParserSupport.Range(h, 0, CombatVocabulary.MaxStat, true, "health", Code, location, report);
        }

        Stat(dto.AttackRating, "attack_rating", location, report);
        Stat(dto.Defence, "defence", location, report);
        IReadOnlyList<ContentId> attacks = Attacks(dto.Attacks, "attacks", required: true, location, report) ?? Array.Empty<ContentId>();
        IReadOnlyDictionary<string, int> resistances = CombatParserSupport.Resistances(dto.Resistances, location, report);

        double speed = 0;
        if (dto.SpeedMS is not { } sp)
        {
            report.Error(ValidatorCodes.MissingField, location, "Missing required field 'speed_m_s'.", "For example: speed_m_s: 4 (0 for a creature that never moves).");
        }
        else
        {
            speed = sp;
            CombatParserSupport.Range(sp, 0, CombatVocabulary.MaxSpeedMetersPerSecond, false, "speed_m_s", Code, location, report);
        }

        var tiers = new List<CreatureTier>
        {
            // Safe: a missing name is reported as an error, which returns null before this list is used.
            new(1, dto.Name ?? string.Empty, tags, health, dto.AttackRating, dto.Defence, attacks, resistances, speed),
        };

        foreach (TierDto t in dto.Tiers ?? new List<TierDto>())
        {
            CreatureTier family = tiers[0];
            int number = tiers.Count + 1;
            string field = $"tiers[{number - 2}]";
            if (string.IsNullOrWhiteSpace(t.Name))
            {
                report.Error(ValidatorCodes.MissingField, location, $"Tier {number} ({field}) is missing 'name'.");
            }

            if (t.Health is { } th)
            {
                CombatParserSupport.Range(th, 0, CombatVocabulary.MaxStat, true, $"{field}.health", Code, location, report);
            }

            if (t.SpeedMS is { } ts)
            {
                CombatParserSupport.Range(ts, 0, CombatVocabulary.MaxSpeedMetersPerSecond, false, $"{field}.speed_m_s", Code, location, report);
            }

            Stat(t.AttackRating ?? 0, $"{field}.attack_rating", location, report);
            Stat(t.Defence ?? 0, $"{field}.defence", location, report);
            IReadOnlyList<ContentId>? tierAttacks = Attacks(t.Attacks, $"{field}.attacks", required: false, location, report);

            var mergedResistances = new SortedDictionary<string, int>(new Dictionary<string, int>(family.Resistances), StringComparer.Ordinal);
            foreach ((string type, int percent) in CombatParserSupport.Resistances(t.Resistances, location, report))
            {
                mergedResistances[type] = percent;
            }

            TagSet tierTags = TagSet.From(family.Tags.Concat(ParserSupport.ParseTags(t.Tags, location, report)));
            tiers.Add(new CreatureTier(
                number,
                t.Name ?? string.Empty,
                tierTags,
                t.Health ?? family.Health,
                t.AttackRating ?? family.AttackRating,
                t.Defence ?? family.Defence,
                tierAttacks ?? family.Attacks,
                new ReadOnlyDictionary<string, int>(mergedResistances),
                t.SpeedMS ?? family.SpeedMetersPerSecond));
        }

        if (report.Count(Severity.Error) > errorsBefore)
        {
            return null;
        }

        // Safe: a missing or blank name was reported as an error above, which returned null.
        return new CreatureDefinition(id, dto.Name!, behaviour, tiers);
    }

    private static void Stat(int value, string field, string location, ValidationReport report) =>
        CombatParserSupport.Range(value, -CombatVocabulary.MaxStat, CombatVocabulary.MaxStat, false, field, Code, location, report);

    private static IReadOnlyList<ContentId>? Attacks(List<string?>? raw, string field, bool required, string location, ValidationReport report)
    {
        if (raw is null && !required)
        {
            return null;
        }

        if (raw is null || raw.Count == 0)
        {
            report.Error(required ? ValidatorCodes.MissingField : Code, location, $"Field '{field}' needs at least one attack.", "For example: attacks: [attack.goblin_club].");
            return null;
        }

        var attacks = new List<ContentId>();
        foreach (string? text in raw)
        {
            if (text is null)
            {
                report.Error(Code, location, $"Field '{field}' has an empty entry.");
            }
            else if (ParserSupport.ParseReference(text, field, AttackType, location, report) is { } attack)
            {
                attacks.Add(attack);
            }
        }

        return attacks.AsReadOnly();
    }

    private sealed class CreatureDto
    {
        public string? Id { get; set; }

        public string? Name { get; set; }

        public List<string?>? Tags { get; set; }

        public string? Behaviour { get; set; }

        public int? Health { get; set; }

        public int AttackRating { get; set; }

        public int Defence { get; set; }

        public List<string?>? Attacks { get; set; }

        public Dictionary<string, int>? Resistances { get; set; }

        [YamlMember(Alias = "speed_m_s")]
        public double? SpeedMS { get; set; }

        public List<TierDto>? Tiers { get; set; }
    }

    private sealed class TierDto
    {
        public string? Name { get; set; }

        public List<string?>? Tags { get; set; }

        public int? Health { get; set; }

        public int? AttackRating { get; set; }

        public int? Defence { get; set; }

        public List<string?>? Attacks { get; set; }

        public Dictionary<string, int>? Resistances { get; set; }

        [YamlMember(Alias = "speed_m_s")]
        public double? SpeedMS { get; set; }
    }
}
