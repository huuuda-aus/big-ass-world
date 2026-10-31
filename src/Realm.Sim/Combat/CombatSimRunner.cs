using System;
using System.Collections.Generic;

namespace Realm.Sim.Combat;

/// <summary>
/// Runs the same fight many times with different encounter numbers and summarises the results: the headless balance
/// check behind <c>realm combat-sim</c> (ADR 0029).
/// </summary>
/// <remarks>Determinism: fight <c>i</c> is built with encounter number <c>i</c>, so the summary depends only on the builder and the count.</remarks>
public static class CombatSimRunner
{
    private const double P90 = 0.9;
    private const double Percent = 100.0;

    /// <summary>Runs <paramref name="fights"/> fights.</summary>
    /// <param name="build">Builds fight number <c>i</c>; it should pass <c>i</c> as the encounter number.</param>
    /// <param name="fights">How many fights; must be positive.</param>
    /// <param name="maxTicks">Tick limit per fight; must be positive.</param>
    /// <param name="focusTeam">The team whose wins are counted (the player's).</param>
    /// <returns>The summary.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="build"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="fights"/> or <paramref name="maxTicks"/> is not positive.</exception>
    public static CombatSimSummary Run(Func<long, CombatEncounter> build, int fights, long maxTicks, int focusTeam)
    {
        ArgumentNullException.ThrowIfNull(build);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fights);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTicks);

        var lengths = new List<long>(fights);
        int wins = 0, losses = 0, timeouts = 0;
        double healthLeft = 0;
        for (long i = 0; i < fights; i++)
        {
            CombatEncounter fight = build(i);
            fight.Run(maxTicks);
            lengths.Add(fight.Tick);

            if (fight.Phase != CombatPhase.Over)
            {
                timeouts++;
            }
            else if (fight.WinningTeam == focusTeam)
            {
                wins++;
                healthLeft += HealthPercent(fight, focusTeam);
            }
            else
            {
                losses++;
            }
        }

        lengths.Sort();
        double mean = 0;
        foreach (long l in lengths)
        {
            mean += l;
        }

        return new CombatSimSummary(
            fights,
            wins,
            losses,
            timeouts,
            mean / fights,
            lengths[(fights - 1) / 2],
            lengths[(int)Math.Ceiling(P90 * fights) - 1],
            wins == 0 ? 0 : healthLeft / wins);
    }

    private static double HealthPercent(CombatEncounter fight, int team)
    {
        long health = 0, max = 0;
        foreach (Combatant c in fight.Combatants)
        {
            if (c.Team == team)
            {
                health += c.Health;
                max += c.MaxHealth;
            }
        }

        return max == 0 ? 0 : Percent * health / max;
    }
}
