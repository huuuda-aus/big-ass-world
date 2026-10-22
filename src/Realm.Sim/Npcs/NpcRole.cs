namespace Realm.Sim.Npcs;

/// <summary>How a character spends the day.</summary>
public enum NpcRole
{
    /// <summary>Hangs about the square, shifting between spots now and then, with the odd errand to a building.</summary>
    Loiterer = 0,

    /// <summary>Goes about its business between its home and a few other buildings, and sleeps at home at night.</summary>
    Walker = 1,

    /// <summary>Stands at one post all day and all night.</summary>
    Guard = 2,
}
