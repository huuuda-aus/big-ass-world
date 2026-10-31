namespace Realm.Gen.Bake;

/// <summary>What a terrain editor brush dab does to the heights under it.</summary>
public enum BrushMode
{
    /// <summary>Pushes heights up.</summary>
    Raise,

    /// <summary>Pushes heights down.</summary>
    Lower,

    /// <summary>Blends each height toward its neighbours' average.</summary>
    Smooth,

    /// <summary>Moves heights toward <see cref="BrushStroke.FlattenHeight"/>.</summary>
    Flatten,
}
