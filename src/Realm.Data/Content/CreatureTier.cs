using System.Collections.Generic;
using Realm.Core.Ids;

namespace Realm.Data.Content;

/// <summary>
/// One tier of a creature family with every value resolved: the family's values with the tier's overrides applied.
/// Tier 1 is the family itself.
/// </summary>
/// <param name="Tier">Tier number, from 1.</param>
/// <param name="Name">Display name, for example <c>Goblin Archer</c>.</param>
/// <param name="Tags">The family's tags plus the tier's own; enumerates in sorted order.</param>
/// <param name="Health">Health when unhurt.</param>
/// <param name="AttackRating">Added to an attack's bonus for hit rolls.</param>
/// <param name="Defence">Subtracted from an attacker's rating for hit rolls.</param>
/// <param name="Attacks">The <c>attack</c> records it can use, in order.</param>
/// <param name="Resistances">Percent reduction per damage type, 0 to 100, sorted by type.</param>
public sealed record CreatureTier(
    int Tier,
    string Name,
    TagSet Tags,
    int Health,
    int AttackRating,
    int Defence,
    IReadOnlyList<ContentId> Attacks,
    IReadOnlyDictionary<string, int> Resistances);
