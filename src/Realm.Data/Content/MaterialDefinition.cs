using System.Collections.Generic;
using Realm.Core.Ids;

namespace Realm.Data.Content;

/// <summary>
/// A crafting material: a named bundle of tags and numeric properties (CLAUDE.md section 6.4).
/// Schema reference: docs/reference/content/material.md.
/// </summary>
/// <param name="Id">Stable identifier, for example <c>material.iron_ore</c>.</param>
/// <param name="Name">Display name.</param>
/// <param name="Tags">Tags such as <c>metal</c>; enumerates in sorted order.</param>
/// <param name="Properties">Numeric properties such as <c>hardness</c>; enumerates in sorted key order.</param>
public sealed record MaterialDefinition(
    ContentId Id,
    string Name,
    TagSet Tags,
    IReadOnlyDictionary<string, double> Properties);
