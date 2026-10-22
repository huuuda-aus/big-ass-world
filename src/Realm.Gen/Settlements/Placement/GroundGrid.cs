using System;
using Realm.Gen.Settlements.Roads;
using Realm.Gen.Terrain;

namespace Realm.Gen.Settlements.Placement;

/// <summary>
/// The terrain heights around one settlement on a regular grid, kept so a city can be built on a worker thread without the terrain
/// engine. It is sampled from the finished terrain (after roads) when the world is baked and stored next to the settlements.
/// </summary>
/// <remarks>Determinism: sampling is pure. Thread-safety: immutable after construction.</remarks>
public sealed class GroundGrid
{
    /// <summary>Distance between grid points, in metres.</summary>
    public const double DefaultSpacingMeters = 8;

    /// <summary>How far beyond the wall the grid reaches, in metres; farm buildings stand outside the wall.</summary>
    public const double MarginBeyondWallMeters = 150;

    /// <summary>Ground below this height is sea.</summary>
    private const double SeaLevelMeters = 0;

    private readonly float[] _heights;
    private readonly bool[] _water;

    /// <summary>Creates a grid.</summary>
    /// <param name="settlementId">ID of the settlement it belongs to.</param>
    /// <param name="originX">World X of the grid's first column, in metres.</param>
    /// <param name="originZ">World Z of the grid's first row, in metres.</param>
    /// <param name="spacingMeters">Distance between grid points.</param>
    /// <param name="side">Points per side.</param>
    /// <param name="heights">Heights in metres above sea level, row by row (Z) then column (X); copied.</param>
    /// <param name="water">Whether each grid point is under water (river, lake or sea), in the same order; copied. Null means dry everywhere.</param>
    public GroundGrid(string settlementId, double originX, double originZ, double spacingMeters, int side, float[] heights, bool[]? water = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(settlementId);
        ArgumentNullException.ThrowIfNull(heights);
        if (side < 2 || heights.Length != side * side || !(spacingMeters > 0) || (water is not null && water.Length != heights.Length))
        {
            throw new ArgumentException("A ground grid needs at least 2 x 2 points and a positive spacing.");
        }

        SettlementId = settlementId;
        OriginX = originX;
        OriginZ = originZ;
        SpacingMeters = spacingMeters;
        Side = side;
        _heights = (float[])heights.Clone();
        _water = water is null ? new bool[heights.Length] : (bool[])water.Clone();
    }

    /// <summary>ID of the settlement it belongs to.</summary>
    public string SettlementId { get; }

    /// <summary>World X of the first column.</summary>
    public double OriginX { get; }

    /// <summary>World Z of the first row.</summary>
    public double OriginZ { get; }

    /// <summary>Distance between grid points, in metres.</summary>
    public double SpacingMeters { get; }

    /// <summary>Points per side.</summary>
    public int Side { get; }

    /// <summary>The heights, row by row.</summary>
    public ReadOnlySpan<float> Heights => _heights;

    /// <summary>Whether each point is under water, row by row.</summary>
    public ReadOnlySpan<bool> Water => _water;

    /// <summary>Whether the ground at a world position is under water (to the accuracy of the grid spacing).</summary>
    /// <param name="x">World X in metres.</param>
    /// <param name="z">World Z in metres.</param>
    /// <returns><c>true</c> when the nearest grid point is river, lake or sea.</returns>
    public bool IsWaterAt(double x, double z)
    {
        int col = (int)Math.Round((x - OriginX) / SpacingMeters);
        int row = (int)Math.Round((z - OriginZ) / SpacingMeters);
        return col >= 0 && row >= 0 && col < Side && row < Side && _water[(row * Side) + col];
    }

    /// <summary>Samples the terrain around a settlement.</summary>
    /// <param name="field">The finished terrain.</param>
    /// <param name="settlement">The settlement.</param>
    /// <param name="spacingMeters">Distance between grid points.</param>
    /// <param name="rivers">Where the rivers and lakes are; the sea (ground below 0 m) counts as water too. Null for none.</param>
    /// <returns>The grid, centred on the settlement.</returns>
    public static GroundGrid Capture(HeightField field, Settlement settlement, double spacingMeters = DefaultSpacingMeters, RiverRaster? rivers = null)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(settlement);
        double reach = settlement.WallRadiusMeters + MarginBeyondWallMeters;
        int side = (int)Math.Ceiling(2 * reach / spacingMeters) + 1;
        double originX = settlement.X - (spacingMeters * (side - 1) / 2);
        double originZ = settlement.Z - (spacingMeters * (side - 1) / 2);
        var heights = new float[side * side];
        var water = new bool[side * side];
        for (int row = 0; row < side; row++)
        {
            for (int col = 0; col < side; col++)
            {
                double x = originX + (col * spacingMeters);
                double z = originZ + (row * spacingMeters);
                double height = field.Sample(x, z);
                heights[(row * side) + col] = (float)height;
                water[(row * side) + col] = height < SeaLevelMeters || (rivers is not null && (rivers.IsRiver(x, z) || rivers.IsLake(x, z)));
            }
        }

        return new GroundGrid(settlement.Id, originX, originZ, spacingMeters, side, heights, water);
    }

    /// <summary>Height of the ground at a world position, interpolated between grid points.</summary>
    /// <param name="x">World X in metres.</param>
    /// <param name="z">World Z in metres.</param>
    /// <returns>Height above sea level in metres. Outside the grid the nearest edge value is used.</returns>
    public double HeightAt(double x, double z)
    {
        double fx = Math.Clamp((x - OriginX) / SpacingMeters, 0, Side - 1);
        double fz = Math.Clamp((z - OriginZ) / SpacingMeters, 0, Side - 1);
        int x0 = Math.Min((int)fx, Side - 2);
        int z0 = Math.Min((int)fz, Side - 2);
        double tx = fx - x0;
        double tz = fz - z0;
        double h00 = _heights[(z0 * Side) + x0];
        double h10 = _heights[(z0 * Side) + x0 + 1];
        double h01 = _heights[((z0 + 1) * Side) + x0];
        double h11 = _heights[((z0 + 1) * Side) + x0 + 1];
        return (h00 * (1 - tx) * (1 - tz)) + (h10 * tx * (1 - tz)) + (h01 * (1 - tx) * tz) + (h11 * tx * tz);
    }
}
