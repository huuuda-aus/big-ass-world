namespace Realm.Data.Content;

/// <summary>Damage a condition deals at a regular interval while it lasts (poison, burning).</summary>
/// <param name="EverySeconds">Seconds between pulses; positive.</param>
/// <param name="Damage">Damage per pulse before resistance; zero or more.</param>
/// <param name="DamageType">Damage type word, for example <c>poison</c>.</param>
public readonly record struct ConditionPulse(double EverySeconds, int Damage, string DamageType);
