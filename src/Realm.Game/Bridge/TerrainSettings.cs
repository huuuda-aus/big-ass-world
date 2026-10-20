namespace Realm.Game.Bridge;

/// <summary>How Terrain3D collides with the player.</summary>
public enum TerrainCollisionMode
{
    /// <summary>No collision shapes.</summary>
    Disabled = 0,

    /// <summary>Shapes follow the camera or player at runtime, only around it.</summary>
    DynamicGame = 1,

    /// <summary>Shapes for every region.</summary>
    FullGame = 3,
}

/// <summary>Settings for creating a Terrain3D node through <see cref="TerrainBridge"/>.</summary>
/// <param name="RegionSize">Vertices per region side: 64, 128, 256, 512, 1024 or 2048.</param>
/// <param name="VertexSpacing">Metres between vertices.</param>
/// <param name="Collision">Collision mode.</param>
/// <param name="CollisionShapeSize">Vertices per dynamic collision shape side (Terrain3D <c>collision_shape_size</c>, 8 to 64 in steps of 8).</param>
/// <param name="CollisionRadius">Vertices around the camera that get collision (Terrain3D <c>collision_radius</c>, 16 to 256 in steps of 16).</param>
public sealed record TerrainSettings(
    int RegionSize,
    float VertexSpacing,
    TerrainCollisionMode Collision,
    int CollisionShapeSize = 8,
    int CollisionRadius = 16);
