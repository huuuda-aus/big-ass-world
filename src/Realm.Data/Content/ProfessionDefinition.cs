using System.Collections.Generic;
using Realm.Core.Ids;

namespace Realm.Data.Content;

/// <summary>One block of a profession's day: from a minute of the day to another, doing an activity. A block that ends before it starts wraps past midnight.</summary>
/// <param name="FromMinute">Minute of the day the block starts, 0 up to 1,440.</param>
/// <param name="ToMinute">Minute of the day the block ends.</param>
/// <param name="Activity">One of <see cref="ProfessionVocabulary.Activities"/>.</param>
public readonly record struct DayBlock(int FromMinute, int ToMinute, string Activity);

/// <summary>
/// What people of a trade do: where they work and how they spend the day. Schema reference: docs/reference/content/profession.md.
/// </summary>
/// <param name="Id">Stable identifier, for example <c>profession.baker</c>.</param>
/// <param name="Name">Display name.</param>
/// <param name="Tags">Tags such as <c>food</c>; enumerates in sorted order.</param>
/// <param name="WorkplaceKind">A kind from <see cref="ProfessionVocabulary.WorkplaceKinds"/>, or <c>service</c> when <paramref name="WorkplaceService"/> is set.</param>
/// <param name="WorkplaceService">The service building they work in (for example <c>service.baker</c>), or null.</param>
/// <param name="Shares">Share of a settlement's people of each tier (0 to 1), sorted by tier.</param>
/// <param name="Day">The blocks of the day; minutes not covered by a block are spent at home.</param>
public sealed record ProfessionDefinition(
    ContentId Id,
    string Name,
    TagSet Tags,
    string WorkplaceKind,
    ContentId? WorkplaceService,
    IReadOnlyDictionary<string, double> Shares,
    IReadOnlyList<DayBlock> Day)
{
    /// <summary>The share of a settlement of a tier.</summary>
    /// <param name="tier">The tier word.</param>
    /// <returns>The share; 0 when the tier is not listed.</returns>
    public double ShareFor(string tier) => Shares.TryGetValue(tier, out double s) ? s : 0;
}
