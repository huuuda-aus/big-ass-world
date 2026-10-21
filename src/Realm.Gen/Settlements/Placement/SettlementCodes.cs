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

    /// <summary>A settlement's pad is not flat, is under the sea, or is outside the allowed height range.</summary>
    public const string PadUnsuitable = "SETL004";

    /// <summary>A river or lake reaches a settlement's pad.</summary>
    public const string WaterOnPad = "SETL005";

    /// <summary>The settlements file is damaged or has an unsupported format.</summary>
    public const string ManifestInvalid = "SETL006";
}
