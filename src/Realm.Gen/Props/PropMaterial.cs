namespace Realm.Gen.Props;

/// <summary>
/// Which real-world material a triangle represents. Gen only names the material; a texture (if any) is a
/// presentation concern, chosen and applied by <c>Realm.Game</c>.
/// </summary>
public enum PropMaterial
{
    /// <summary>No material: the renderer uses the triangle's flat <see cref="PropTriangle.Colour"/>, as every prop did before texturing.</summary>
    None = 0,

    /// <summary>Plastered or brick exterior wall.</summary>
    WallPlaster,

    /// <summary>Mud or cob exterior wall (slum dwellings).</summary>
    Mud,

    /// <summary>Thatched roof.</summary>
    RoofThatch,

    /// <summary>Red clay roof tile.</summary>
    RoofTileRed,

    /// <summary>Grey clay or slate roof tile.</summary>
    RoofTileGrey,

    /// <summary>Rough wood: beams, doors, the roof's underside.</summary>
    Wood,

    /// <summary>Stacked stone: a foundation plinth.</summary>
    Stone,
}
