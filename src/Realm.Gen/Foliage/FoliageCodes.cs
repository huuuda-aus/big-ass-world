namespace Realm.Gen.Foliage;

/// <summary>Validator codes of the foliage step (documented in docs/reference/validators.md).</summary>
public static class FoliageCodes
{
    /// <summary>A plant stands on water, a road or a settlement, or outside its species' height and slope limits.</summary>
    public const string PlacedWhereItCannotGrow = "FOL001";

    /// <summary>Two plants of one species are closer than the species' minimum spacing.</summary>
    public const string TooClose = "FOL002";

    /// <summary>A tree or bush species placed nothing at all (its biomes may have no suitable ground).</summary>
    public const string SpeciesNotPlaced = "FOL003";
}
