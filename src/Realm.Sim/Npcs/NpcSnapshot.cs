namespace Realm.Sim.Npcs;

/// <summary>Where a character is right now, and how it carries itself, for the presentation.</summary>
/// <param name="Id">The character's index.</param>
/// <param name="Role">Its role.</param>
/// <param name="X">Position east in the graph's frame (metres), including the sideways offset of its lane.</param>
/// <param name="Z">Position south in the graph's frame (metres).</param>
/// <param name="Walking">Whether it is walking (otherwise standing).</param>
/// <param name="Heading">The yaw it faces (see <c>NavRoute.HeadingAt</c>).</param>
/// <param name="Noticing">Whether it is looking at the player.</param>
/// <param name="Alert">Whether it is a guard challenging the player.</param>
public readonly record struct NpcSnapshot(int Id, NpcRole Role, double X, double Z, bool Walking, double Heading = 0, bool Noticing = false, bool Alert = false);
