namespace Realm.Gen.Settlements.Model;

/// <summary>Where in its ward a service building prefers to stand.</summary>
public enum ServiceNear
{
    /// <summary>No preference.</summary>
    Any,

    /// <summary>Close to a gate.</summary>
    Gate,

    /// <summary>Close to the central square.</summary>
    Square,

    /// <summary>Close to the wall.</summary>
    Wall,
}
