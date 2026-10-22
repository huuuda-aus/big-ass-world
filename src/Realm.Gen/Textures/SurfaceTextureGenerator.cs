using System;
using Realm.Core.Random;

namespace Realm.Gen.Textures;

/// <summary>
/// Synthesises the small tileable ground textures used until hand-made art exists (CLAUDE.md P6: 64 pixel textures).
/// </summary>
/// <remarks>
/// Determinism: integer hashing and IEEE double arithmetic from <see cref="Rng"/>-derived seeds, so output is identical
/// on every machine. Bump <see cref="Version"/> when the pixels change and regenerate the golden checksums.
/// Thread-safety: all methods are pure.
/// </remarks>
public static class SurfaceTextureGenerator
{
    /// <summary>Generator identifier recorded with baked output.</summary>
    public const string GeneratorId = "gen.textures.surface";

    /// <summary>Generator version; bump when output changes.</summary>
    public const int Version = 2;

    /// <summary>Texture width and height in pixels.</summary>
    public const int DefaultSize = 64;

    private const double NormalStrength = 2.5;
    private const int Octaves = 4;
    private const double Persistence = 0.7; // slow decay keeps fine detail, so nearest-filtered texels read as grain

    /// <summary>Makes a mottled green ground texture.</summary>
    /// <param name="seed">World seed; the same seed gives the same pixels.</param>
    /// <param name="size">Width and height in pixels; a power of two of at least 8.</param>
    /// <returns>The texture.</returns>
    public static SurfaceTexture Grass(ulong seed, int size = DefaultSize) =>
        Build("grass", seed, size, cells: 8, contrast: 0.55,
            dark: (0.62, 0.78, 0.40), light: (0.92, 1.00, 0.62), roughness: 0.9);

    /// <summary>Makes a grey, craggy rock texture.</summary>
    /// <param name="seed">World seed; the same seed gives the same pixels.</param>
    /// <param name="size">Width and height in pixels; a power of two of at least 8.</param>
    /// <returns>The texture.</returns>
    public static SurfaceTexture Rock(ulong seed, int size = DefaultSize) =>
        Build("rock", seed, size, cells: 4, contrast: 0.9,
            dark: (0.52, 0.50, 0.48), light: (0.95, 0.92, 0.88), roughness: 0.75);

    /// <summary>Makes a deep blue, glossy water texture with soft ripples; opaque, so the bed never shows through.</summary>
    /// <param name="seed">World seed; the same seed gives the same pixels.</param>
    /// <param name="size">Width and height in pixels; a power of two of at least 8.</param>
    /// <returns>The texture.</returns>
    public static SurfaceTexture Water(ulong seed, int size = DefaultSize) =>
        Build("water", seed, size, cells: 6, contrast: 0.5,
            dark: (0.07, 0.20, 0.36), light: (0.17, 0.38, 0.56), roughness: 0.3);

