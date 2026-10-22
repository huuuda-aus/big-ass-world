using System.Collections.Generic;
using Realm.Gen.Settlements.Geometry;

namespace Realm.Gen.Settlements.Model;

/// <summary>A generated city: patches, wall, gates, streets and buildings in a flat 2D plane (metres, central square at the origin).</summary>
public sealed class City
{
    /// <summary>The seed the city was generated from.</summary>
    public int Seed { get; }
    /// <summary>All patches (Voronoi cells), inside and outside the wall; most outside are farmland.</summary>
    public IReadOnlyList<Patch> Patches { get; }

    /// <summary>Patches inside the walls.</summary>
    public IReadOnlyList<Patch> CityPatches { get; }

    /// <summary>Closed loop of wall vertices.</summary>
    public IReadOnlyList<Vec2> Wall { get; }

    /// <summary>Gate positions on the wall.</summary>
    public IReadOnlyList<Vec2> Gates { get; }
    /// <summary>Streets from the central square to the gates.</summary>
    public IReadOnlyList<Street> Streets { get; }

    /// <summary>The centrepiece of the central square, or null when it is left empty.</summary>
    public Landmark? Landmark { get; }

    /// <summary>Every building, inside and outside the wall, in patch order.</summary>
    public IEnumerable<Building> AllBuildings
    {
        get
        {
            foreach (Patch patch in Patches)
            {
                foreach (Building building in patch.Buildings)
                {
                    yield return building;
                }
            }
        }
    }

    /// <summary>Creates a city.</summary>
    /// <param name="seed">The generation seed.</param>
    /// <param name="patches">All patches, inside and outside the wall.</param>
    /// <param name="cityPatches">Patches inside the wall.</param>
    /// <param name="wall">Closed loop of wall vertices.</param>
    /// <param name="gates">Gate positions.</param>
    /// <param name="streets">Streets.</param>
    /// <param name="landmark">Centrepiece of the central square, or null.</param>
    public City(int seed, IReadOnlyList<Patch> patches, IReadOnlyList<Patch> cityPatches,
        IReadOnlyList<Vec2> wall, IReadOnlyList<Vec2> gates, IReadOnlyList<Street> streets,
        Landmark? landmark = null)
    {
        Landmark = landmark;
        Seed = seed;
        Patches = patches;
        CityPatches = cityPatches;
        Wall = wall;
        Gates = gates;
        Streets = streets;
    }
}
