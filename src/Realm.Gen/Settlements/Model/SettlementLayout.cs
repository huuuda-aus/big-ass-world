namespace Realm.Gen.Settlements.Model;

/// <summary>The shape of a settlement's plan.</summary>
public enum SettlementLayout
{
    /// <summary>Wards cut from a radial pattern (Voronoi cells round the central square), enclosed by a wall that follows their outline.</summary>
    Radial,

    /// <summary>Two main streets crossing at a square, like a crossroads: each of the four arms runs as far as the houses need, with rows of houses along both sides.</summary>
    Cross,
}
