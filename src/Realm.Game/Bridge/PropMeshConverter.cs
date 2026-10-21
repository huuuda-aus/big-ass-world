using System;
using Godot;
using Realm.Gen.Props;

namespace Realm.Game.Bridge;

/// <summary>Turns Godot-free <see cref="PropMesh"/> models into Godot meshes and collision shapes (the one place that converts them).</summary>
/// <remarks>Both methods only create new, unshared Godot resources, so they are safe on worker threads.</remarks>
public static class PropMeshConverter
{
    /// <summary>Builds a flat-shaded, vertex-coloured mesh.</summary>
    /// <param name="prop">The model.</param>
    /// <returns>The mesh. Godot treats clockwise triangles as front faces, so each triangle is emitted reversed.</returns>
    public static ArrayMesh ToArrayMesh(PropMesh prop)
    {
        ArgumentNullException.ThrowIfNull(prop);
        int count = prop.Triangles.Count * 3;
        var vertices = new Vector3[count];
        var normals = new Vector3[count];
        var colours = new Color[count];
        int i = 0;
        foreach (PropTriangle t in prop.Triangles)
        {
            var a = new Vector3(t.A.X, t.A.Y, t.A.Z);
            var b = new Vector3(t.B.X, t.B.Y, t.B.Z);
            var c = new Vector3(t.C.X, t.C.Y, t.C.Z);
            Vector3 normal = (b - a).Cross(c - a).Normalized();
            var colour = new Color(t.Colour.X, t.Colour.Y, t.Colour.Z);
            // Reverse the winding: our meshes are counter-clockwise outward, Godot's front faces are clockwise.
            vertices[i] = a;
            vertices[i + 1] = c;
            vertices[i + 2] = b;
            for (int k = 0; k < 3; k++)
            {
                normals[i + k] = normal;
                colours[i + k] = colour;
            }

            i += 3;
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Color] = colours;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    /// <summary>Builds a triangle-mesh collision shape that collides from both sides.</summary>
    /// <param name="prop">The model.</param>
    /// <returns>The shape.</returns>
    public static ConcavePolygonShape3D ToCollisionShape(PropMesh prop)
    {
        ArgumentNullException.ThrowIfNull(prop);
        var faces = new Vector3[prop.Triangles.Count * 3];
        int i = 0;
        foreach (PropTriangle t in prop.Triangles)
        {
            faces[i++] = new Vector3(t.A.X, t.A.Y, t.A.Z);
            faces[i++] = new Vector3(t.B.X, t.B.Y, t.B.Z);
            faces[i++] = new Vector3(t.C.X, t.C.Y, t.C.Z);
        }

        return new ConcavePolygonShape3D { Data = faces, BackfaceCollision = true };
    }
}
