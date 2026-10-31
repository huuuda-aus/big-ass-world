using Realm.Core.Ids;

namespace Realm.Data.Content;

/// <summary>A condition an attack may apply to whoever it hits.</summary>
/// <param name="Condition">The <c>condition</c> record.</param>
/// <param name="ChancePercent">Chance per hit, 0 to 100.</param>
public readonly record struct AttackCondition(ContentId Condition, int ChancePercent);
