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
    private const double MinutesPerDay = 1440;

    /// <summary>The character's name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The profession ID (for example <c>profession.baker</c>); empty for a plain role.</summary>
    public string Profession { get; init; } = string.Empty;

    /// <summary>The blocks of the day; when null the character follows the simple behaviour of its <see cref="Role"/>.</summary>
    public ScheduleBlock[]? Schedule { get; init; }

    /// <summary>Minutes this character's day runs later (or earlier, if negative) than its profession's, so people do not all move at once.</summary>
    public double ScheduleShiftMinutes { get; init; }

    /// <summary>Graph node in front of the door of the building it works in; -1 when it works elsewhere.</summary>
    public int WorkNode { get; init; } = -1;

    /// <summary>A place in the open where it works (a stall, a post), or null.</summary>
    public (double X, double Z)? WorkSpot { get; init; }

    /// <summary>Graph nodes of its patrol, in order.</summary>
    public int[] PatrolNodes { get; init; } = Array.Empty<int>();

    /// <summary>Graph node in front of the inn it goes to; -1 for none.</summary>
    public int InnNode { get; init; } = -1;

    /// <summary>Graph node outside the gate it goes through to the fields; -1 for none.</summary>
    public int FieldsNode { get; init; } = -1;

    /// <summary>Walking speed relative to the crowd's, 0.7 (slow) to 1.3 (brisk).</summary>
    public double SpeedFactor { get; init; } = 1;

    /// <summary>How far to the right of the line of the walk the character keeps, in metres (people pass each other on their right).</summary>
    public double LaneOffsetMeters { get; init; }

    /// <summary>Whether the character takes shelter when it rains; guards and watchmen stay at their posts.</summary>
    public bool ShelterFromRain { get; init; } = true;

    /// <summary>Whether the character challenges the player who comes close.</summary>
    public bool Challenges { get; init; }

    /// <summary>What the character is scheduled to do at a minute of the day.</summary>
    /// <param name="minuteOfDay">Minute of the day, 0 to 1,440 (before the character's own shift).</param>
    /// <param name="minutesUntilChange">Receives the minutes until the activity changes.</param>
    /// <returns>The activity; minutes no block covers are spent at home.</returns>
    public ScheduleActivity ScheduledAt(double minuteOfDay, out double minutesUntilChange)
    {
        double m = (((minuteOfDay - ScheduleShiftMinutes) % MinutesPerDay) + MinutesPerDay) % MinutesPerDay;
        ScheduleActivity activity = ScheduleActivity.Home;
        double end = double.MaxValue;
        if (Schedule is not null)
        {
            foreach (ScheduleBlock b in Schedule)
            {
                bool inside = b.ToMinute > b.FromMinute ? m >= b.FromMinute && m < b.ToMinute : m >= b.FromMinute || m < b.ToMinute;
                if (inside)
                {
                    activity = b.Activity;
                    end = ((b.ToMinute - m) % MinutesPerDay + MinutesPerDay) % MinutesPerDay;
                    if (end == 0)
                    {
                        end = MinutesPerDay;
                    }

                    minutesUntilChange = end;
                    return activity;
                }
            }

            // In a gap: at home until the next block starts.
            foreach (ScheduleBlock b in Schedule)
            {
                double until = ((b.FromMinute - m) % MinutesPerDay + MinutesPerDay) % MinutesPerDay;
                end = Math.Min(end, until == 0 ? MinutesPerDay : until);
            }
        }

        minutesUntilChange = end == double.MaxValue ? MinutesPerDay : end;
        return activity;
    }

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
