namespace Realm.Gen.Terrain;

/// <summary>Kinds of landmark that a biome can scatter across its region (CLAUDE.md section 6.1).</summary>
public enum LandmarkKind
{
    /// <summary>A smooth, isolated hill.</summary>
    Hill,

    /// <summary>A rugged, ridged mountain that stands alone.</summary>
    Mountain,

    /// <summary>A depression; below sea level or deep enough it fills with water and becomes a lake or lagoon.</summary>
    Basin,
}
