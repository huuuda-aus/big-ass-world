using System.Numerics;

namespace Realm.Gen.Props;

/// <summary>Builds simple low-poly medieval houses: a timber-framed cottage and a two-storey town house.</summary>
/// <remarks>
/// Both have a stone foundation sunk <see cref="FoundationDepthMeters"/> below the origin so they stand on sloping ground,
/// a gabled roof with the ridge along X, and the front (door side) facing +Z. Determinism: pure functions.
/// </remarks>
public static class HouseModels
{
    /// <summary>How far the foundation reaches below Y = 0.</summary>
    public const float FoundationDepthMeters = 1.5f;

    /// <summary>Cottage footprint width (X) in metres.</summary>
    public const float CottageWidth = 6f;

    /// <summary>Cottage footprint depth (Z) in metres.</summary>
    public const float CottageDepth = 5f;

    /// <summary>Cottage wall height in metres.</summary>
    public const float CottageWallHeight = 2.4f;

    /// <summary>Cottage roof rise from eaves to ridge in metres.</summary>
    public const float CottageRoofRise = 1.8f;

    /// <summary>Town house footprint width (X) in metres.</summary>
    public const float TownHouseWidth = 7f;

    /// <summary>Town house footprint depth (Z) in metres.</summary>
    public const float TownHouseDepth = 6f;

    /// <summary>Town house wall height (two storeys) in metres.</summary>
    public const float TownHouseWallHeight = 5f;

    /// <summary>Town house roof rise from eaves to ridge in metres.</summary>
    public const float TownHouseRoofRise = 2.4f;

    /// <summary>How far the roof overhangs the walls on every side, in metres.</summary>
    public const float RoofOverhang = 0.35f;

    private const float PostSize = 0.22f;
    private const float Relief = 0.06f; // details stand this far proud of the wall
    private const float DoorWidth = 1.0f;
    private const float DoorHeight = 2.0f;
    private const float WindowWidth = 0.8f;
    private const float WindowHeight = 0.9f;
    private const float BandHeight = 0.2f;
    private static readonly Vector3 Plaster = new(0.86f, 0.80f, 0.66f);
    private static readonly Vector3 Timber = new(0.30f, 0.19f, 0.11f);
    private static readonly Vector3 Stone = new(0.50f, 0.49f, 0.46f);
    private static readonly Vector3 Thatch = new(0.64f, 0.52f, 0.26f);
    private static readonly Vector3 Tiles = new(0.58f, 0.27f, 0.19f);
    private static readonly Vector3 Glass = new(0.16f, 0.22f, 0.30f);

    /// <summary>One-storey cottage: 6 x 5 m, 4.2 m to the ridge.</summary>
    /// <returns>The model.</returns>
    public static PropMesh Cottage() =>
        Build("house.cottage", CottageWidth, CottageDepth, CottageWallHeight, CottageRoofRise, Thatch, storeys: 1);

    /// <summary>Two-storey town house: 7 x 6 m, 7.4 m to the ridge.</summary>
    /// <returns>The model.</returns>
    public static PropMesh TownHouse() =>
        Build("house.town_house", TownHouseWidth, TownHouseDepth, TownHouseWallHeight, TownHouseRoofRise, Tiles, storeys: 2);

    private static PropMesh Build(string name, float width, float depth, float wall, float rise, Vector3 roof, int storeys)
    {
        var b = new PropMeshBuilder();
        float x0 = -width / 2;
        float x1 = width / 2;
        float z0 = -depth / 2;
        float z1 = depth / 2;

        b.Box(new Vector3(x0 - 0.1f, -FoundationDepthMeters, z0 - 0.1f), new Vector3(x1 + 0.1f, 0.3f, z1 + 0.1f), Stone);
        b.Box(new Vector3(x0, 0.3f, z0), new Vector3(x1, wall, z1), Plaster);
        b.GableRoofAlongX(x0 - RoofOverhang, x1 + RoofOverhang, z0 - RoofOverhang, z1 + RoofOverhang, wall, wall + rise, roof);

        // Timber frame: corner posts, a band between storeys, a band under the eaves.
        foreach ((float cx, float cz) in new[] { (x0, z0), (x1, z0), (x0, z1), (x1, z1) })
        {
            b.Box(
                new Vector3(cx - (PostSize / 2), 0.3f, cz - (PostSize / 2)),
                new Vector3(cx + (PostSize / 2), wall, cz + (PostSize / 2)),
                Timber);
        }

        for (int storey = 1; storey <= storeys; storey++)
        {
            float top = storey * (wall / storeys);
            float y0 = top - BandHeight;
            b.Box(new Vector3(x0, y0, z1), new Vector3(x1, top, z1 + Relief), Timber);
            b.Box(new Vector3(x0, y0, z0 - Relief), new Vector3(x1, top, z0), Timber);
        }

        // Front (+Z): door at the centre, windows each side; upper windows on the town house.
        b.Box(new Vector3(-DoorWidth / 2, 0.3f, z1), new Vector3(DoorWidth / 2, 0.3f + DoorHeight, z1 + Relief), Timber);
        float side = (width / 4) + (DoorWidth / 4);
        foreach (float wx in new[] { -side, side })
        {
            Window(b, wx, 1.2f, z1);
        }

        if (storeys == 2)
        {
            float upper = (wall / 2) + 0.7f;
            foreach (float wx in new[] { -width / 3, 0f, width / 3 })
            {
                Window(b, wx, upper, z1);
            }
        }

        return b.Build(name);
    }

    private static void Window(PropMeshBuilder b, float centreX, float baseY, float wallZ) =>
        b.Box(
            new Vector3(centreX - (WindowWidth / 2), baseY, wallZ),
            new Vector3(centreX + (WindowWidth / 2), baseY + WindowHeight, wallZ + Relief),
            Glass);
}
