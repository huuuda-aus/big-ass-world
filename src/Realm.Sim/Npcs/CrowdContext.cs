namespace Realm.Sim.Npcs;

/// <summary>What the crowd's characters can sense of the world this frame.</summary>
/// <param name="Rain">Rain intensity at the settlement, 0 (dry) to 1 (downpour).</param>
/// <param name="PlayerPresent">Whether the player is near enough to be noticed.</param>
/// <param name="PlayerX">The player's position east in the graph's frame (metres).</param>
/// <param name="PlayerZ">The player's position south in the graph's frame (metres).</param>
public readonly record struct CrowdContext(double Rain, bool PlayerPresent, double PlayerX, double PlayerZ);
