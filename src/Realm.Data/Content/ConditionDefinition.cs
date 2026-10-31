using Realm.Core.Ids;

namespace Realm.Data.Content;

/// <summary>
/// A condition an attack can put on a combatant, such as poisoned or asleep (ADR 0029).
/// Schema reference: docs/reference/content/condition.md.
/// </summary>
/// <param name="Id">Stable identifier, for example <c>condition.poisoned</c>.</param>
/// <param name="Name">Display name.</param>
/// <param name="Tags">Tags such as <c>toxin</c>; enumerates in sorted order.</param>
/// <param name="DurationSeconds">How long it lasts once applied; positive.</param>
/// <param name="PreventsActions">True when the affected combatant cannot act.</param>
/// <param name="BreaksOnDamage">True when any damage above zero ends it.</param>
/// <param name="Pulse">Damage dealt at intervals while it lasts, or null.</param>
public sealed record ConditionDefinition(
    ContentId Id,
    string Name,
    TagSet Tags,
    double DurationSeconds,
    bool PreventsActions,
    bool BreaksOnDamage,
    ConditionPulse? Pulse);
