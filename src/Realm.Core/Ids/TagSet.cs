using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Realm.Core.Ids;

/// <summary>
/// Immutable set of tags such as <c>metal</c> or <c>blessed</c>. Tags follow the
/// <see cref="ContentId.IsValidSegment"/> grammar.
/// </summary>
/// <remarks>
/// Determinism: enumeration is always in ordinal sorted order, so it is safe to use in generator output.
/// Thread-safety: immutable.
/// </remarks>
public sealed class TagSet : IReadOnlyCollection<string>
{
    /// <summary>The empty set.</summary>
    public static readonly TagSet Empty = new(Array.Empty<string>());

    private readonly string[] _sorted;

    private TagSet(string[] sorted) => _sorted = sorted;

    /// <summary>Number of tags.</summary>
    public int Count => _sorted.Length;

    /// <summary>Builds a set. Duplicates collapse into one.</summary>
    /// <param name="tags">Tags to include.</param>
    /// <returns>The set.</returns>
    /// <exception cref="FormatException">A tag violates the grammar.</exception>
    public static TagSet From(IEnumerable<string> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);

        string[] sorted = tags.Distinct(StringComparer.Ordinal).OrderBy(t => t, StringComparer.Ordinal).ToArray();
        foreach (string tag in sorted)
        {
            if (!ContentId.IsValidSegment(tag))
            {
                throw new FormatException(
                    $"Invalid tag '{tag}'. Tags are lowercase letters, digits and underscores, starting with a letter.");
            }
        }

        return sorted.Length == 0 ? Empty : new TagSet(sorted);
    }

    /// <summary>Checks membership.</summary>
    /// <param name="tag">Tag to look for.</param>
    /// <returns><c>true</c> when the set contains the tag.</returns>
    public bool Contains(string tag) => Array.BinarySearch(_sorted, tag, StringComparer.Ordinal) >= 0;

    /// <inheritdoc />
    public IEnumerator<string> GetEnumerator() => ((IEnumerable<string>)_sorted).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
