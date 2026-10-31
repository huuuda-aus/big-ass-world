namespace Realm.Sim.Combat;

/// <summary>
/// The player's brain in real time: repeats whatever action the input layer has set (holding the attack key) each
/// time the player is ready. Recovery, not the key-repeat rate, sets the pace.
/// </summary>
/// <remarks>Thread-safety: set <see cref="Intent"/> only from the simulation thread (presentation sends a command).</remarks>
public sealed class HeldIntentBrain : ICombatBrain
{
    /// <summary>The held action, or null for none.</summary>
    public CombatAction? Intent { get; set; }

    /// <inheritdoc />
    public CombatAction? Decide(CombatEncounter encounter, Combatant self) => Intent;
}
