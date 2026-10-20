using Godot;

namespace Realm.Game.Bridge;

/// <summary>A Terrain3D region object built by <see cref="TerrainBridge.PrepareRegion"/>, not yet added to the terrain.</summary>
public sealed class PreparedRegion
{
    internal PreparedRegion(Vector2I location, GodotObject region, double buildMs)
    {
        Location = location;
        Region = region;
        BuildMs = buildMs;
    }

    /// <summary>The region coordinate.</summary>
    public Vector2I Location { get; }

    /// <summary>Milliseconds it took to build the region object.</summary>
    public double BuildMs { get; }

    internal GodotObject Region { get; }
}