    /// <summary>
    /// Makes a wood-grain texture: four planks across, grain running along the horizontal axis, with dark seams between the planks.
    /// It is a light, neutral brown meant to be multiplied by a vertex colour, so the same texture serves any timber tint.
    /// </summary>
    /// <param name="seed">World seed; the same seed gives the same pixels.</param>
    /// <param name="size">Width and height in pixels; a power of two of at least 8.</param>
    /// <returns>The texture.</returns>
    public static SurfaceTexture Wood(ulong seed, int size = DefaultSize)
    {
        if (size < 8 || (size & (size - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "Size must be a power of two of at least 8.");
        }

        ulong key = Rng.ForKey(seed, "texture", "wood").NextUInt64();
        ulong plankKey = Rng.ForKey(seed, "texture", "wood", "plank").NextUInt64();
        var height = new double[size * size];
        for (int y = 0; y < size; y++)
        {
            double v = (double)y / size;
            int plank = (int)Math.Floor(v * WoodPlanks);
            // Each plank has its own grain offset, so neighbours do not match; grain lines depend on the row only (they run along the plank).
            double offset = Lattice(plankKey, plank, 0, WoodPlanks);
            double grain = ValueNoise(key, 0, (v * WoodGrainLines) + (offset * WoodGrainLines), WoodGrainLines);
            for (int x = 0; x < size; x++)
            {
                double u = (double)x / size;
                double wander = ValueNoise(key + 1, (u * WoodWander) + offset, v * WoodWander, WoodWander);
                double h = (0.65 * grain) + (0.35 * wander);
                double inPlank = (v * WoodPlanks) - plank;
                height[(y * size) + x] = inPlank < WoodSeam ? h * 0.15 : h;
            }
        }

        return Shade("wood", size, height, 0.7, dark: (0.66, 0.58, 0.48), light: (1.0, 0.92, 0.80), roughness: 0.9);
    }

    private const int WoodPlanks = 4;
    private const int WoodGrainLines = 16;
    private const int WoodWander = 2;
    private const double WoodSeam = 0.07;

    private static SurfaceTexture Build(
        string name, ulong seed, int size, int cells, double contrast,
        (double R, double G, double B) dark, (double R, double G, double B) light, double roughness)
    {
        if (size < 8 || (size & (size - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "Size must be a power of two of at least 8.");
        }

        ulong key = Rng.ForKey(seed, "texture", name).NextUInt64();
        var height = new double[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                height[(y * size) + x] = Fbm(key, (double)x / size, (double)y / size, cells);
            }
        }

        return Shade(name, size, height, contrast, dark, light, roughness);
    }

    // Turns a tileable height field in [0, 1] into the albedo (colour from dark to light, height in alpha) and normal (roughness in alpha) images.
    private static SurfaceTexture Shade(
        string name, int size, double[] height, double contrast,
        (double R, double G, double B) dark, (double R, double G, double B) light, double roughness)
    {
        var albedo = new byte[size * size * 4];
        var normal = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int i = (y * size) + x;
                double h = Math.Clamp(0.5 + ((height[i] - 0.5) * contrast * 2), 0, 1);
                int p = i * 4;
                albedo[p] = ToByte(Lerp(dark.R, light.R, h));
                albedo[p + 1] = ToByte(Lerp(dark.G, light.G, h));
                albedo[p + 2] = ToByte(Lerp(dark.B, light.B, h));
                albedo[p + 3] = ToByte(h);

                // Central differences with wrap-around keep the normal map tileable.
                double dx = height[(y * size) + ((x + 1) % size)] - height[(y * size) + ((x + size - 1) % size)];
                double dy = height[(((y + 1) % size) * size) + x] - height[(((y + size - 1) % size) * size) + x];
                double nx = -dx * NormalStrength;
                double ny = -dy * NormalStrength;
                double inv = 1.0 / Math.Sqrt((nx * nx) + (ny * ny) + 1.0);
                normal[p] = ToByte(0.5 + (0.5 * nx * inv));
                normal[p + 1] = ToByte(0.5 + (0.5 * ny * inv));
                normal[p + 2] = ToByte(0.5 + (0.5 * inv));
                normal[p + 3] = ToByte(roughness);
            }
        }

        return new SurfaceTexture(name, size, albedo, normal);
    }

    // Periodic fractal value noise in [0, 1]: each octave doubles the lattice, which always wraps at the image edge.
    internal static double Fbm(ulong key, double u, double v, int baseCells)
    {
        double sum = 0;
        double amplitude = 1;
        double total = 0;
        int cells = baseCells;
        for (int octave = 0; octave < Octaves; octave++)
        {
            sum += amplitude * ValueNoise(key + (ulong)octave, u * cells, v * cells, cells);
            total += amplitude;
            amplitude *= Persistence;
            cells *= 2;
        }

        return sum / total;
    }

    internal static double ValueNoise(ulong key, double x, double y, int period)
    {
        int x0 = (int)Math.Floor(x);
        int y0 = (int)Math.Floor(y);
        double fx = Smooth(x - x0);
        double fy = Smooth(y - y0);
        double a = Lattice(key, x0, y0, period);
        double b = Lattice(key, x0 + 1, y0, period);
        double c = Lattice(key, x0, y0 + 1, period);
        double d = Lattice(key, x0 + 1, y0 + 1, period);
        return Lerp(Lerp(a, b, fx), Lerp(c, d, fx), fy);
    }

    internal static double Lattice(ulong key, int x, int y, int period)
    {
        ulong h = key;
        h ^= (ulong)(((x % period) + period) % period) * 0x9E3779B97F4A7C15UL;
        h ^= (ulong)(((y % period) + period) % period) * 0xC2B2AE3D27D4EB4FUL;
        h ^= h >> 33;
        h *= 0xFF51AFD7ED558CCDUL;
        h ^= h >> 33;
        h *= 0xC4CEB9FE1A85EC53UL;
        h ^= h >> 33;
        return (h >> 11) / (double)(1UL << 53);
    }

    private static double Smooth(double t) => t * t * (3 - (2 * t));

    private static double Lerp(double a, double b, double t) => a + ((b - a) * t);

    private static byte ToByte(double value) => (byte)Math.Clamp((int)Math.Round(value * 255.0), 0, 255);
}
