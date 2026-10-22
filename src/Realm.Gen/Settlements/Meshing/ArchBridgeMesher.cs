using System;
using System.Numerics;
using Realm.Gen.Props;
using Realm.Gen.Settlements.Geometry;
using Realm.Gen.Settlements.Model;

namespace Realm.Gen.Settlements.Meshing;

/// <summary>
/// Builds a stone arch bridge for a street over a river inside a city: a roadway that rises to a crown in the middle, an arched
/// underside, stone parapets and an abutment at each end.
/// </summary>
/// <remarks>
/// Coordinates: the city's local frame (plan x is X, plan y is Z, height is Y in metres above the city origin), like the wall.
/// The roadway meets the ground at each end, so a walker steps straight on. Determinism: pure. Thread-safety: pure.
/// </remarks>
public static class ArchBridgeMesher
{
    /// <summary>Segments along the bridge; the arch is a smooth curve through this many pieces.</summary>
    public const int Segments = 24;

    /// <summary>Rise of the crown above the line between the ends, as a share of the span.</summary>
    public const double CrownShare = 0.10;

    private const double MinCrownMeters = 1.2;
    private const double MaxCrownMeters = 6.0;
    private const double EndThicknessMeters = 1.1;
    private const double ArchExtraMeters = 0.5;
    private const double ParapetHeight = 1.0;
    private const double ParapetThickness = 0.4;
    private const double AbutmentDepth = 3.0;
    private const double AbutmentLength = 2.0;
    private const double AbutmentTop = 0.3; // the abutment top lies this far below the roadway, inside the deck
    private const double AbutmentInset = 0.2;
    private const double EndLift = 0.05;

    private static readonly Vector3 Stone = SettlementPalette.Hex(0x8c887e);
    private static readonly Vector3 Soffit = SettlementPalette.Hex(0x6e6a62);
    private static readonly Vector3 Roadway = SettlementPalette.Hex(0x9a8f78);

    /// <summary>Height of the crown above the line between the two ends of a bridge, in metres.</summary>
    /// <param name="lengthMeters">Span from bank to bank.</param>
    /// <returns>A tenth of the span, between 1.2 and 6 m.</returns>
    public static double CrownMeters(double lengthMeters) => Math.Clamp(lengthMeters * CrownShare, MinCrownMeters, MaxCrownMeters);

    /// <summary>Height of the roadway above the city origin at a place along the bridge.</summary>
    /// <param name="bridge">The bridge.</param>
    /// <param name="ground">The ground under the city.</param>
    /// <param name="t">Position along the bridge: 0 at <see cref="CityBridge.From"/>, 1 at <see cref="CityBridge.To"/>.</param>
    /// <returns>Height in metres.</returns>
    public static double RoadwayHeight(CityBridge bridge, IGroundSampler ground, double t)
    {
        double start = ground.HeightAt(bridge.From.X, bridge.From.Y) + EndLift;
        double end = ground.HeightAt(bridge.To.X, bridge.To.Y) + EndLift;
        return start + ((end - start) * t) + (CrownMeters(bridge.LengthMeters) * 4 * t * (1 - t));
    }

    /// <summary>Builds the mesh of a bridge.</summary>
    /// <param name="bridge">The bridge.</param>
    /// <param name="ground">The ground under the city (the ends stand on it).</param>
    /// <returns>The mesh: roadway, arched underside, spandrel walls, parapets and abutments.</returns>
    public static PropMesh Build(CityBridge bridge, IGroundSampler ground)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(ground);
        var b = new PropMeshBuilder();
        double length = bridge.LengthMeters;
        Vec2 d = (bridge.To - bridge.From).Normalized();
        Vec2 n = new(-d.Y, d.X);
        double crown = CrownMeters(length);
        double archLift = crown + ArchExtraMeters;
        double half = (bridge.WidthMeters / 2) + ParapetThickness;
        double startHeight = ground.HeightAt(bridge.From.X, bridge.From.Y) + EndLift;
        double endHeight = ground.HeightAt(bridge.To.X, bridge.To.Y) + EndLift;

        double Top(double t) => startHeight + ((endHeight - startHeight) * t) + (crown * 4 * t * (1 - t));
        double Under(double t) => startHeight + ((endHeight - startHeight) * t) - EndThicknessMeters + (archLift * 4 * t * (1 - t));
        Vector3 At(double t, double lateral, double y)
        {
            Vec2 p = Vec2.Lerp(bridge.From, bridge.To, t) + (n * lateral);
            return new Vector3((float)p.X, (float)y, (float)p.Y);
        }

        for (int i = 0; i < Segments; i++)
        {
            double t0 = i / (double)Segments, t1 = (i + 1) / (double)Segments;

            // Roadway and the arched underside.
            b.FacingQuad(At(t0, -half, Top(t0)), At(t1, -half, Top(t1)), At(t1, half, Top(t1)), At(t0, half, Top(t0)), Vector3.UnitY, Roadway);
            b.FacingQuad(At(t0, -half, Under(t0)), At(t1, -half, Under(t1)), At(t1, half, Under(t1)), At(t0, half, Under(t0)), -Vector3.UnitY, Soffit);

            // The two faces of the bridge (the spandrel walls) between the underside and the roadway.
            foreach (int side in new[] { -1, 1 })
            {
                var outward = new Vector3((float)(n.X * side), 0, (float)(n.Y * side));
                b.FacingQuad(At(t0, side * half, Under(t0)), At(t1, side * half, Under(t1)), At(t1, side * half, Top(t1)), At(t0, side * half, Top(t0)), outward, Stone);

                // A parapet along the edge of the roadway, following its slope.
                Vector3 a = At(t0, side * (half - (ParapetThickness / 2)), Top(t0));
                Vector3 c = At(t1, side * (half - (ParapetThickness / 2)), Top(t1));
                Vector3 along = Vector3.Normalize(c - a);
                var across = new Vector3((float)n.X, 0, (float)n.Y);
                b.OrientedBox(((a + c) / 2) + (Vector3.UnitY * (float)(ParapetHeight / 2)), along, Vector3.UnitY, across, new Vector3((c - a).Length() + 0.04f, (float)ParapetHeight, (float)ParapetThickness), Stone);
            }
        }

        // An abutment under each end: masonry from the bank up into the bridge. It stands under the roadway (its top is inside the deck and
        // it is a little narrower than the faces), so nothing shows above the ground beyond the ends and nothing shares a plane with the bridge.
        foreach ((double t, double sign, double height) in new[] { (0.0, 1.0, startHeight), (1.0, -1.0, endHeight) })
        {
            Vec2 end = Vec2.Lerp(bridge.From, bridge.To, t);
            double bank = ground.HeightAt(end.X, end.Y);
            double bottom = bank - AbutmentDepth;
            double topY = height - AbutmentTop;
            Vec2 centre = end + (d * (sign * (AbutmentLength / 2)));
            var along = new Vector3((float)d.X, 0, (float)d.Y);
            var across = new Vector3((float)n.X, 0, (float)n.Y);
            b.OrientedBox(new Vector3((float)centre.X, (float)((bottom + topY) / 2), (float)centre.Y), along, Vector3.UnitY, across, new Vector3((float)AbutmentLength, (float)(topY - bottom), (float)((2 * half) - AbutmentInset)), Stone);
        }

        return b.Build("city.bridge");
    }
}
