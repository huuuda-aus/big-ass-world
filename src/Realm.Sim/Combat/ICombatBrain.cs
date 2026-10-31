namespace Realm.Sim.Combat;

/// <summary>Chooses what a combatant does each tick. AI profiles and the player's input both implement it.</summary>
/// <remarks>
/// Determinism contract: decide only from the encounter's state; never use <c>System.Random</c>, wall-clock time or
/// hash-order enumeration. Called on the simulation thread.
/// </remarks>
public interface ICombatBrain
{
    /// <summary>
    /// Decides for <paramref name="self"/>, which is alive and not prevented. Called every tick, ready or not: the action
    /// is only taken when <see cref="Combatant.ReadyAt"/> has come, but walking happens while recovering too.
    /// </summary>
    /// <param name="encounter">The fight, for reading other combatants.</param>
    /// <param name="self">The combatant to decide for.</param>
    /// <returns>The decision; <see cref="CombatDecision.None"/> to wait.</returns>
    CombatDecision Decide(CombatEncounter encounter, Combatant self);
}
