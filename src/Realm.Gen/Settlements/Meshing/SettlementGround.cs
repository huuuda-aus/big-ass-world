using System;
using Realm.Gen.Settlements.Placement;

namespace Realm.Gen.Settlements.Meshing;

/// <summary>A settlement's terrain seen from the city plan: plan coordinates in, height above the settlement's origin out.</summary>
/// <remarks>Thread-safety: immutable; safe to share between worker threads.</remarks>
public sealed class SettlementGround : IGroundSampler
{
    private readonly Settlement _settlement;
    private readonly GroundGrid _grid;

    /// <summary>Creates the sampler.</summary>
    /// <param name="settlement">The settlement (its position, rotation and ground height).</param>
    /// <param name="grid">The sampled terrain around it.</param>
    public SettlementGround(Settlement settlement, GroundGrid grid)
    {
        _settlement = settlement ?? throw new ArgumentNullException(nameof(settlement));
        _grid = grid ?? throw new ArgumentNullException(nameof(grid));
    }

    /// <inheritdoc />
    public double HeightAt(double x, double y)
    {
        (double wx, double wz) = _settlement.ToWorld(new Geometry.Vec2(x, y));
        return _grid.HeightAt(wx, wz) - _settlement.GroundHeightMeters;
    }
}
