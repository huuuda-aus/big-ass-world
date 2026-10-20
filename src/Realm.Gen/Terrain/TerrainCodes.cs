namespace Realm.Gen.Terrain;

/// <summary>
/// Stable issue codes produced by the terrain generator's validators. Meanings and fixes:
/// docs/reference/validators.md. Codes are never reused for a different problem.
/// </summary>
public static class TerrainCodes
{
    /// <summary>The world size is not a multiple of the macro cell size.</summary>
    public const string WorldSizeNotCellMultiple = "TERR001";

    /// <summary>Some cells are owned by no region.</summary>
    public const string Gap = "TERR002";

    /// <summary>Some cells are claimed by more than one region.</summary>
    public const string Overlap = "TERR003";

    /// <summary>A region owns no cells.</summary>
    public const string RegionOwnsNoCells = "TERR004";

    /// <summary>There are more regions than the cell encoding can hold.</summary>
    public const string TooManyRegions = "TERR005";

    /// <summary>A biome lacks a property the height-field step requires.</summary>
    public const string BiomeMissingProperty = "TERR006";

    /// <summary>A biome property is outside its allowed range.</summary>
    public const string BiomePropertyOutOfRange = "TERR007";

    /// <summary>A biome has a property that does not apply to it and is ignored.</summary>
    public const string BiomePropertyIgnored = "TERR008";

    /// <summary>A region names a biome that is not loaded.</summary>
    public const string BiomeMissing = "TERR009";

    /// <summary>The height-field settings are invalid for this world.</summary>
    public const string HeightSettingsInvalid = "TERR010";

    /// <summary>The region field has gaps or is empty, so heights cannot be generated.</summary>
    public const string RegionFieldIncomplete = "TERR011";

    /// <summary>A generated height is not a finite number.</summary>
    public const string HeightNonFinite = "TERR012";

    /// <summary>A generated height is outside the allowed range.</summary>
    public const string HeightOutOfRange = "TERR013";

    /// <summary>The terrain is steeper than allowed where two regions meet.</summary>
    public const string BorderTooSteep = "TERR014";

    /// <summary>The erosion settings are invalid for this world.</summary>
    public const string ErosionSettingsInvalid = "TERR015";

    /// <summary>A river's bed rises noticeably downstream.</summary>
    public const string RiverUphill = "TERR016";

    /// <summary>A river ends somewhere other than the sea, a lake, the world edge or another river.</summary>
    public const string RiverDeadEnd = "TERR017";

    /// <summary>The terrain bake settings are invalid for this height field.</summary>
    public const string BakeSettingsInvalid = "TERR018";
}
