using System;

namespace Realm.Gen.Settlements.Geometry;

/// <summary>A 2D vector or point in double precision; units are metres in the city plane.</summary>
public readonly struct Vec2 : IEquatable<Vec2>
{
    /// <summary>X component.</summary>
    public readonly double X;
    /// <summary>Y component.</summary>
    public readonly double Y;

    /// <summary>Creates a vector.</summary>
    /// <param name="x">X component.</param>
    /// <param name="y">Y component.</param>
    public Vec2(double x, double y)
    {
        X = x;
        Y = y;
    }

    /// <summary>Length of the vector.</summary>
    public double Length => Math.Sqrt(X * X + Y * Y);

    /// <summary>Adds two vectors.</summary>
    /// <param name="a">First vector.</param>
    /// <param name="b">Second vector.</param>
    /// <returns>The sum.</returns>
    public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);
    /// <summary>Subtracts two vectors.</summary>
    /// <param name="a">First vector.</param>
    /// <param name="b">Vector to subtract.</param>
    /// <returns>The difference.</returns>
    public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
    /// <summary>Scales a vector.</summary>
    /// <param name="a">The vector.</param>
    /// <param name="s">The factor.</param>
    /// <returns>The scaled vector.</returns>
    public static Vec2 operator *(Vec2 a, double s) => new Vec2(a.X * s, a.Y * s);
    /// <summary>Negates a vector.</summary>
    /// <param name="a">The vector.</param>
    /// <returns>The opposite vector.</returns>
    public static Vec2 operator -(Vec2 a) => new Vec2(-a.X, -a.Y);

    /// <summary>Dot product.</summary>
    /// <param name="a">First vector.</param>
    /// <param name="b">Second vector.</param>
    /// <returns>The dot product.</returns>
    public static double Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;
    /// <summary>Z component of the 3D cross product (positive when <paramref name="b"/> is counter-clockwise from <paramref name="a"/>).</summary>
    /// <param name="a">First vector.</param>
    /// <param name="b">Second vector.</param>
    /// <returns>The 2D cross product.</returns>
    public static double Cross(Vec2 a, Vec2 b) => a.X * b.Y - a.Y * b.X;
    /// <summary>Distance between two points.</summary>
    /// <param name="a">First point.</param>
    /// <param name="b">Second point.</param>
    /// <returns>Distance in metres.</returns>
    public static double Distance(Vec2 a, Vec2 b) => (a - b).Length;
    /// <summary>Linear interpolation.</summary>
    /// <param name="a">Start point.</param>
    /// <param name="b">End point.</param>
    /// <param name="t">0 gives <paramref name="a"/>, 1 gives <paramref name="b"/>.</param>
    /// <returns>The interpolated point.</returns>
    public static Vec2 Lerp(Vec2 a, Vec2 b, double t) => a + (b - a) * t;

    /// <summary>The unit vector in the same direction.</summary>
    /// <returns>A unit vector, or zero for a (nearly) zero vector.</returns>
    public Vec2 Normalized()
    {
        double l = Length;
        return l < 1e-12 ? new Vec2(0, 0) : new Vec2(X / l, Y / l);
    }

    /// <summary>Rotates the vector counter-clockwise.</summary>
    /// <param name="radians">Angle in radians.</param>
    /// <returns>The rotated vector.</returns>
    public Vec2 Rotated(double radians)
    {
        double c = Math.Cos(radians), s = Math.Sin(radians);
        return new Vec2(X * c - Y * s, X * s + Y * c);
    }

    /// <summary>Exact component equality.</summary>
    /// <param name="other">The vector to compare with.</param>
    /// <returns><c>true</c> when both components are equal.</returns>
    public bool Equals(Vec2 other) => X == other.X && Y == other.Y;
    /// <summary>Exact component equality.</summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns><c>true</c> when <paramref name="obj"/> is an equal <see cref="Vec2"/>.</returns>
    public override bool Equals(object? obj) => obj is Vec2 v && Equals(v);
    /// <summary>Hash of both components.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => (X, Y).GetHashCode();
    /// <summary>Formats the vector for debugging.</summary>
    /// <returns>Text such as <c>(1.5, 2)</c>.</returns>
    public override string ToString() => $"({X:0.##}, {Y:0.##})";
}
