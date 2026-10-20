using System;
using System.Collections.Generic;
using System.Numerics;

namespace Realm.Gen.Props;

/// <summary>
/// A simple low-poly model made of flat-coloured triangles. Units are metres, Y is up, the origin is at the centre
/// of the footprint on the ground; anything below Y = 0 is foundation or root that sinks into the terrain.
/// </summary>
/// <remarks>Determinism: built by pure functions in <see cref="HouseModels"/> and <see cref="TreeModels"/>. Immutable.</remarks>
public sealed class PropMesh
{
    /// <summary>Creates a mesh.</summary>
    /// <param name="name">Short identifier such as <c>house.one_storey</c>.</param>
    /// <param name="triangles">The triangles; must not be empty.</param>
    /// <exception cref="ArgumentException">No triangles.</exception>
    public PropMesh(string name, IReadOnlyList<PropTriangle> triangles)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(triangles);
        if (triangles.Count == 0)
        {
            throw new ArgumentException("A prop needs at least one triangle.", nameof(triangles));
        }

        Name = name;
        Triangles = triangles;
        Vector3 min = new(float.MaxValue);
        Vector3 max = new(float.MinValue);
        foreach (PropTriangle t in triangles)
        {
            foreach (Vector3 v in new[] { t.A, t.B, t.C })
            {
                min = Vector3.Min(min, v);
                max = Vector3.Max(max, v);
            }
        }

        Min = min;
        Max = max;
    }

    /// <summary>Short identifier.</summary>
    public string Name { get; }

    /// <summary>The triangles.</summary>
    public IReadOnlyList<PropTriangle> Triangles { get; }

    /// <summary>Smallest corner of the bounding box.</summary>
    public Vector3 Min { get; }

    /// <summary>Largest corner of the bounding box.</summary>
    public Vector3 Max { get; }

    /// <summary>Height above the ground plane (Y = 0) of the highest point.</summary>
    public float HeightMeters => Max.Y;

    /// <summary>
    /// Sum of the signed volumes of the solids the triangles enclose (divergence theorem). Positive when every solid
    /// is wound outward; overlapping solids count twice.
    /// </summary>
    /// <returns>Volume in cubic metres.</returns>
    public double SignedVolume()
    {
        double sum = 0;
        foreach (PropTriangle t in Triangles)
        {
            sum += Vector3.Dot(t.A, Vector3.Cross(t.B, t.C)) / 6.0;
        }

        return sum;
    }
}
