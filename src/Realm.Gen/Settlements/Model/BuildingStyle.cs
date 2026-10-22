namespace Realm.Gen.Settlements.Model;

/// <summary>How a building is built and looks. Chosen from the ward; the art kit (ADR 0015) will add region styles on top.</summary>
public enum BuildingStyle
{
    /// <summary>A well-built house: plaster and half-timbering, thick walls, usually more than one storey and room.</summary>
    Rich,

    /// <summary>A small mud-walled dwelling: one storey, thin walls, little or no timber, few windows.</summary>
    Slum,
}
