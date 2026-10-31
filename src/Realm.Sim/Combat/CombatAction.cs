namespace Realm.Sim.Combat;

/// <summary>An intention to use one of the actor's attacks on a target.</summary>
/// <param name="AttackIndex">Index into the actor's <see cref="Combatant.Attacks"/>.</param>
/// <param name="TargetId">The target's <see cref="Combatant.Id"/>; for an area attack, the blast centres on it.</param>
public readonly record struct CombatAction(int AttackIndex, int TargetId);
