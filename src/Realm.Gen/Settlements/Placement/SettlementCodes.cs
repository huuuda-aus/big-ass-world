namespace Realm.Gen.Settlements.Placement;

/// <summary>Validator codes of the settlement step (see docs/reference/validators.md).</summary>
public static class SettlementCodes
{
    /// <summary>The settings are invalid.</summary>
    public const string SettingsInvalid = "SETL001";

    /// <summary>Fewer settlements could be placed than requested.</summary>
    public const string TooFewPlaced = "SETL002";

    /// <summary>Two settlements are closer than the minimum spacing.</summary>
    public const string TooClose = "SETL003";

    /// <summary>The ground under a settlement is steeper than allowed, or outside the allowed height range.</summary>
    public const string PadUnsuitable = "SETL004";

    /// <summary>A river or lake reaches a settlement's pad.</summary>
    public const string WaterOnPad = "SETL005";

    /// <summary>The settlements file is damaged or has an unsupported format.</summary>
    public const string ManifestInvalid = "SETL006";

    /// <summary>A settlement has fewer of a service building than it must contain.</summary>
    public const string ServiceMissing = "SETL007";

    /// <summary>A service building's footprint is smaller than the service asks for (the largest free lot was used).</summary>
    public const string ServiceTooSmall = "SETL008";

    /// <summary>A service building meant to stand outside the wall stands inside it, or the other way round.</summary>
    public const string ServiceMisplaced = "SETL009";
}
