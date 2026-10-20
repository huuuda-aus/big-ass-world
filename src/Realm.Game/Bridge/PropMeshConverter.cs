using System;
using Godot;
using Realm.Gen.Props;

namespace Realm.Game.Bridge;

/// <summary>Turns Godot-free <see cref="PropMesh"/> models into Godot meshes (the one place that converts them).</summary>
public static class PropMeshConverter
{
    /// <summary>Builds a flat-shaded, vertex-coloured mesh.</summary>
    /// <param name="prop">The model.</param>
    /// <returns>The mesh. Godot treats clockwise triangles as front faces, so each triangle is emitted reversed.</returns>
    public static ArrayMesh ToArrayMesh(PropMesh prop)
    {
        ArgumentNullException.ThrowIfNull(prop);
        var surface = new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);
        foreach (PropTriangle t in prop.Triangles)
        {
            surface.SetColor(new Color(t.Colour.X, t.Colour.Y, t.Colour.Z));
            foreach (System.Numerics.Vector3 v in new[] { t.A, t.C, t.B })
            {
                surface.AddVertex(new Vector3(v.X, v.Y, v.Z));
            }
        }

        surface.GenerateNormals();
        return surface.Commit();
    }
}
