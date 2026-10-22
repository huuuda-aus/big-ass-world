namespace Realm.Gen.Settlements.Navigation;

/// <summary>Validator codes of the settlement navigation graph (documented in docs/reference/validators.md).</summary>
public static class NavCodes
{
    /// <summary>A building's front door cannot be reached on foot from the central square.</summary>
    public const string DoorUnreachable = "NAV001";

    /// <summary>A gate cannot be reached on foot from the central square.</summary>
    public const string GateUnreachable = "NAV002";

    /// <summary>A settlement is not joined to the others by the road network on the world walking graph.</summary>
    public const string SettlementNotJoined = "NAV003";
}
