namespace Realm.Sim.Navigation;

/// <summary>What a navigation node stands for.</summary>
public enum NavNodeKind
{
    /// <summary>A point along a street or around the central square.</summary>
    Street = 0,

    /// <summary>The spot in front of a building's front door; <see cref="NavNode.Ref"/> is the building ID.</summary>
    Door = 1,

    /// <summary>A gate in the wall.</summary>
    Gate = 2,

    /// <summary>A point just outside a gate, where the roads of the world join the settlement's graph.</summary>
    Outside = 3,

    /// <summary>One end of a bridge.</summary>
    BridgeEnd = 4,

    /// <summary>A point along a road between settlements.</summary>
    Road = 5,
}
