using System.Collections.Generic;
using Realm.Gen.Settlements.Geometry;

namespace Realm.Gen.Settlements.Model;

/// <summary>One Voronoi cell of the city plan: a ward inside the wall, or farmland outside it.</summary>
public sealed class Patch
{
    /// <summary>Index among all patches of the city.</summary>
    public int Index { get; }
    /// <summary>The Voronoi site the patch grew from.</summary>
    public Vec2 Site { get; }
    /// <summary>Outline of the patch.</summary>
    public ConvexPoly Shape { get; }
    /// <summary>Indices of neighbouring patches.</summary>
    public IReadOnlyList<int> Neighbors { get; }
    /// <summary>What kind of ward the patch is.</summary>
    public WardType Ward { get; set; } = WardType.Craftsmen;
    /// <summary>Buildings standing on the patch.</summary>
    public List<Building> Buildings { get; } = new List<Building>();

    /// <summary>Creates a patch.</summary>
    /// <param name="index">Index among all patches.</param>
    /// <param name="site">The Voronoi site the patch grew from.</param>
    /// <param name="shape">Outline.</param>
    /// <param name="neighbors">Indices of neighbouring patches.</param>
    public Patch(int index, Vec2 site, ConvexPoly shape, IReadOnlyList<int> neighbors)
    {
        Index = index;
        Site = site;
        Shape = shape;
        Neighbors = neighbors;
    }
}
