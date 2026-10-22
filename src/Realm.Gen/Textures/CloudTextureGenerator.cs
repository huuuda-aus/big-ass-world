using System;
using Realm.Core.Random;

namespace Realm.Gen.Textures;

/// <summary>
/// Makes the tileable noise texture the sky shader builds clouds from: four channels of different character, so the shader can combine
/// large shapes, billowing cells, fine erosion and streaks without computing noise per pixel.
/// </summary>
/// <remarks>
/// Channels: R large soft shapes; G billowy cell noise (bright in the middle of a cell, like cumulus tops); B fine detail; A long streaks
/// along the horizontal axis (for high wispy cloud). Determinism: integer hashing and IEEE double arithmetic from <see cref="Rng"/>-derived
/// seeds, identical on every machine. Thread-safety: pure.
/// </remarks>
public static class CloudTextureGenerator
{
    /// <summary>Generator identifier recorded with baked output.</summary>
    public const string GeneratorId = "gen.textures.clouds";

    /// <summary>Generator version; bump when the pixels change and regenerate the golden checksums.</summary>
    public const int Version = 1;

    /// <summary>Default width and height in pixels.</summary>
    public const int DefaultSize = 256;

    private const int ShapeCells = 4;
    private const int BillowCells = 8;
    private const int DetailCells = 24;
    private const int StreakCellsAcross = 48;
    private const int StreakCellsAlong = 6;

    /// <summary>Makes the noise texture.</summary>
    /// <param name="seed">World seed; the same seed gives the same pixels.</param>
    /// <param name="size">Width and height in pixels; a power of two of at least 16.</param>
    /// <returns>RGBA bytes, <c>size * size * 4</c>, row by row.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="size"/> is not a power of two of at least 16.</exception>
    public static byte[] Noise(ulong seed, int size = DefaultSize)
    {
        if (size < 16 || (size & (size - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "Size must be a power of two of at least 16.");
        }

        ulong shapeKey = Rng.ForKey(seed, "texture", "cloud", "shape").NextUInt64();
        ulong billowKey = Rng.ForKey(seed, "texture", "cloud", "billow").NextUInt64();
        ulong detailKey = Rng.ForKey(seed, "texture", "cloud", "detail").NextUInt64();
        ulong streakKey = Rng.ForKey(seed, "texture", "cloud", "streak").NextUInt64();
        var pixels = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        {
            double v = (double)y / size;
            for (int x = 0; x < size; x++)
            {
                double u = (double)x / size;
                int p = ((y * size) + x) * 4;
                pixels[p] = ToByte(SurfaceTextureGenerator.Fbm(shapeKey, u, v, ShapeCells));
                pixels[p + 1] = ToByte(Billow(billowKey, u, v));
                pixels[p + 2] = ToByte(SurfaceTextureGenerator.Fbm(detailKey, u, v, DetailCells / 2));
                pixels[p + 3] = ToByte(Streak(streakKey, u, v));
            }
        }

        return pixels;
    }

    // Worley cell noise: distance to the nearest of one jittered point per cell, inverted so cell centres are bright puffs.
    private static double Billow(ulong key, double u, double v)
    {
        double x = u * BillowCells;
        double y = v * BillowCells;
        int cx = (int)Math.Floor(x);
        int cy = (int)Math.Floor(y);
        double nearest = double.MaxValue;
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int gx = cx + dx;
                int gy = cy + dy;
                double px = gx + SurfaceTextureGenerator.Lattice(key, gx, gy, BillowCells);
                double py = gy + SurfaceTextureGenerator.Lattice(key + 1, gx, gy, BillowCells);
                nearest = Math.Min(nearest, ((x - px) * (x - px)) + ((y - py) * (y - py)));
            }
        }

        return Math.Clamp(1 - Math.Sqrt(nearest), 0, 1);
    }

    // Streaks: noise that varies quickly across the rows but slowly along them, so it draws long horizontal wisps.
    private static double Streak(ulong key, double u, double v)
    {
        double along = Stretched(key, u, v, StreakCellsAlong, StreakCellsAlong);
        double fine = Stretched(key + 1, u, v, StreakCellsAlong, StreakCellsAcross);
        return Math.Clamp((0.55 * fine) + (0.45 * along), 0, 1);
    }

    // Tileable value noise with its own number of cells along each axis (the shared noise uses one period for both).
    private static double Stretched(ulong key, double u, double v, int cellsX, int cellsY)
    {
        double x = u * cellsX;
        double y = v * cellsY;
        int x0 = (int)Math.Floor(x);
        int y0 = (int)Math.Floor(y);
        double fx = Smooth(x - x0);
        double fy = Smooth(y - y0);
        double a = Corner(key, x0, y0, cellsX, cellsY);
        double b = Corner(key, x0 + 1, y0, cellsX, cellsY);
        double c = Corner(key, x0, y0 + 1, cellsX, cellsY);
        double d = Corner(key, x0 + 1, y0 + 1, cellsX, cellsY);
        double top = a + ((b - a) * fx);
        double bottom = c + ((d - c) * fx);
        return top + ((bottom - top) * fy);
    }

    private static double Corner(ulong key, int x, int y, int cellsX, int cellsY) =>
        SurfaceTextureGenerator.Lattice(key, ((x % cellsX) + cellsX) % cellsX, ((y % cellsY) + cellsY) % cellsY, NoWrap);

    private static double Smooth(double t) => t * t * (3 - (2 * t));

    private const int NoWrap = 1 << 24; // the corners are wrapped by Corner, so the shared lattice must not wrap them again

    private static byte ToByte(double value) => (byte)Math.Clamp((int)Math.Round(value * 255.0), 0, 255);
}
