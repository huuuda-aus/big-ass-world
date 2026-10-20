using Godot;

namespace Realm.Game.World;

/// <summary>What a streaming step did on the main thread.</summary>
public enum StreamEventKind
{
    /// <summary>A region was built and added to the terrain.</summary>
    Add = 0,

    /// <summary>A region was removed.</summary>
    Remove = 1,

    /// <summary>Shared textures were rebuilt after a batch of adds.</summary>
    Update = 2,
}

/// <summary>One timed main-thread streaming step, for correlating with frame hitches.</summary>
/// <param name="Frame">Frame number in which it happened.</param>
/// <param name="Kind">What happened.</param>
/// <param name="Location">The region involved (default for <see cref="StreamEventKind.Update"/>).</param>
/// <param name="Milliseconds">Main-thread time it took.</param>
public readonly record struct StreamEvent(long Frame, StreamEventKind Kind, Vector2I Location, double Milliseconds);
