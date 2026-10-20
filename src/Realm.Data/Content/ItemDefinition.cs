using Realm.Core.Ids;

namespace Realm.Data.Content;

/// <summary>
/// An item kind. Placeholder schema: only identity, tags and an optional material, enough to
/// exercise content references. Real item design is still to be decided.
/// Schema reference: docs/reference/content/item.md.
/// </summary>
/// <param name="Id">Stable identifier, for example <c>item.torch</c>.</param>
/// <param name="Name">Display name.</param>
/// <param name="Tags">Tags such as <c>light_source</c>; enumerates in sorted order.</param>
/// <param name="Material">The material the item is made of, or null.</param>
public sealed record ItemDefinition(
    ContentId Id,
    string Name,
    TagSet Tags,
    ContentId? Material);
