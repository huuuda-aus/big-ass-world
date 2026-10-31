namespace Realm.Sim.Combat;

/// <summary>Something that happened in a fight, for presentation (animations, sounds, log) and for tests.</summary>
/// <param name="Tick">Encounter tick when it happened.</param>
/// <param name="Kind">What happened.</param>
/// <param name="ActorId">Who caused it, or -1.</param>
/// <param name="TargetId">Who it happened to, or -1.</param>
/// <param name="Amount">Damage, action points or similar; 0 when not applicable.</param>
/// <param name="Detail">An attack or condition identifier, or empty.</param>
public readonly record struct CombatEvent(long Tick, CombatEventKind Kind, int ActorId, int TargetId, int Amount, string Detail);
