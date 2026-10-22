using System;
using System.Collections.Generic;

namespace Realm.Data.Content;

/// <summary>The words a <c>service</c> record may use for tiers, wards, placement, foundations and rooms.</summary>
/// <remarks>The generator (<c>Realm.Gen.Settlements</c>) maps these strings to its own enums; a test keeps the two lists in step.</remarks>
public static class ServiceVocabulary
{
    /// <summary>Settlement tiers: <c>city</c> (including the mountain city), <c>town</c>, <c>hamlet</c>.</summary>
    public static readonly IReadOnlyList<string> Tiers = Array.AsReadOnly(new[] { "city", "town", "hamlet" });

    /// <summary>Wards inside the wall a service may stand in.</summary>
    public static readonly IReadOnlyList<string> Wards = Array.AsReadOnly(new[] { "market", "merchant", "craftsmen", "slum" });

    /// <summary>Where in a ward a service prefers to stand: near the central square, near a gate, near the wall, or anywhere.</summary>
    public static readonly IReadOnlyList<string> Near = Array.AsReadOnly(new[] { "any", "gate", "square", "wall" });

    /// <summary>Forced foundation styles.</summary>
    public static readonly IReadOnlyList<string> Foundations = Array.AsReadOnly(new[] { "stone", "wood" });

    /// <summary>Room types a service's main room may have.</summary>
    public static readonly IReadOnlyList<string> Rooms = Array.AsReadOnly(new[] { "hall", "kitchen", "living_room", "shop", "storage", "workshop" });

    /// <summary>The placement used when a record does not say.</summary>
    public const string DefaultNear = "any";

    /// <summary>The main room used when a record does not say.</summary>
    public const string DefaultMainRoom = "hall";

    /// <summary>The footprint area used when a record does not say, in square metres.</summary>
    public const double DefaultMinAreaMeters2 = 40;

    /// <summary>Most of one service a settlement can be asked for.</summary>
    public const int MaxCount = 8;
}
