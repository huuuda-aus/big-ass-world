using System;
using Realm.Gen.Settlements.Generation;
using Realm.Gen.Settlements.Geometry;
using Realm.Gen.Settlements.Model;

namespace Realm.Gen.Settlements.Placement;

/// <summary>
/// Where one city stands in the world and what it is made from. The city itself is not stored: it is regenerated from
/// <see cref="CitySeed"/> and the options below, which always gives the same layout (see <see cref="ToCityParams"/>).
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
    CentrepieceKind Centre)
{
    /// <summary>The generator options that reproduce this city exactly.</summary>
    /// <returns>Options for <see cref="CityGenerator.Generate"/>.</returns>
    public CityParams ToCityParams() => new()
    {
        Seed = CitySeed,
        MaxBuildings = MaxBuildings,
        Cathedral = Cathedral,
        Centre = Centre,
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
