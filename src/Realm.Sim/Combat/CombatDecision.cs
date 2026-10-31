using Realm.Core.Geometry;

namespace Realm.Sim.Combat;

/// <summary>What a brain wants this tick: an action to take when ready, a place to walk towards, both, or neither.</summary>
/// <param name="Action">The action to take if the combatant is ready and the action is valid, or null.</param>
/// <param name="MoveTo">Where to walk when not acting this tick, or null to stand still.</param>
/// <param name="StopWithinMeters">Stop walking once this close to <paramref name="MoveTo"/>; zero walks all the way.</param>
/// <remarks>
/// The encounter tries the action first; a combatant that acts this tick does not move. Otherwise it walks towards
/// <paramref name="MoveTo"/> at its speed.
/// </remarks>
public readonly record struct CombatDecision(CombatAction? Action, Point2D? MoveTo = null, double StopWithinMeters = 0)
{
    /// <summary>Do nothing this tick.</summary>
    public static readonly CombatDecision None = default;

    /// <summary>Use an action (when ready).</summary>
    /// <param name="action">The action.</param>
    /// <returns>The decision.</returns>
    public static CombatDecision Act(CombatAction action) => new(action);

    /// <summary>Walk towards a point.</summary>
    /// <param name="to">Where to walk.</param>
    /// <param name="stopWithinMeters">Stop once this close.</param>
    /// <returns>The decision.</returns>
    public static CombatDecision Move(Point2D to, double stopWithinMeters = 0) => new(null, to, stopWithinMeters);
}
