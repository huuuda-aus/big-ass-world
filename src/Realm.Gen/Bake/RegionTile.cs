using System;

namespace Realm.Gen.Bake;

/// <summary>
/// One baked terrain region: a square block of heights and an RGBA colour map for the same vertices, ready for the
/// game to stream in. Region (0, 0) starts at the world origin and extends east and south; coordinates run from
/// <c>-regionsPerAxis / 2</c> to <c>regionsPerAxis / 2 - 1</c> on each axis.
/// </summary>
/// <remarks>Thread-safety: immutable once built; the arrays must not be modified by callers.</remarks>
public sealed class RegionTile
{
    /// <summary>Creates a tile.</summary>
    /// <param name="regionX">Region coordinate, east.</param>
    /// <param name="regionZ">Region coordinate, south.</param>
    /// <param name="size">Vertices per side.</param>
    /// <param name="spacingMeters">Metres between vertices.</param>
    /// <param name="heights">Heights in metres, row by row from north to south; <c>size x size</c> values.</param>
    /// <param name="colours">Colour per vertex as 0xAABBGGRR packed bytes (red lowest), same layout as <paramref name="heights"/>.</param>
    /// <exception cref="ArgumentException">An array has the wrong length or the size is below 2.</exception>
    public RegionTile(int regionX, int regionZ, int size, float spacingMeters, float[] heights, uint[] colours)
    {
        ArgumentNullException.ThrowIfNull(heights);
        ArgumentNullException.ThrowIfNull(colours);
        if (size < 2)
        {
            throw new ArgumentException("A region needs at least 2 vertices per side.", nameof(size));
        }

        if (heights.Length != size * size || colours.Length != size * size)
        {
            throw new ArgumentException($"Expected {size * size} values per array.", nameof(heights));
        }

        RegionX = regionX;
        RegionZ = regionZ;
        Size = size;
        SpacingMeters = spacingMeters;
        Heights = heights;
        Colours = colours;
    }

    /// <summary>Region coordinate, east.</summary>
    public int RegionX { get; }

    /// <summary>Region coordinate, south.</summary>
    public int RegionZ { get; }

    /// <summary>Vertices per side.</summary>
    public int Size { get; }

    /// <summary>Metres between vertices.</summary>
    public float SpacingMeters { get; }

    /// <summary>Width of the region in metres.</summary>
    public float ExtentMeters => Size * SpacingMeters;

    /// <summary>Heights in metres.</summary>
    public float[] Heights { get; }

    /// <summary>Colours, packed 0xAABBGGRR (red in the lowest byte).</summary>
    public uint[] Colours { get; }

    /// <summary>The file name used for a region: <c>region_X_Z.tile</c>.</summary>
    /// <param name="regionX">Region coordinate, east.</param>
    /// <param name="regionZ">Region coordinate, south.</param>
    /// <returns>The file name.</returns>
    public static string FileName(int regionX, int regionZ) => $"region_{regionX}_{regionZ}.tile";
}
