using System;
using System.Collections.Generic;
using Realm.Gen.Settlements.Generation;
using Realm.Gen.Settlements.Geometry;
using Realm.Gen.Settlements.Model;

namespace Realm.Gen.Settlements.Placement;

/// <summary>
/// Where one city stands in the world and what it is made from. The city itself is not stored: it is regenerated from
/// <see cref="CitySeed"/> and the options below, which always gives the same layout (see <see cref="ToCityParams()"/>).
/// </summary>
/// <param name="Id">Stable identifier such as <c>settlement.city_1</c>.</param>
/// <param name="Name">Display name (a placeholder until names are designed).</param>
/// <param name="Tier">City or town.</param>
/// <param name="X">World X of the central square in metres.</param>
/// <param name="Z">World Z of the central square in metres.</param>
/// <param name="RotationRadians">Rotation of the city plan about its centre, counter-clockwise seen from above in plan coordinates.</param>
/// <param name="GroundHeightMeters">Height of the flattened ground under the city, in metres above sea level.</param>
/// <param name="WallRadiusMeters">Distance from the centre to the farthest wall corner.</param>
/// <param name="PadRadiusMeters">Radius of the terrain flattened flat under the city (wall radius plus a margin).</param>
/// <param name="CitySeed">Seed passed to <see cref="CityGenerator"/>.</param>
/// <param name="MaxBuildings">Upper limit on houses.</param>
/// <param name="Cathedral">Whether the ward beside the central square holds a cathedral.</param>
/// <param name="Centre">What stands in the central square.</param>
/// <param name="Services">The service buildings the city must contain (empty for none).</param>
/// <param name="Highland">Whether it stands in the mountains (steeper, higher ground is allowed).</param>
/// <param name="Layout">The shape of its plan: radial wards or a cross of streets.</param>
public sealed record Settlement(
    string Id,
    string Name,
    SettlementTier Tier,
    double X,
    double Z,
    double RotationRadians,
    double GroundHeightMeters,
    double WallRadiusMeters,
    double PadRadiusMeters,
    int CitySeed,
    int MaxBuildings,
    bool Cathedral,
    CentrepieceKind Centre,
    IReadOnlyList<ServiceSpec>? Services = null,
    bool Highland = false,
    SettlementLayout Layout = SettlementLayout.Radial)
{
    /// <summary>Whether a river runs through the city (it was placed as a river city, `settlement.river_N`).</summary>
    public bool IsRiverCity => Id.StartsWith("settlement.river_", StringComparison.Ordinal);

    /// <summary>
    /// What protects the settlement: a stone wall for a city, a wooden palisade for a town of more than
    /// <see cref="SettlementSettings.SmallSettlementMaxBuildings"/> houses, nothing for a smaller settlement or a hamlet.
    /// </summary>
    public Fortification Fortification => Tier switch
    {
        SettlementTier.City => Fortification.Stone,
        SettlementTier.Town when MaxBuildings > SettlementSettings.SmallSettlementMaxBuildings => Fortification.Palisade,
        _ => Fortification.None,
    };

    /// <summary>The service buildings this settlement contains; never null.</summary>
    public IReadOnlyList<ServiceSpec> ServiceList => Services ?? Array.Empty<ServiceSpec>();

    /// <summary>The generator options that reproduce this city exactly.</summary>
    /// <returns>Options for <see cref="CityGenerator.Generate"/>.</returns>
    public CityParams ToCityParams() => ToCityParams(null);

    /// <summary>The generator options that reproduce this city, with the water known (buildings in water are left out).</summary>
    /// <param name="water">Where the water is, in the city's plan frame; null for a dry site.</param>
    /// <returns>Options for <see cref="CityGenerator.Generate"/>.</returns>
    public CityParams ToCityParams(IWaterSampler? water) => new()
    {
        Water = water,
        Seed = CitySeed,
        MaxBuildings = MaxBuildings,
        Cathedral = Cathedral,
        Centre = Centre,
        Humble = Tier == SettlementTier.Hamlet,
        Layout = Layout,
        Parks = false,
        Services = ServiceList,
    };

    /// <summary>Converts a point of the city plan (plane x, y) to world coordinates.</summary>
    /// <param name="local">Point in the city's own plane, metres.</param>
    /// <returns>World X and Z in metres: the plan is rotated by <see cref="RotationRadians"/> and moved to the centre; plan y becomes world Z.</returns>
    public (double X, double Z) ToWorld(Vec2 local)
    {
        Vec2 rotated = local.Rotated(RotationRadians);
        return (X + rotated.X, Z + rotated.Y);
    }

    /// <summary>Converts a world position to the city's own plane.</summary>
    /// <param name="x">World X in metres.</param>
    /// <param name="z">World Z in metres.</param>
    /// <returns>The point in plan coordinates (the inverse of <see cref="ToWorld"/>).</returns>
    public Vec2 ToLocal(double x, double z) => new Vec2(x - X, z - Z).Rotated(-RotationRadians);

    /// <summary>Straight-line distance from the centre to a world position.</summary>
    /// <param name="x">World X in metres.</param>
    /// <param name="z">World Z in metres.</param>
    /// <returns>Distance in metres.</returns>
    public double DistanceTo(double x, double z) => Math.Sqrt(((x - X) * (x - X)) + ((z - Z) * (z - Z)));
}
