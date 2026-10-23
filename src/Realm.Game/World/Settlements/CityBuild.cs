using System.Collections.Generic;
using System;
using System.Linq;
using Godot;
using Realm.Game.Bridge;
using Realm.Gen.Props;
using Realm.Gen.Settlements.Generation;
using Realm.Gen.Settlements.Geometry;
using Realm.Gen.Settlements.Meshing;
using Realm.Data.Content;
using Realm.Gen.Population;
using Realm.Gen.Settlements.Navigation;
using Realm.Sim.Npcs;
using Realm.Gen.Settlements.Model;
using Realm.Gen.Settlements.Placement;

namespace Realm.Game.World.Settlements;

/// <summary>One building of a city as the game holds it: its plan data, interior and cheap exterior mesh.</summary>
/// <param name="Building">The generated building.</param>
/// <param name="Interior">Its interior (rooms, doors, windows, stairs), generated up front because the far mesh paints its openings.</param>
/// <param name="Far">The cheap exterior mesh.</param>
/// <param name="CentreX">Plan X of the footprint centre in the city's local frame.</param>
/// <param name="CentreY">Plan Y of the footprint centre in the city's local frame.</param>
/// <param name="Site">Where the floor sits on the ground and what holds it up; the building's meshes are placed at its <see cref="BuildingSite.BaseY"/>.</param>
public sealed record CityBuilding(Building Building, Interior Interior, ArrayMesh Far, double CentreX, double CentreY, BuildingSite Site)
{
    /// <summary>Glowing panes over the far mesh's painted windows (in the far mesh's frame), or null when no room keeps a lamp.</summary>
    public ArrayMesh? Glow { get; init; }

    /// <summary>The glow its lit windows throw on the street and the walls round them (in the city's frame), or null when it has none.</summary>
    public ArrayMesh? Aura { get; init; }
}

/// <summary>A bridge over water inside the city: its mesh and walkable collision.</summary>
/// <param name="Mesh">The visible bridge.</param>
/// <param name="Shape">Collision for the roadway, parapets and abutments.</param>
public sealed record CityBridgeMesh(ArrayMesh Mesh, ConcavePolygonShape3D Shape);

/// <summary>The meshes of a centrepiece as Godot resources.</summary>
public sealed record CityLandmark(
    ArrayMesh Solid, ConcavePolygonShape3D SolidShape, ArrayMesh? Water, ArrayMesh? Flames, Vector3? FireLight, Vector3? JetOrigin);

/// <summary>
/// Everything about a city that can be computed off the main thread: the generated plan, every building's interior and far mesh,
/// and the wall and landmark meshes. Created by <see cref="Create"/> on a worker thread and handed to a <see cref="CityInstance"/>.
/// </summary>
public sealed class CityBuild
{
    private CityBuild(Settlement settlement, City city, CityBuilding[] buildings, ArrayMesh? wall, ConcavePolygonShape3D? wallShape, CityLandmark? landmark, double landmarkBaseY, CityBridgeMesh[] bridges, NavGraphResult nav, IGroundSampler ground, OpenGround open, IReadOnlyList<NpcPlan> crowd, IReadOnlyDictionary<int, string> nodeLabels)
    {
        NodeLabels = nodeLabels;
        Open = open;
        CrowdPlans = crowd;
        Nav = nav;
        Ground = ground;
        Settlement = settlement;
        City = city;
        Buildings = buildings;
        Wall = wall;
        WallShape = wallShape;
        Landmark = landmark;
        LandmarkBaseY = landmarkBaseY;
        Bridges = bridges;
    }

    /// <summary>Where and what the city is.</summary>
    public Settlement Settlement { get; }

    /// <summary>The generated plan.</summary>
    public City City { get; }

    /// <summary>Every building inside the wall.</summary>
    public CityBuilding[] Buildings { get; }

    /// <summary>The wall and towers, or null.</summary>
    public ArrayMesh? Wall { get; }

    /// <summary>Collision for the wall, or null.</summary>
    public ConcavePolygonShape3D? WallShape { get; }

    /// <summary>The centrepiece, or null.</summary>
    public CityLandmark? Landmark { get; }

    /// <summary>The bridges where streets cross water inside the city (empty for a dry city).</summary>
    public CityBridgeMesh[] Bridges { get; }

    /// <summary>The walking graph of the settlement (plan frame) and anything in it that could not be connected.</summary>
    public NavGraphResult Nav { get; }

