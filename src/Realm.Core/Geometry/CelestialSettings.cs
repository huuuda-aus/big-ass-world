namespace Realm.Core.Geometry;

/// <summary>The fixed facts of the sky: where on the planet the player's world lies and how the moon moves. There are no seasons, so the sun follows the same path every day.</summary>
/// <remarks>Thread-safety: immutable.</remarks>
public sealed record CelestialSettings
{
    /// <summary>Latitude of the world in degrees north (0 to 89): the sun at noon stands this far below straight overhead, due south.</summary>
    public double LatitudeDegrees { get; init; } = 40;

    /// <summary>Game days from one new moon to the next. Real moons take 29.5 days; a game day is short, so this is shorter.</summary>
    public double MoonCycleDays { get; init; } = 12;

    /// <summary>How far the moon's path wanders north and south of the sun's path, in degrees.</summary>
    public double MoonInclinationDegrees { get; init; } = 5;

    /// <summary>Game days the moon takes to swing through that wander.</summary>
    public double MoonInclinationPeriodDays { get; init; } = 9;

    /// <summary>Moon phase on day 0, as a fraction of the cycle: 0 is new, 0.5 is full.</summary>
    public double MoonPhaseAtStart { get; init; } = 0.4;
}
