using System;

namespace Realm.Core.Ids;

/// <summary>
/// Stable string identifier of a content record, such as <c>material.iron_ore</c>.
/// Grammar: two or more dot-separated segments; each segment is lowercase ASCII, starts with a
/// letter and continues with letters, digits or underscores. The first segment is the content type.
/// </summary>
/// <remarks>
/// Determinism: comparison and ordering are ordinal, never culture-dependent.
/// Thread-safety: immutable.
/// </remarks>
public readonly struct ContentId : IEquatable<ContentId>, IComparable<ContentId>
{
    private readonly string? _value;

    private ContentId(string value) => _value = value;

    /// <summary>The full identifier text. Empty for a default-constructed value.</summary>
    public string Value => _value ?? string.Empty;

    /// <summary>The content type, the first segment (for example <c>material</c>).</summary>
    public string Type => Value.Length == 0 ? string.Empty : Value[..Value.IndexOf('.')];

    /// <summary>Parses an identifier.</summary>
    /// <param name="text">Identifier text.</param>
    /// <returns>The identifier.</returns>
    /// <exception cref="FormatException"><paramref name="text"/> violates the grammar.</exception>
    public static ContentId Parse(string text)
    {
        if (!TryParse(text, out ContentId id, out string? error))
        {
            throw new FormatException(error);
        }

        return id;
    }

    /// <summary>Tries to parse an identifier.</summary>
    /// <param name="text">Identifier text; may be null.</param>
    /// <param name="id">The identifier when parsing succeeds.</param>
    /// <returns><c>true</c> when <paramref name="text"/> is a valid identifier.</returns>
    public static bool TryParse(string? text, out ContentId id) => TryParse(text, out id, out _);

    /// <summary>Tries to parse an identifier and explains a failure.</summary>
    /// <param name="text">Identifier text; may be null.</param>
    /// <param name="id">The identifier when parsing succeeds.</param>
    /// <param name="error">Human-readable reason when parsing fails; otherwise null.</param>
    /// <returns><c>true</c> when <paramref name="text"/> is a valid identifier.</returns>
    public static bool TryParse(string? text, out ContentId id, out string? error)
    {
        id = default;

        if (string.IsNullOrEmpty(text))
        {
            error = "Content ID is empty.";
            return false;
        }

        string[] segments = text.Split('.');
        if (segments.Length < 2)
        {
            error = $"Content ID '{text}' needs a type and a name, like 'material.iron_ore'.";
            return false;
        }

        foreach (string segment in segments)
        {
            if (!IsValidSegment(segment))
            {
                error = $"Content ID '{text}' has an invalid segment '{segment}'. " +
                        "Segments are lowercase letters, digits and underscores, starting with a letter.";
                return false;
            }
        }

        id = new ContentId(text);
        error = null;
        return true;
    }

    /// <summary>Checks whether text is a valid single segment. Also the grammar of a tag.</summary>
    /// <param name="segment">Text to check; may be null.</param>
    /// <returns><c>true</c> when the text is a non-empty lowercase identifier starting with a letter.</returns>
    public static bool IsValidSegment(string? segment)
    {
        if (string.IsNullOrEmpty(segment) || segment[0] < 'a' || segment[0] > 'z')
        {
            return false;
        }

        foreach (char c in segment)
        {
            bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_';
            if (!ok)
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public bool Equals(ContentId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ContentId other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    /// <inheritdoc />
    public int CompareTo(ContentId other) => string.CompareOrdinal(Value, other.Value);

    /// <inheritdoc />
    public override string ToString() => Value;

    /// <summary>Equality operator.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns><c>true</c> when equal.</returns>
    public static bool operator ==(ContentId left, ContentId right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns><c>true</c> when not equal.</returns>
    public static bool operator !=(ContentId left, ContentId right) => !left.Equals(right);
}
