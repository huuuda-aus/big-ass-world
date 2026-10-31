using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Realm.Core.Ids;
using Realm.Core.Validation;

namespace Realm.Data.Content;

/// <summary>Checks shared by the combat content parsers.</summary>
internal static class CombatParserSupport
{
    /// <summary>Validates a damage type word; returns the default type when absent.</summary>
    public static string DamageType(string? raw, string field, string code, string location, ValidationReport report)
    {
        if (raw is null)
        {
            return CombatVocabulary.DefaultDamageType;
        }

        if (!ContentId.IsValidSegment(raw))
        {
            report.Error(code, location, $"Field '{field}' has invalid damage type '{raw}'.", "Use lowercase letters, digits and underscores, for example 'fire'.");
        }

        return raw;
    }

    /// <summary>Checks a number lies in (min, max] (exclusive minimum) or [min, max] (inclusive), reporting otherwise.</summary>
    public static void Range(double value, double min, double max, bool minExclusive, string field, string code, string location, ValidationReport report)
    {
        bool tooLow = minExclusive ? value <= min : value < min;
        if (!double.IsFinite(value) || tooLow || value > max)
        {
            string lower = minExclusive ? $"above {min}" : $"at least {min}";
            report.Error(code, location, $"Field '{field}' must be {lower} and at most {max}, not {value}.");
        }
    }

    /// <summary>Validates a resistance map: valid damage-type words, percent 0 to 100; sorted by type.</summary>
    public static IReadOnlyDictionary<string, int> Resistances(Dictionary<string, int>? raw, string location, ValidationReport report)
    {
        var sorted = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach ((string type, int percent) in raw ?? new Dictionary<string, int>())
        {
            if (!ContentId.IsValidSegment(type))
            {
                report.Error(ValidatorCodes.InvalidCreatureField, location, $"Resistance has invalid damage type '{type}'.", "Use lowercase letters, digits and underscores.");
            }
            else if (percent is < 0 or > CombatVocabulary.PercentScale)
            {
                report.Error(ValidatorCodes.InvalidCreatureField, location, $"Resistance to '{type}' must be 0 to 100, not {percent}.");
            }
            else
            {
                sorted[type] = percent;
            }
        }

        return new ReadOnlyDictionary<string, int>(sorted);
    }
}
