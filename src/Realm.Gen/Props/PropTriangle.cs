using System.Numerics;

namespace Realm.Gen.Props;

/// <summary>One flat-coloured triangle of a prop model, wound counter-clockwise when seen from outside (right-handed, Y up).</summary>
/// <param name="A">First corner in metres.</param>
/// <param name="B">Second corner in metres.</param>
/// <param name="C">Third corner in metres.</param>
/// <param name="Colour">Linear RGB, each channel 0 to 1.</param>
public readonly record struct PropTriangle(Vector3 A, Vector3 B, Vector3 C, Vector3 Colour);
