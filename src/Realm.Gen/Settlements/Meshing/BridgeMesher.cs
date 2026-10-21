using System;
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
    private static readonly Vector3 Timber = SettlementPalette.Hex(0x6b4a2e);
    private static readonly Vector3 Stone = SettlementPalette.Hex(0x8c887e);

    /// <summary>Builds the mesh of a bridge.</summary>
    /// <param name="bridge">The bridge.</param>
    /// <returns>The mesh: deck, abutments, posts and rails.</returns>
    public static PropMesh Build(RoadBridge bridge)
    {
        var b = new PropMeshBuilder();
        float half = (float)bridge.LengthMeters / 2;
        float width = (float)bridge.WidthMeters;
        var start = new Vector3(-half, (float)bridge.StartHeight, 0);
        var end = new Vector3(half, (float)bridge.EndHeight, 0);
        Vector3 along = Vector3.Normalize(end - start);
        Vector3 across = Vector3.UnitZ;
        Vector3 up = Vector3.Normalize(Vector3.Cross(across, along));
        float length = (end - start).Length();

        // The deck's top surface runs from the start height to the end height.
        b.OrientedBox(((start + end) / 2) - (up * (DeckThickness / 2)), along, up, across, new Vector3(length, DeckThickness, width), Timber);

        b.OrientedBox(start - (Vector3.UnitY * (AbutmentDepth / 2)) + (Vector3.UnitX * (-AbutmentLength / 2)), Vector3.UnitX, Vector3.UnitY, across, new Vector3(AbutmentLength, AbutmentDepth, width + 1), Stone);
        b.OrientedBox(end - (Vector3.UnitY * (AbutmentDepth / 2)) + (Vector3.UnitX * (AbutmentLength / 2)), Vector3.UnitX, Vector3.UnitY, across, new Vector3(AbutmentLength, AbutmentDepth, width + 1), Stone);

        foreach (int side in new[] { -1, 1 })
        {
            float z = side * ((width / 2) - 0.1f);
            int posts = Math.Max(2, (int)Math.Ceiling(length / PostSpacing) + 1);
            for (int i = 0; i < posts; i++)
            {
                float t = i / (float)(posts - 1);
                Vector3 foot = Vector3.Lerp(start, end, t) + (across * z);
                b.OrientedBox(foot + (Vector3.UnitY * (RailHeight / 2)), Vector3.UnitX, Vector3.UnitY, across, new Vector3(0.2f, RailHeight, 0.2f), Timber);
            }

            b.OrientedBox(((start + end) / 2) + (across * z) + (up * (RailHeight - 0.1f)), along, up, across, new Vector3(length, 0.12f, 0.16f), Timber);
        }

        return b.Build("bridge");
    }
}
