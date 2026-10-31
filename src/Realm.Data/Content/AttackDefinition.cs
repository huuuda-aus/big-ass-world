using Realm.Core.Ids;
using Realm.Core.Random;

namespace Realm.Data.Content;

/// <summary>
/// One thing a combatant can do to a target: a weapon swing, a bow shot or a spell (ADR 0029).
/// Schema reference: docs/reference/content/attack.md.
/// </summary>
/// <param name="Id">Stable identifier, for example <c>attack.longsword</c>.</param>
/// <param name="Name">Display name.</param>
/// <param name="Tags">Tags such as <c>melee</c> or <c>spell</c>; enumerates in sorted order.</param>
/// <param name="RecoverySeconds">Time before the user may act again; positive.</param>
/// <param name="RangeMeters">Furthest target distance; positive.</param>
/// <param name="Damage">Damage dice.</param>
/// <param name="DamageType">Damage type word, matched against resistances.</param>
/// <param name="AttackBonus">Added to the user's attack rating for the hit roll.</param>
/// <param name="AreaRadiusMeters">Blast radius around the target; zero for a single target.</param>
/// <param name="ActionPointCost">Action points it costs in the tactical pause.</param>
/// <param name="Applies">A condition it may apply on a hit, or null.</param>
public sealed record AttackDefinition(
    ContentId Id,
    string Name,
    TagSet Tags,
    double RecoverySeconds,
    double RangeMeters,
    Dice Damage,
    string DamageType,
    int AttackBonus,
    double AreaRadiusMeters,
    int ActionPointCost,
    AttackCondition? Applies);
