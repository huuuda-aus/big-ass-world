using System;
using Godot;
using Realm.Game.Bridge;
using Realm.Gen.Props;
using Realm.Gen.Settlements.Generation;
using Realm.Gen.Settlements.Geometry;
using Realm.Gen.Settlements.Meshing;
using Realm.Gen.Settlements.Model;
using Realm.Gen.Settlements.Placement;

namespace Realm.Game.World.Settlements;

/// <summary>One building of a city as the game holds it: its plan data, interior and cheap exterior mesh.</summary>
/// <param name="Building">The generated building.</param>
/// <param name="Interior">Its interior (rooms, doors, windows, stairs), generated up front because the far mesh paints its openings.</param>
/// <param name="Far">The cheap exterior mesh.</param>
/// <param name="CentreX">Plan X of the footprint centre in the city's local frame.</param>
/// <param name="CentreY">Plan Y of the footprint centre in the city's local frame.</param>
public sealed record CityBuilding(Building Building, Interior Interior, ArrayMesh Far, double CentreX, double CentreY);

/// <summary>The meshes of a centrepiece as Godot resources.</summary>
public sealed record CityLandmark(
    ArrayMesh Solid, ConcavePolygonShape3D SolidShape, ArrayMesh? Water, ArrayMesh? Flames, Vector3? FireLight, Vector3? JetOrigin);

/// <summary>
/// Everything about a city that can be computed off the main thread: the generated plan, every building's interior and far mesh,
/// and the ground, wall and landmark meshes. Created by <see cref="Create"/> on a worker thread and handed to a <see cref="CityInstance"/>.
/// </summary>
public sealed class CityBuild
{
    private CityBuild(Settlement settlement, City city, CityBuilding[] buildings, ArrayMesh ground, ArrayMesh? wall, ConcavePolygonShape3D? wallShape, CityLandmark? landmark)
    {
        Settlement = settlement;
        City = city;
        Buildings = buildings;
        Ground = ground;
        Wall = wall;
        WallShape = wallShape;
        Landmark = landmark;
    }

    /// <summary>Where and what the city is.</summary>
    public Settlement Settlement { get; }

    /// <summary>The generated plan.</summary>
    public City City { get; }

    /// <summary>Every building inside the wall.</summary>
    public CityBuilding[] Buildings { get; }

    /// <summary>Ward ground and streets.</summary>
    public ArrayMesh Ground { get; }

    /// <summary>The wall and towers, or null.</summary>
    public ArrayMesh? Wall { get; }

    /// <summary>Collision for the wall, or null.</summary>
    public ConcavePolygonShape3D? WallShape { get; }

    /// <summary>The centrepiece, or null.</summary>
    public CityLandmark? Landmark { get; }

    /// <summary>Generates a city and all its far meshes. Takes about a second or two; call it from a worker thread.</summary>
    /// <param name="settlement">The settlement to build.</param>
    /// <returns>The finished build.</returns>
    public static CityBuild Create(Settlement settlement)
    {
        ArgumentNullException.ThrowIfNull(settlement);
        City city = CityGenerator.Generate(settlement.ToCityParams());

        var list = new System.Collections.Generic.List<CityBuilding>();
        foreach (Patch patch in city.CityPatches)
        {
            foreach (Building building in patch.Buildings)
            {
                Interior interior = InteriorGenerator.Generate(building, city.Seed);
                ArrayMesh far = PropMeshConverter.ToArrayMesh(BuildingMesher.Far(building, interior));
                Vec2 centre = building.Footprint.Centroid;
                list.Add(new CityBuilding(building, interior, far, centre.X, centre.Y));
            }
        }

        CityMeshes meshes = CityMesher.Build(city, settlement.CitySeed);
        PropMesh? wall = meshes.Wall;
        CityLandmark? landmark = null;
        if (meshes.Landmark is { } l)
        {
            landmark = new CityLandmark(
                PropMeshConverter.ToArrayMesh(l.Solid),
                PropMeshConverter.ToCollisionShape(l.Solid),
                l.Water is null ? null : PropMeshConverter.ToArrayMesh(l.Water),
                l.Flames is null ? null : PropMeshConverter.ToArrayMesh(l.Flames),
                l.FireLight is { } f ? new Vector3(f.X, f.Y, f.Z) : null,
                l.JetOrigin is { } j ? new Vector3(j.X, j.Y, j.Z) : null);
        }

        return new CityBuild(
            settlement,
            city,
            list.ToArray(),
            PropMeshConverter.ToArrayMesh(meshes.Ground),
            wall is null ? null : PropMeshConverter.ToArrayMesh(wall),
            wall is null ? null : PropMeshConverter.ToCollisionShape(wall),
            landmark);
    }
}
