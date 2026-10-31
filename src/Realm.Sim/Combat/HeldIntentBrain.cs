using Realm.Core.Geometry;

namespace Realm.Sim.Combat;

/// <summary>
/// The player's brain in real time: repeats whatever action the input layer has set (holding the attack key) each
/// time the player is ready, and walks towards <see cref="MoveTo"/> otherwise. Recovery, not the key-repeat rate, sets
/// the pace.
/// </summary>
/// <remarks>Thread-safety: set the properties only from the simulation thread (presentation sends a command).</remarks>
public sealed class HeldIntentBrain : ICombatBrain
{
    /// <summary>The held action, or null for none.</summary>
    public CombatAction? Intent { get; set; }

    /// <summary>Where the player is walking to, or null to stand still.</summary>
    public Point2D? MoveTo { get; set; }

    /// <inheritdoc />
    public CombatDecision Decide(CombatEncounter encounter, Combatant self) => new(Intent, MoveTo);
}
