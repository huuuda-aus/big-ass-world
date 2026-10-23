namespace Realm.Gen.Foliage;

/// <summary>Tuning of the foliage step. Placeholder look-tuning values, not design.</summary>
/// <remarks>Thread-safety: immutable.</remarks>
public sealed record FoliageSettings
{
    /// <summary>Side of one streaming chunk in metres.</summary>
    public int ChunkMeters { get; init; } = 256;

    /// <summary>Side of one grass density cell in metres.</summary>
    public int GrassCellMeters { get; init; } = 32;

    /// <summary>Side of one cell of the exclusion raster in metres.</summary>
    public double ExclusionCellMeters { get; init; } = 4;

    /// <summary>Nothing grows below this height (the shore and the sea), metres.</summary>
    public double MinHeightMeters { get; init; } = 1.5;

    /// <summary>Clear margin beside rivers and lakes, metres.</summary>
    public double WaterMarginMeters { get; init; } = 5;

    /// <summary>Clear margin beside a road's edge, metres.</summary>
    public double RoadMarginMeters { get; init; } = 3;

    /// <summary>Clear margin outside a settlement's pad, metres.</summary>
    public double SettlementMarginMeters { get; init; } = 8;

    /// <summary>How far each neighbouring biome's density blends across a border, metres.</summary>
    public int BlendMeters { get; init; } = 600;

    /// <summary>Size of the jitter inside a candidate cell, 0 to 1 (1 = anywhere in the cell).</summary>
    public double Jitter { get; init; } = 0.8;
}
