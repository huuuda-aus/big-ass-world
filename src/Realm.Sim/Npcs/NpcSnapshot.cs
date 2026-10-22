namespace Realm.Sim.Npcs;

/// <summary>Where a character is right now, for the presentation.</summary>
/// <param name="Id">The character's index.</param>
/// <param name="Role">Its role.</param>
/// <param name="X">Position east in the graph's frame (metres).</param>
/// <param name="Z">Position south in the graph's frame (metres).</param>
/// <param name="Walking">Whether it is walking (otherwise standing).</param>
public readonly record struct NpcSnapshot(int Id, NpcRole Role, double X, double Z, bool Walking);
