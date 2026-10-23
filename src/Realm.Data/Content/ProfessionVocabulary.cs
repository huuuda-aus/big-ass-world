using System;
using System.Collections.Generic;

namespace Realm.Data.Content;

/// <summary>The words a <c>profession</c> record may use.</summary>
/// <remarks>The simulation maps the activity words to its own enum; a test keeps the two lists in step.</remarks>
public static class ProfessionVocabulary
{
    /// <summary>Activities a day block may name.</summary>
    public static readonly IReadOnlyList<string> Activities = Array.AsReadOnly(new[] { "home", "work", "about", "inn", "errand", "patrol", "fields" });

    /// <summary>Kinds of workplace besides a service building.</summary>
    public static readonly IReadOnlyList<string> WorkplaceKinds = Array.AsReadOnly(new[] { "none", "stall", "gate_post", "square", "patrol", "fields" });

    /// <summary>Settlement tiers a share may be given for.</summary>
    public static readonly IReadOnlyList<string> Tiers = ServiceVocabulary.Tiers;

    /// <summary>Workplace kind of a profession that does not say.</summary>
    public const string DefaultWorkplaceKind = "none";

    /// <summary>Minutes in a day.</summary>
    public const int MinutesPerDay = 1440;
}