    /// <summary>Where a character on foot may stand (the walking graph's obstacles).</summary>
    public OpenGround Open { get; }

    /// <summary>What each graph node is called, for describing where a character is going.</summary>
    public IReadOnlyDictionary<int, string> NodeLabels { get; }

    /// <summary>The plans of the characters that live in the settlement (empty when none were asked for).</summary>
    public IReadOnlyList<NpcPlan> CrowdPlans { get; }

    /// <summary>The ground the city stands on, for placing things at the right height in the city's frame.</summary>
    public IGroundSampler Ground { get; }

    /// <summary>Height of the centrepiece's base above the city origin, in metres.</summary>
    public double LandmarkBaseY { get; }

    /// <summary>Generates a city and all its far meshes. Takes about a second or two; call it from a worker thread.</summary>
    /// <param name="settlement">The settlement to build.</param>
    /// <param name="terrain">The baked terrain around it; the city is built on level ground when null.</param>
    /// <param name="crowdCount">How many characters live in it; 0 for none.</param>
    /// <param name="professions">The professions of its people; none gives them the simple behaviour of a role.</param>
    /// <returns>The finished build.</returns>
    public static CityBuild Create(Settlement settlement, GroundGrid? terrain, int crowdCount = 0, IReadOnlyCollection<ProfessionDefinition>? professions = null)
    {
        ArgumentNullException.ThrowIfNull(settlement);
        IGroundSampler ground = terrain is null ? FlatGround.Instance : new SettlementGround(settlement, terrain);
        IWaterSampler? water = ground as IWaterSampler; // the baked ground grid also knows where the rivers and lakes are
        City city = CityGenerator.Generate(settlement.ToCityParams(water));

        var list = new System.Collections.Generic.List<CityBuilding>();
        foreach (Building building in city.AllBuildings)
        {
            Interior interior = InteriorGenerator.Generate(building, city.Seed);
            BuildingSite site = BuildingSite.Plan(building, ground);
            ArrayMesh far = PropMeshConverter.ToArrayMesh(BuildingMesher.Far(building, interior, site));
            Vec2 centre = building.Footprint.Centroid;
            PropMesh? glow = NightLights.Glow(building, interior);
            list.Add(new CityBuilding(building, interior, far, centre.X, centre.Y, site) { Glow = glow is null ? null : PropMeshConverter.ToArrayMesh(glow) });
        }

        // The glow of lit windows lies on the street and on neighbouring walls, so it needs every house placed first.
        var neighbours = list
            .Select(b => new AuraNeighbour(b.Building.Id, b.Building.Footprint, b.Site.BaseY, b.Interior.Floors.Count * b.Interior.FloorHeight))
            .ToList();
        for (int i = 0; i < list.Count; i++)
        {
            CityBuilding b = list[i];
            list[i] = b with { Aura = PropMeshConverter.ToAuraMesh(NightLights.Aura(b.Building, b.Interior, b.Site.BaseY, ground, neighbours)) };
        }

        CityMeshes meshes = CityMesher.Build(city, settlement.CitySeed, ground, settlement.Fortification, water);
        PropMesh? wall = meshes.Wall;
        CityBridgeMesh[] bridges = city.Bridges
            .Select(b => ArchBridgeMesher.Build(b, ground))
            .Select(m => new CityBridgeMesh(PropMeshConverter.ToArrayMesh(m), PropMeshConverter.ToCollisionShape(m)))
            .ToArray();
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

        NavGraphResult nav = NavGraphBuilder.Build(city, water, settlement.Fortification);
        var open = new OpenGround(city, water, settlement.Fortification);
        ulong crowdSeed = (ulong)(uint)settlement.CitySeed;
        IReadOnlyList<NpcPlan> crowd = professions is { Count: > 0 }
            ? CrowdGenerator.Generate(city, nav, open, settlement.Id, settlement.Tier, crowdCount, crowdSeed, professions)
            : CrowdGenerator.Generate(city, nav, open, settlement.Id, crowdCount, crowdSeed);

        return new CityBuild(
            settlement,
            city,
            list.ToArray(),
            wall is null ? null : PropMeshConverter.ToArrayMesh(wall),
            wall is null ? null : PropMeshConverter.ToCollisionShape(wall),
            landmark,
            meshes.LandmarkBaseY,
            bridges,
            nav,
            ground,
            open,
            crowd,
            CrowdGenerator.NodeLabels(city, nav));
    }
}
