namespace Realm.Gen.Settlements.Roads;

/// <summary>Validator codes of the road step (see docs/reference/validators.md).</summary>
public static class RoadCodes
{
    /// <summary>The road settings are invalid.</summary>
    public const string SettingsInvalid = "ROAD001";

    /// <summary>A settlement is not connected to the others by road.</summary>
    public const string NotConnected = "ROAD002";

    /// <summary>The terrain under a road differs from the graded road surface, or a cut or fill is deeper than allowed.</summary>
    public const string BedMismatch = "ROAD003";

    /// <summary>A road runs through the sea or a lake.</summary>
    public const string InWater = "ROAD004";

    /// <summary>A bridge is longer than allowed.</summary>
    public const string BridgeTooLong = "ROAD005";

    /// <summary>The roads file is damaged or has an unsupported format.</summary>
    public const string ManifestInvalid = "ROAD006";

    /// <summary>A bridge deck is steeper between its ends than allowed.</summary>
    public const string BridgeTooSteep = "ROAD007";
}
