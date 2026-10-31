using System;
using System.Collections.Generic;
using Realm.Core.Ids;

namespace Realm.Data.Content;

/// <summary>
/// A creature family with its tiers (CLAUDE.md section 6.5): tier 1 is the family's own values, higher tiers override
/// some of them. Schema reference: docs/reference/content/creature.md.
/// </summary>
/// <param name="Id">Stable identifier, for example <c>creature.goblin</c>.</param>
/// <param name="Name">Family display name.</param>
/// <param name="Behaviour">AI profile from <see cref="CombatVocabulary.Behaviours"/>.</param>
/// <param name="Tiers">Every tier, resolved, in tier order; never empty.</param>
public sealed record CreatureDefinition(
    ContentId Id,
    string Name,
    string Behaviour,
    IReadOnlyList<CreatureTier> Tiers)
{
    /// <summary>Gets one tier.</summary>
    /// <param name="tier">Tier number, from 1.</param>
    /// <returns>The resolved tier.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The family has no such tier.</exception>
    public CreatureTier Tier(int tier)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tier, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(tier, Tiers.Count);
        return Tiers[tier - 1];
    }
}
