using System;
using System.Collections.Generic;

namespace Realm.Data.Content;

/// <summary>The fixed words and limits the combat content types (<c>attack</c>, <c>condition</c>, <c>creature</c>) use.</summary>
/// <remarks>The simulation maps each behaviour word to an AI brain; a test keeps the two lists in step.</remarks>
public static class CombatVocabulary
{
    /// <summary>AI behaviour profiles a creature may name.</summary>
    public static readonly IReadOnlyList<string> Behaviours = Array.AsReadOnly(new[] { "nearest_enemy" });

    /// <summary>Damage type of an attack or pulse that does not say.</summary>
    public const string DefaultDamageType = "physical";

    /// <summary>Longest recovery an attack may have, in seconds.</summary>
    public const double MaxRecoverySeconds = 60;

    /// <summary>Longest attack range, in metres.</summary>
    public const double MaxRangeMeters = 1000;

    /// <summary>Largest area radius, in metres.</summary>
    public const double MaxAreaRadiusMeters = 100;

    /// <summary>Largest action-point cost.</summary>
    public const int MaxActionPointCost = 100;

    /// <summary>Longest condition, in seconds (one hour).</summary>
    public const double MaxDurationSeconds = 3600;

    /// <summary>Largest health, damage per pulse, attack rating or defence magnitude.</summary>
    public const int MaxStat = 1_000_000;

    /// <summary>Percent values (chances, resistances) are out of this.</summary>
    public const int PercentScale = 100;
}
