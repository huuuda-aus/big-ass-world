namespace Realm.Sim.Combat;

/// <summary>Aggregate results of many simulated fights, from <see cref="CombatSimRunner"/>.</summary>
/// <param name="Fights">Fights run.</param>
/// <param name="Wins">Fights the focus team won.</param>
/// <param name="Losses">Fights another team won, or nobody survived.</param>
/// <param name="Timeouts">Fights still running when the tick limit was reached.</param>
/// <param name="MeanTicks">Mean fight length in ticks, over all fights.</param>
/// <param name="MedianTicks">Median fight length in ticks.</param>
/// <param name="P90Ticks">90th-percentile fight length in ticks.</param>
/// <param name="MeanHealthLeftPercent">Focus team's remaining health as a percent of its maximum, averaged over its wins (0 without wins).</param>
public sealed record CombatSimSummary(
    int Fights,
    int Wins,
    int Losses,
    int Timeouts,
    double MeanTicks,
    long MedianTicks,
    long P90Ticks,
    double MeanHealthLeftPercent)
{
    /// <summary>Wins as a fraction of fights, 0 to 1.</summary>
    public double WinRate => Fights == 0 ? 0 : (double)Wins / Fights;
}
