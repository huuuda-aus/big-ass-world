using System;

namespace Realm.Sim.Npcs;

/// <summary>
/// Everything that makes one character different: its role, where it lives and goes, where it hangs about, and when it sleeps. Plans are
/// produced by the population generator and never change; the character's changing state lives in the <see cref="Crowd"/>.
/// </summary>
/// <param name="Id">Index of the character in its settlement (stable).</param>
/// <param name="Role">How it spends the day.</param>
/// <param name="HomeNode">Graph node in front of its home's door; -1 for a guard.</param>
/// <param name="Destinations">Graph nodes in front of the doors of the buildings it visits.</param>
/// <param name="Spots">Places in the open (the square, a gate) where it stands about, in the graph's frame; a guard's single post.</param>
/// <param name="SleepMinute">Minute of the day it goes home to sleep, 0 to 1,440.</param>
/// <param name="WakeMinute">Minute of the day it wakes, 0 to 1,440.</param>
/// <param name="Seed">Seed of the character's own random choices.</param>
public sealed record NpcPlan(
    int Id,
    NpcRole Role,
    int HomeNode,
    int[] Destinations,
    (double X, double Z)[] Spots,
    double SleepMinute,
    double WakeMinute,
    ulong Seed)
{
    /// <summary>Whether a minute of the day lies in this character's sleeping hours.</summary>
    /// <param name="minuteOfDay">Minute of the day, 0 to 1,440.</param>
    /// <returns><c>true</c> between bedtime and waking.</returns>
    public bool IsAsleepAt(double minuteOfDay) =>
        SleepMinute >= WakeMinute
            ? minuteOfDay >= SleepMinute || minuteOfDay < WakeMinute
            : minuteOfDay >= SleepMinute && minuteOfDay < WakeMinute;

    /// <summary>Minutes before bedtime at which a character starts for home, so even a long walk ends by bedtime.</summary>
    public const double EveningMinutes = 90;

    /// <summary>Whether a character should be heading home, or at home, at a minute of the day: in the evening before bedtime, or asleep.</summary>
    /// <param name="minuteOfDay">Minute of the day, 0 to 1,440.</param>
    /// <returns><c>true</c> from <see cref="EveningMinutes"/> before bedtime until waking.</returns>
    public bool GoesHomeAt(double minuteOfDay)
    {
        double start = ((SleepMinute - EveningMinutes) % 1440 + 1440) % 1440;
        return start <= WakeMinute
            ? minuteOfDay >= start && minuteOfDay < WakeMinute
            : minuteOfDay >= start || minuteOfDay < WakeMinute;
    }
}
