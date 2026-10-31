namespace Realm.Sim.Combat;

/// <summary>
/// Names of the damage types the spike uses. Damage types are open strings, not an enum, so content can add more;
/// these constants only avoid typos in code.
/// </summary>
public static class DamageTypes
{
    /// <summary>Weapons and claws.</summary>
    public const string Physical = "physical";

    /// <summary>Fire spells and burning.</summary>
    public const string Fire = "fire";

    /// <summary>Venom and poisoned blades.</summary>
    public const string Poison = "poison";
}
