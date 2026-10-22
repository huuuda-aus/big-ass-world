using System;
using System.Collections.Generic;
using System.Linq;
using Realm.Data.Content;
using Realm.Gen.Settlements.Model;

namespace Realm.Gen.Settlements.Placement;

/// <summary>Turns the <c>service</c> content records into the list a settlement of a tier must contain.</summary>
/// <remarks>Determinism: output is in ordinal ID order. Thread-safety: pure.</remarks>
public static class ServiceCatalog
{
    /// <summary>The services a settlement of a tier gets.</summary>
    /// <param name="database">The loaded content.</param>
    /// <param name="tier">The settlement tier.</param>
    /// <returns>One spec per service with a non-zero count for the tier, in ID order.</returns>
    public static IReadOnlyList<ServiceSpec> For(ContentDatabase database, SettlementTier tier)
    {
        ArgumentNullException.ThrowIfNull(database);
        string word = tier switch
        {
            SettlementTier.City => "city",
            SettlementTier.Town => "town",
            _ => "hamlet",
        };
        return database.Services.Values
            .Where(d => d.CountFor(word) > 0)
            .Select(d => ToSpec(d, d.CountFor(word)))
            .ToList();
    }

    /// <summary>Gives every settlement the services its tier requires.</summary>
    /// <param name="settlements">The placed settlements.</param>
    /// <param name="database">The loaded content.</param>
    /// <returns>Copies of the settlements with <see cref="Settlement.Services"/> set; the input is not changed.</returns>
    public static IReadOnlyList<Settlement> Attach(IReadOnlyList<Settlement> settlements, ContentDatabase database)
    {
        ArgumentNullException.ThrowIfNull(settlements);
        ArgumentNullException.ThrowIfNull(database);
        return settlements.Select(s => s with { Services = For(database, s.Tier) }).ToList();
    }

    /// <summary>Converts one content record.</summary>
    /// <param name="definition">The record.</param>
    /// <param name="count">How many the settlement gets.</param>
    /// <returns>The spec.</returns>
    public static ServiceSpec ToSpec(ServiceBuildingDefinition definition, int count)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return new ServiceSpec(
            definition.Id.ToString(),
            definition.Name,
            count,
            definition.Wards.Select(ParseWard).ToList(),
            ParseEnum<ServiceNear>(definition.Near),
            definition.OutsideWall,
            definition.MinAreaMeters2,
            definition.Foundation is null ? null : ParseEnum<FoundationStyle>(definition.Foundation),
            ParseEnum<RoomType>(definition.MainRoom));
    }

    private static WardType ParseWard(string word) => ParseEnum<WardType>(word);

    // The vocabulary uses lower_snake_case words; the enums use PascalCase.
    private static T ParseEnum<T>(string word)
        where T : struct, Enum
    {
        string pascal = string.Concat(word.Split('_').Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
        return Enum.Parse<T>(pascal);
    }
}
