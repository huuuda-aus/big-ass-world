namespace Realm.Sim.Combat;

/// <summary>Chooses what a combatant does when it is ready to act. AI profiles and the player's input both implement it.</summary>
/// <remarks>
/// Determinism contract: decide only from the encounter's state; never use <c>System.Random</c>, wall-clock time or
/// hash-order enumeration. Called on the simulation thread.
/// </remarks>
public interface ICombatBrain
{
    /// <summary>Picks an action for <paramref name="self"/>, which is alive, ready and not prevented.</summary>
    /// <param name="encounter">The fight, for reading other combatants.</param>
    /// <param name="self">The combatant to act.</param>
    /// <returns>The action, or null to wait (it is asked again next tick).</returns>
    CombatAction? Decide(CombatEncounter encounter, Combatant self);
}
