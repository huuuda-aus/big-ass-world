using System.Collections.Generic;

namespace Realm.Gen.Foliage;

/// <summary>The output of <see cref="FoliageGenerator"/>: every tree and bush, plus a coarse grass density map.</summary>
/// <remarks>Instances are sorted by chunk (row, then column), then species, then position, so each chunk is a contiguous run.</remarks>
public sealed class FoliageData
{
    /// <summary>Creates foliage data.</summary>
    /// <param name="worldSizeMeters">World side length in metres.</param>
    /// <param name="chunkMeters">Side of one chunk in metres.</param>
    /// <param name="species">All species, sorted by ID.</param>
    /// <param name="instances">Trees and bushes in chunk order.</param>
    /// <param name="grassCellMeters">Side of one grass cell in metres.</param>
    /// <param name="grassSpecies">Per grass cell: index of its dominant grass species.</param>
    /// <param name="grassTufts">Per grass cell: tufts to scatter in it (0 to 255).</param>
    public FoliageData(int worldSizeMeters, int chunkMeters, IReadOnlyList<FoliageSpecies> species, IReadOnlyList<FoliageInstance> instances, int grassCellMeters, byte[] grassSpecies, byte[] grassTufts)
    {
        WorldSizeMeters = worldSizeMeters;
        ChunkMeters = chunkMeters;
        Species = species;
        Instances = instances;
        GrassCellMeters = grassCellMeters;
        GrassSpecies = grassSpecies;
        GrassTufts = grassTufts;
    }

    /// <summary>World side length in metres.</summary>
    public int WorldSizeMeters { get; }

    /// <summary>Side of one chunk in metres.</summary>
    public int ChunkMeters { get; }

    /// <summary>Chunks along one side of the world.</summary>
    public int ChunksPerSide => (WorldSizeMeters + ChunkMeters - 1) / ChunkMeters;

    /// <summary>All species, sorted by ID.</summary>
    public IReadOnlyList<FoliageSpecies> Species { get; }

    /// <summary>Trees and bushes in chunk order.</summary>
    public IReadOnlyList<FoliageInstance> Instances { get; }

    /// <summary>Side of one grass cell in metres.</summary>
    public int GrassCellMeters { get; }

    /// <summary>Grass cells along one side of the world.</summary>
    public int GrassSide => (WorldSizeMeters + GrassCellMeters - 1) / GrassCellMeters;

    /// <summary>Per grass cell (row by row from the north-west): index of its dominant grass species.</summary>
    public byte[] GrassSpecies { get; }

    /// <summary>Per grass cell: tufts to scatter in it, 0 to 255.</summary>
    public byte[] GrassTufts { get; }
}
