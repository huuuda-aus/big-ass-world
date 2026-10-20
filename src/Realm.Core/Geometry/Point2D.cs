namespace Realm.Core.Geometry;

/// <summary>A point on the ground plane in metres: X is east, Z is south (Godot's horizontal axes).</summary>
/// <param name="X">East coordinate in metres.</param>
/// <param name="Z">South coordinate in metres.</param>
/// <remarks>Thread-safety: immutable.</remarks>
public readonly record struct Point2D(double X, double Z);
