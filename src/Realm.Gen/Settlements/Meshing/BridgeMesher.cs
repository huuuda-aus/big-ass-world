using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Realm.Gen.Props;
using Realm.Gen.Settlements.Roads;

namespace Realm.Gen.Settlements.Meshing;

/// <summary>Builds a simple timber bridge with stone abutments: a sloped deck, railings and posts.</summary>
/// <remarks>
/// The mesh is in a frame centred on the bridge at ground X and Z, with heights in absolute metres above sea level; the game places it at the bridge's
/// X and Z (Y = 0) and turns it by minus the bridge's yaw about the vertical axis (the same convention as the city plans: plan rotation counter-clockwise is a turn about the vertical axis by the negative angle).
/// The deck runs along local X. Determinism: pure. Thread-safety: pure.
/// </remarks>
public static class BridgeMesher
{
    private const float DeckThickness = 0.5f;
    private const float RailHeight = 1.0f;
    private const float PostSpacing = 3f;
    private const float AbutmentDepth = 4f;
    private const float AbutmentLength = 3f;
    private const float FootingBelowGround = 1.0f; // an abutment reaches this far below the lowest ground under it
    private const float AbutmentTuck = 0.05f; // the abutment top reaches this far into the deck
    private const float AbutmentInset = 0.2f;
    private const float RampMeters = 4f; // the deck's ends slope down to the ground over this length, a gentle 5 percent
    private const float DeckLift = 0.2f; // the deck stands this far above the road ends, so it never sinks into the graded ground
    private static readonly Vector3 Timber = SettlementPalette.Hex(0x6b4a2e);
    private static readonly Vector3 Stone = SettlementPalette.Hex(0x8c887e);

    /// <summary>Builds the mesh of a bridge.</summary>
    /// <param name="bridge">The bridge.</param>
    /// <returns>The mesh: deck, end ramps, abutments, posts and rails.</returns>
    public static PropMesh Build(RoadBridge bridge)
    {
        (PropMesh timber, PropMesh stone) = BuildParts(bridge);
        return new PropMesh("bridge", timber.Triangles.Concat(stone.Triangles).ToList());
    }

    /// <summary>Builds the bridge in two meshes so the game can give the timber a wood texture and leave the stone plain.</summary>
    /// <param name="bridge">The bridge.</param>
    /// <returns>The timber (deck, ramps, posts, rails) and the stone (abutments).</returns>
    public static (PropMesh Timber, PropMesh Stone) BuildParts(RoadBridge bridge)
    {
        var wood = new PropMeshBuilder();
        var stoneBuilder = new PropMeshBuilder();
        float half = (float)bridge.LengthMeters / 2;
        float width = (float)bridge.WidthMeters;
        var start = new Vector3(-half, (float)bridge.StartHeight + DeckLift, 0);
        var end = new Vector3(half, (float)bridge.EndHeight + DeckLift, 0);
        Vector3 along = Vector3.Normalize(end - start);
        Vector3 across = Vector3.UnitZ;
        Vector3 up = Vector3.Normalize(Vector3.Cross(across, along));
        float length = (end - start).Length();

        // The deck's top surface runs from the start height to the end height.
        wood.OrientedBox(((start + end) / 2) - (up * (DeckThickness / 2)), along, up, across, new Vector3(length, DeckThickness, width), Timber);

        // A ramp of the same timber at each end carries the road up the 20 cm lift: the top runs from the deck's end down to the road's
        // height over RampMeters, and the slab is thick enough that its far end sinks into the ground.
        AddRamp(wood, start, -1, across, width);
        AddRamp(wood, end, 1, across, width);

        // The abutments sit under the ends of the deck and stop below the roadway, so no stone plate shows above the ground where the
        // graded terrain meets the deck (it used to be flush with the road and clipped through the terrain). They are a little narrower
        // than the deck so their sides never share a plane with it.
        float top = -DeckThickness + AbutmentTuck;
        AddAbutment(stoneBuilder, start, 1, top, bridge.StartFootingHeight, across, width);
        AddAbutment(stoneBuilder, end, -1, top, bridge.EndFootingHeight, across, width);

        foreach (int side in new[] { -1, 1 })
        {
            float z = side * ((width / 2) - 0.1f);
            int posts = Math.Max(2, (int)Math.Ceiling(length / PostSpacing) + 1);
            for (int i = 0; i < posts; i++)
            {
                float t = i / (float)(posts - 1);
                Vector3 foot = Vector3.Lerp(start, end, t) + (across * z);
                wood.OrientedBox(foot + (Vector3.UnitY * (RailHeight / 2)), Vector3.UnitX, Vector3.UnitY, across, new Vector3(0.2f, RailHeight, 0.2f), Timber);
            }

            wood.OrientedBox(((start + end) / 2) + (across * z) + (up * (RailHeight - 0.1f)), along, up, across, new Vector3(length, 0.12f, 0.16f), Timber);
        }

        return (wood.Build("bridge.timber"), stoneBuilder.Build("bridge.stone"));
    }

    // A slab whose top slopes from the deck's end (outward is -1 at the start, +1 at the end) down by DeckLift over RampMeters.
    private static void AddRamp(PropMeshBuilder b, Vector3 deckEnd, int outward, Vector3 across, float width)
    {
        Vector3 tip = deckEnd + (Vector3.UnitX * (outward * RampMeters)) - (Vector3.UnitY * DeckLift);
        Vector3 along = Vector3.Normalize(tip - deckEnd);
        Vector3 up = Vector3.Cross(across, along);
        if (up.Y < 0)
        {
            up = -up;
        }

        up = Vector3.Normalize(up);
        b.OrientedBox(((deckEnd + tip) / 2) - (up * (DeckThickness / 2)), along, up, across, new Vector3((tip - deckEnd).Length(), DeckThickness, width), Timber);
    }

    // An abutment hangs from the deck down to the lowest ground under it (never less than AbutmentDepth), so a bank that falls away
    // steeply does not leave it floating.
    private static void AddAbutment(PropMeshBuilder b, Vector3 end, int inward, float top, double? footing, Vector3 across, float width)
    {
        float bottom = top - AbutmentDepth;
        if (footing is double ground)
        {
            bottom = Math.Min(bottom, (float)(ground - end.Y) - FootingBelowGround);
        }

        float height = top - bottom;
        b.OrientedBox(end + (Vector3.UnitY * ((top + bottom) / 2)) + (Vector3.UnitX * (inward * AbutmentLength / 2)), Vector3.UnitX, Vector3.UnitY, across, new Vector3(AbutmentLength, height, width - AbutmentInset), Stone);
    }
}
