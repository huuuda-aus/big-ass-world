namespace Realm.Gen.Settlements.Model;

/// <summary>What protects a settlement. Chosen from its tier (see <see cref="Placement.Settlement.Fortification"/>).</summary>
public enum Fortification
{
    /// <summary>A tall stone wall with towers and gate towers: a city.</summary>
    Stone,

    /// <summary>A lower wall of sharpened logs with wooden gate towers: a town.</summary>
    Palisade,

    /// <summary>No wall: a hamlet.</summary>
    None,
}
