using System;
using System.Collections.Generic;
using System.Linq;
using Realm.Core.Ids;
using Realm.Core.Validation;

namespace Realm.Data.Content;

/// <summary>Checks rules that span several records, after loading.</summary>
/// <remarks>
/// Determinism: records are visited in sorted ID order, so issue order is stable.
/// Thread-safety: stateless.
/// </remarks>
public static class ContentValidator
{
    /// <summary>Reports every reference that points at a record which does not exist.</summary>
    /// <remarks>Covers <c>item.material</c>, <c>region.biome</c>, the biomes a foliage species grows in, a profession's workplace, an attack's condition and a creature's attacks; also warns (<see cref="ValidatorCodes.UnusedDamageType"/>) about resistances to damage types nothing deals.</remarks>
    /// <param name="database">The loaded content.</param>
    /// <param name="report">Receives <see cref="ValidatorCodes.DanglingReference"/> errors.</param>
    public static void ValidateReferences(ContentDatabase database, ValidationReport report)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(report);

        foreach (ItemDefinition item in database.Items.Values)
        {
            if (item.Material is { } material && !database.Materials.ContainsKey(material))
            {
                database.TryGetSource(item.Id, out string location);
                report.Error(
                    ValidatorCodes.DanglingReference,
                    location,
                    $"Item '{item.Id}' references material '{material}', which does not exist.",
                    "Fix the ID or add the material.");
            }
        }

        foreach (RegionDefinition region in database.Regions.Values)
        {
            if (!database.Biomes.ContainsKey(region.Biome))
            {
                database.TryGetSource(region.Id, out string location);
                report.Error(
                    ValidatorCodes.DanglingReference,
                    location,
                    $"Region '{region.Id}' references biome '{region.Biome}', which does not exist.",
                    "Fix the ID or add the biome.");
            }
        }

        foreach (FoliageDefinition species in database.Foliage.Values)
        {
            foreach (ContentId biome in species.PerHectare.Keys)
            {
                if (!database.Biomes.ContainsKey(biome))
                {
                    database.TryGetSource(species.Id, out string location);
                    report.Error(
                        ValidatorCodes.DanglingReference,
                        location,
                        $"Foliage '{species.Id}' grows in biome '{biome}', which does not exist.",
                        "Fix the ID or add the biome.");
                }
            }
        }

        foreach (ProfessionDefinition profession in database.Professions.Values)
        {
            if (profession.WorkplaceService is { } service && !database.Services.ContainsKey(service))
            {
                database.TryGetSource(profession.Id, out string location);
                report.Error(
                    ValidatorCodes.DanglingReference,
                    location,
                    $"Profession '{profession.Id}' works in service '{service}', which does not exist.",
                    "Fix the ID or add the service building.");
            }
        }

        ValidateCombatReferences(database, report);

        foreach (string tier in ProfessionVocabulary.Tiers)
        {
            double total = database.Professions.Values.Sum(p => p.ShareFor(tier));
            if (total > 1.0 + 1e-9)
            {
                report.Error(
                    ValidatorCodes.InvalidProfessionField,
                    "professions",
                    $"The shares of the professions for tier '{tier}' add up to {total:0.###}, more than 1.",
                    "Lower some shares; whoever is left over becomes the filler profession.");
            }
        }
    }

    // attack.applies.condition and every tier's attacks must exist; resistances to damage types nothing deals are suspicious.
    private static void ValidateCombatReferences(ContentDatabase database, ValidationReport report)
    {
        var dealt = new HashSet<string>(StringComparer.Ordinal);
        foreach (ConditionDefinition condition in database.Conditions.Values)
        {
            if (condition.Pulse is { } pulse)
            {
                dealt.Add(pulse.DamageType);
            }
        }

        foreach (AttackDefinition attack in database.Attacks.Values)
        {
            dealt.Add(attack.DamageType);
            if (attack.Applies is { } applies && !database.Conditions.ContainsKey(applies.Condition))
            {
                database.TryGetSource(attack.Id, out string location);
                report.Error(
                    ValidatorCodes.DanglingReference,
                    location,
                    $"Attack '{attack.Id}' applies condition '{applies.Condition}', which does not exist.",
                    "Fix the ID or add the condition.");
            }
        }

        foreach (CreatureDefinition creature in database.Creatures.Values)
        {
            database.TryGetSource(creature.Id, out string location);
            foreach (CreatureTier tier in creature.Tiers)
            {
                foreach (ContentId attack in tier.Attacks.Distinct())
                {
                    if (!database.Attacks.ContainsKey(attack))
                    {
                        report.Error(
                            ValidatorCodes.DanglingReference,
                            location,
                            $"Creature '{creature.Id}' tier {tier.Tier} uses attack '{attack}', which does not exist.",
                            "Fix the ID or add the attack.");
                    }
                }
            }

            foreach (string type in creature.Tiers.SelectMany(t => t.Resistances.Keys).Distinct().OrderBy(t => t, StringComparer.Ordinal))
            {
                if (!dealt.Contains(type))
                {
                    report.Warning(
                        ValidatorCodes.UnusedDamageType,
                        location,
                        $"Creature '{creature.Id}' resists '{type}', but no attack or condition deals that damage type.",
                        "Check the spelling, or ignore this if the damage type comes later.");
                }
            }
        }
    }
}
