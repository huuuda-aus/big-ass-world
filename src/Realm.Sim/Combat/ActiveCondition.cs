namespace Realm.Sim.Combat;

/// <summary>A condition currently on a combatant.</summary>
/// <param name="Definition">What the condition is.</param>
/// <param name="EndsAt">Tick at which it wears off.</param>
/// <param name="NextPulseAt">Tick of the next damage pulse; ignored when the condition has no pulses.</param>
public readonly record struct ActiveCondition(Condition Definition, long EndsAt, long NextPulseAt);
