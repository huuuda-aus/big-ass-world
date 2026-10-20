using System;

namespace Realm.Sim.Entities;

/// <summary>
/// Identifies one entity. Values are assigned in increasing order and never reused, so an ID kept in a
/// save or by another entity can never silently point at a different entity later.
/// </summary>
/// <remarks>Determinism: ordering is numeric. Thread-safety: immutable.</remarks>
public readonly struct EntityId : IEquatable<EntityId>, IComparable<EntityId>
{
    /// <summary>The value meaning "no entity". Also what <c>default</c> gives.</summary>
    public static readonly EntityId None = default;

    /// <summary>Creates an ID from its numeric value.</summary>
    /// <param name="value">Numeric value; positive for real entities.</param>
    public EntityId(long value) => Value = value;

    /// <summary>The numeric value. 0 means <see cref="None"/>.</summary>
    public long Value { get; }

    /// <summary>True when this is not <see cref="None"/>.</summary>
    public bool IsValid => Value > 0;

    /// <inheritdoc />
    public bool Equals(EntityId other) => Value == other.Value;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is EntityId other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => Value.GetHashCode();

    /// <inheritdoc />
    public int CompareTo(EntityId other) => Value.CompareTo(other.Value);

    /// <summary>Formats as <c>#42</c>.</summary>
    /// <returns>A short description.</returns>
    public override string ToString() => "#" + Value;

    /// <summary>Less-than operator; entities created earlier compare lower.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns><c>true</c> when <paramref name="left"/> is lower.</returns>
    public static bool operator <(EntityId left, EntityId right) => left.CompareTo(right) < 0;

    /// <summary>Greater-than operator.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns><c>true</c> when <paramref name="left"/> is higher.</returns>
    public static bool operator >(EntityId left, EntityId right) => left.CompareTo(right) > 0;

    /// <summary>Equality operator.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns><c>true</c> when equal.</returns>
    public static bool operator ==(EntityId left, EntityId right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns><c>true</c> when not equal.</returns>
    public static bool operator !=(EntityId left, EntityId right) => !left.Equals(right);
}
