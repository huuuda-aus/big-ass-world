using System;
using Godot;
using System.Collections.Generic;
using Realm.Game.World.Settlements;
using Realm.Gen.Props;
using Realm.Gen.Settlements.Meshing;

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

    /// <summary>
    /// Builds a mesh like <see cref="ToArrayMesh"/>, but additionally carries UV (a dominant-axis planar
    /// projection of each triangle's own vertices, scaled by <see cref="BuildingMaterials.TileSize"/> so the
    /// chosen material tiles at a believable size) and, in UV2.x, which texture layer it samples: -1 for a
    /// triangle whose <see cref="PropMaterial"/> is <see cref="PropMaterial.None"/> (flat vertex colour, exactly
    /// as <see cref="ToArrayMesh"/> renders it), or the layer index otherwise. Pair with
    /// <see cref="BuildingMaterials.Textured"/>.
    /// </summary>
    /// <param name="prop">The model.</param>
    /// <returns>The mesh.</returns>
    public static ArrayMesh ToTexturedArrayMesh(PropMesh prop)
    {
        ArgumentNullException.ThrowIfNull(prop);
        int count = prop.Triangles.Count * 3;
        var vertices = new Vector3[count];
        var normals = new Vector3[count];
        var colours = new Color[count];
        var uvs = new Godot.Vector2[count];
        var uv2s = new Godot.Vector2[count];
        int i = 0;
        foreach (PropTriangle t in prop.Triangles)
        {
            var a = new Vector3(t.A.X, t.A.Y, t.A.Z);
            var b = new Vector3(t.B.X, t.B.Y, t.B.Z);
            var c = new Vector3(t.C.X, t.C.Y, t.C.Z);
            Vector3 normal = (b - a).Cross(c - a).Normalized();
            var colour = new Color(t.Colour.X, t.Colour.Y, t.Colour.Z);
            float layer = t.Material == PropMaterial.None ? -1f : (int)t.Material - 1;
            (float tileX, float tileY) = t.Material == PropMaterial.None ? (1f, 1f) : BuildingMaterials.TileSize((int)t.Material - 1);
            float ax = Mathf.Abs(normal.X), ay = Mathf.Abs(normal.Y), az = Mathf.Abs(normal.Z);

            // World-space planar UV, projected along whichever axis the triangle faces most: seamless tiling
            // textures need no authored UVs, and a building's own local origin gives each one a different phase.
            Godot.Vector2 Uv(Vector3 p) => ay >= ax && ay >= az
                ? new Godot.Vector2(p.X / tileX, p.Z / tileY)
                : ax >= az
                    ? new Godot.Vector2(p.Z / tileX, p.Y / tileY)
                    : new Godot.Vector2(p.X / tileX, p.Y / tileY);

            // Reverse the winding: our meshes are counter-clockwise outward, Godot's front faces are clockwise.
            vertices[i] = a;
            vertices[i + 1] = c;
            vertices[i + 2] = b;
            uvs[i] = Uv(a);
            uvs[i + 1] = Uv(c);
            uvs[i + 2] = Uv(b);
            for (int k = 0; k < 3; k++)
            {
                normals[i + k] = normal;
                colours[i + k] = colour;
                uv2s[i + k] = new Godot.Vector2(layer, 0f);
            }

            i += 3;
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Color] = colours;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.TexUV2] = uv2s;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    /// <summary>Builds the inside surfaces of a house for the interior shader: the colour's alpha is the room's sky fill, UV.x is 1 for a room with a lamp and UV.y is the height of its floor.</summary>
    /// <param name="rooms">The rooms' surfaces.</param>
    /// <returns>The mesh, or null when there are no surfaces.</returns>
    public static ArrayMesh? ToRoomMesh(IReadOnlyList<RoomSurface> rooms)
    {
        ArgumentNullException.ThrowIfNull(rooms);
        int triangles = 0;
        foreach (RoomSurface room in rooms)
        {
            triangles += room.Mesh.Triangles.Count;
        }

        if (triangles == 0)
        {
            return null;
        }

        var vertices = new Vector3[triangles * 3];
        var normals = new Vector3[triangles * 3];
        var colours = new Color[triangles * 3];
        var uvs = new Godot.Vector2[triangles * 3];
        int i = 0;
        foreach (RoomSurface room in rooms)
        {
            var uv = new Godot.Vector2(room.HasLamp ? 1f : 0f, (float)room.BaseY);
            foreach (PropTriangle t in room.Mesh.Triangles)
            {
                var a = new Vector3(t.A.X, t.A.Y, t.A.Z);
                var b = new Vector3(t.B.X, t.B.Y, t.B.Z);
                var c = new Vector3(t.C.X, t.C.Y, t.C.Z);
                Vector3 normal = (b - a).Cross(c - a).Normalized();
                var colour = new Color(t.Colour.X, t.Colour.Y, t.Colour.Z, (float)room.SkyFill);
                vertices[i] = a;
                vertices[i + 1] = c;
                vertices[i + 2] = b;
                for (int k = 0; k < 3; k++)
                {
                    normals[i + k] = normal;
                    colours[i + k] = colour;
                    uvs[i + k] = uv;
                }

                i += 3;
            }
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Color] = colours;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    /// <summary>Builds the glow of lit windows: vertex colour is the brightness, UV samples the falloff disc.</summary>
    /// <param name="vertices">Three vertices per triangle.</param>
    /// <returns>The mesh, or null when there are no triangles.</returns>
    public static ArrayMesh? ToAuraMesh(IReadOnlyList<AuraVertex> vertices)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        if (vertices.Count == 0)
        {
            return null;
        }

        var positions = new Vector3[vertices.Count];
        var colours = new Color[vertices.Count];
        var uvs = new Godot.Vector2[vertices.Count];
        for (int i = 0; i < vertices.Count; i++)
        {
            AuraVertex v = vertices[i];
            positions[i] = new Vector3(v.Position.X, v.Position.Y, v.Position.Z);
            colours[i] = new Color(v.Colour.X, v.Colour.Y, v.Colour.Z);
            uvs[i] = new Godot.Vector2(v.Uv.X, v.Uv.Y);
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = positions;
        arrays[(int)Mesh.ArrayType.Color] = colours;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
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
