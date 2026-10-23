namespace Realm.Gen.Population;

/// <summary>Tuning of the crowd generator. Placeholder look-tuning values, not design.</summary>
/// <remarks>Thread-safety: immutable.</remarks>
public sealed record CrowdSettings
{
    /// <summary>Characters in a city.</summary>
    public int CityCount { get; init; } = 220;

    /// <summary>Characters in a town.</summary>
    public int TownCount { get; init; } = 40;

    /// <summary>Characters in a hamlet.</summary>
    public int HamletCount { get; init; } = 12;

    /// <summary>Share of the characters (after the guards) that hang about the square.</summary>
    public double LoiterShare { get; init; } = 0.55;

    /// <summary>Most share of the characters that may be guards.</summary>
    public double GuardShare { get; init; } = 0.1;

    /// <summary>Guards standing at each gate.</summary>
    public int GuardsPerGate { get; init; } = 2;

    /// <summary>Places each person out and about stands at in turn, spread over the whole settlement.</summary>
    public int SpotsPerLoiterer { get; init; } = 6;

    /// <summary>Buildings each walker visits besides its home.</summary>
    public int DestinationsPerWalker { get; init; } = 3;

    /// <summary>Average bedtime in minutes after midnight.</summary>
    public double BedtimeMinute { get; init; } = 22 * 60;

    /// <summary>Average waking time in minutes after midnight.</summary>
    public double WakeMinute { get; init; } = 6 * 60;

    /// <summary>How much bedtime and waking time vary from character to character, in minutes either way.</summary>
    public double SleepSpreadMinutes { get; init; } = 60;
}
