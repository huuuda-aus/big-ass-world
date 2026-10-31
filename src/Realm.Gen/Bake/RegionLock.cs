namespace Realm.Gen.Bake;

/// <summary>One region a hand edit has protected from being overwritten by a later bake (CLAUDE.md section 4.4).</summary>
/// <remarks>Thread-safety: immutable.</remarks>
public sealed record RegionLock
{
    /// <summary>Region coordinate, east.</summary>
    public required int RegionX { get; init; }

    /// <summary>Region coordinate, south.</summary>
    public required int RegionZ { get; init; }

    /// <summary>Free-text note (who or what edited it, and when); not read by any logic.</summary>
    public string Note { get; init; } = string.Empty;
}
