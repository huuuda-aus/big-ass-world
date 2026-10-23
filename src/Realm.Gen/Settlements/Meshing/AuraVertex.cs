using System.Numerics;

namespace Realm.Gen.Settlements.Meshing;

/// <summary>One corner of a glow triangle: where it is, where it samples the round falloff texture, and how bright it is.</summary>
/// <param name="Position">Position in metres, in the frame the mesh was built for.</param>
/// <param name="Uv">Texture coordinate on the falloff disc (0.5, 0.5 is its centre).</param>
/// <param name="Colour">Linear RGB brightness added to the surface; three consecutive vertices share one colour.</param>
public readonly record struct AuraVertex(Vector3 Position, Vector2 Uv, Vector3 Colour);
