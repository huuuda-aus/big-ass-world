namespace Realm.Sim.Npcs;

/// <summary>Whether a character is standing, walking or inside a building.</summary>
public enum NpcState
{
    /// <summary>Standing in the open.</summary>
    Standing = 0,

    /// <summary>Inside a building (or out in the fields); not visible.</summary>
    Inside = 1,

    /// <summary>Walking along a route.</summary>
    Walking = 2,
}

/// <summary>Why a character is doing what it does.</summary>
public enum NpcPurpose
{
    /// <summary>Nothing in particular.</summary>
    Idle = 0,

    /// <summary>Going home or at home.</summary>
    Home = 1,

    /// <summary>Going to work or working.</summary>
    Work = 2,

    /// <summary>Out and about.</summary>
    About = 3,

    /// <summary>Going to the inn or in it.</summary>
    Inn = 4,

    /// <summary>On an errand.</summary>
    Errand = 5,

    /// <summary>On patrol.</summary>
    Patrol = 6,

    /// <summary>Going to the fields or out in them.</summary>
    Fields = 7,

    /// <summary>Taking shelter from the rain.</summary>
    Shelter = 8,
}

/// <summary>What a character is doing now, for an inspector.</summary>
/// <param name="State">Standing, walking or inside.</param>
/// <param name="Purpose">Why.</param>
/// <param name="TargetNode">The graph node it is walking to, or the door it is inside; -1 when standing in the open.</param>
/// <param name="UntilMinute">The game time in minutes at which it next does something else (arrives, comes out, moves on).</param>
public readonly record struct NpcStatus(NpcState State, NpcPurpose Purpose, int TargetNode, double UntilMinute);
