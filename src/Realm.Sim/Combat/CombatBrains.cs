using System;

namespace Realm.Sim.Combat;

/// <summary>Maps the behaviour words of <c>creature</c> content (<c>CombatVocabulary.Behaviours</c>) to AI brains.</summary>
/// <remarks>A test checks every vocabulary word has a brain here.</remarks>
public static class CombatBrains
{
    /// <summary>The brain for a behaviour word.</summary>
    /// <param name="behaviour">A word from <c>CombatVocabulary.Behaviours</c>.</param>
    /// <returns>The brain; brains are stateless and shared.</returns>
    /// <exception cref="ArgumentException">The word is unknown.</exception>
    public static ICombatBrain ForBehaviour(string behaviour) => behaviour switch
    {
        "nearest_enemy" => NearestEnemyBrain.Instance,
        _ => throw new ArgumentException($"Unknown creature behaviour '{behaviour}'.", nameof(behaviour)),
    };
}
