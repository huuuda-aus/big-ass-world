using System;
using Realm.Gen.Terrain.Erosion;

namespace Realm.Gen.Terrain;

/// <summary>Renders a height field as a coloured, hill-shaded image for people to look at (not used by the game).</summary>
/// <remarks>Determinism: pure function of the field. Thread-safety: stateless.</remarks>
public static class HeightPreview
{
    private const int BytesPerPixel = 3;
    private const double ShadeStrength = 0.06;
    private const double MaxShadeSwing = 0.45;
    private const double ShallowWaterDepthFraction = 1.0;
    private const double HalfDivisor = 2.0;
    private const int BorderWhiteShare = 70; // percent of the way toward white that a region border pixel is pushed


    /// <summary>Renders the field, sub-sampling so neither side exceeds a pixel limit.</summary>
    /// <param name="field">The heights.</param>
    /// <param name="maxPixelsPerSide">Upper limit for width and height; positive.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="network">Rivers and lakes to draw on top in bright blue; none when null.</param>
    /// <param name="regions">Regions whose borders are drawn as thin pale lines; none when null.</param>
    /// <returns>RGB bytes, 3 per pixel, row by row (north at the top).</returns>
    public static byte[] Render(
        HeightField field, int maxPixelsPerSide, out int width, out int height, RiverNetwork? network = null, RegionField? regions = null)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPixelsPerSide);

        int side = field.VerticesPerSide;
        int step = Math.Max(1, (int)Math.Ceiling((double)side / maxPixelsPerSide));
        width = ((side - 1) / step) + 1;
        height = width;

        double top = Math.Max(1.0, field.MaxHeight);
        double depth = Math.Max(1.0, -field.MinHeight);
        var pixels = new byte[width * height * BytesPerPixel];

        for (int py = 0; py < height; py++)
        {
            for (int px = 0; px < width; px++)
            {
                int vx = px * step;
                int vz = py * step;
                double h = field.At(vx, vz);
                double gradX = (field.At(Math.Min(vx + step, side - 1), vz) - field.At(Math.Max(vx - step, 0), vz)) / (2.0 * step * field.SpacingMeters);
                double gradZ = (field.At(vx, Math.Min(vz + step, side - 1)) - field.At(vx, Math.Max(vz - step, 0))) / (2.0 * step * field.SpacingMeters);
                (double r, double g, double b) = TerrainColouring.Albedo(h, top, depth, Math.Sqrt((gradX * gradX) + (gradZ * gradZ)));

                // Light from the north-west: a surface rising toward the south-east faces the light and is lit more.
                double dx = field.At(Math.Min(vx + step, side - 1), vz) - field.At(Math.Max(vx - step, 0), vz);
                double dz = field.At(vx, Math.Min(vz + step, side - 1)) - field.At(vx, Math.Max(vz - step, 0));
                double shade = Math.Clamp((dx + dz) * ShadeStrength / step, -MaxShadeSwing, MaxShadeSwing);
                double factor = 1.0 + shade;

                int o = ((py * width) + px) * BytesPerPixel;
                pixels[o] = ToByte(r * factor);
                pixels[o + 1] = ToByte(g * factor);
                pixels[o + 2] = ToByte(b * factor);
            }
        }

        if (regions is not null && regions.CellsPerSide > 0)
        {
            DrawRegionBorders(pixels, width, height, field, step, regions);
        }

        if (network is not null)
        {
            DrawNetwork(pixels, width, height, field, step, network);
        }

        return pixels;
    }

    private static void DrawRegionBorders(byte[] pixels, int width, int height, HeightField field, int step, RegionField regions)
    {
        double half = field.WorldSizeMeters / HalfDivisor;
        double metresPerPixel = step * (double)field.SpacingMeters;
        double last = field.WorldSizeMeters - 1;
        ushort At(int px, int py) => regions.CellValue(
            Math.Clamp((int)Math.Floor((Math.Min((px * metresPerPixel) - half, last - half) + half) / regions.CellMeters), 0, regions.CellsPerSide - 1),
            Math.Clamp((int)Math.Floor((Math.Min((py * metresPerPixel) - half, last - half) + half) / regions.CellMeters), 0, regions.CellsPerSide - 1));

        var marks = new bool[width * height];
        for (int py = 0; py < height; py++)
        {
            for (int px = 0; px < width; px++)
            {
                ushort here = At(px, py);
                bool border = (px + 1 < width && At(px + 1, py) != here) || (py + 1 < height && At(px, py + 1) != here);
                marks[(py * width) + px] = border;
            }
        }

        for (int i = 0; i < marks.Length; i++)
        {
            if (!marks[i])
            {
                continue;
            }

            int o = i * BytesPerPixel;
            for (int c = 0; c < BytesPerPixel; c++)
            {
                pixels[o + c] = (byte)(pixels[o + c] + ((255 - pixels[o + c]) * BorderWhiteShare / 100));
            }
        }
    }

    private static void DrawNetwork(byte[] pixels, int width, int height, HeightField field, int step, RiverNetwork network)
    {
        double half = field.WorldSizeMeters / HalfDivisor;
        double metresPerPixel = step * (double)field.SpacingMeters;

        foreach (Lake lake in network.Lakes)
        {
            foreach ((double x, double z) in lake.Cells)
            {
                Plot(pixels, width, height, (int)Math.Round((x + half) / metresPerPixel), (int)Math.Round((z + half) / metresPerPixel), 1);
            }
        }

        foreach (River river in network.Rivers)
        {
            for (int i = 1; i < river.Points.Count; i++)
            {
                RiverPoint a = river.Points[i - 1];
                RiverPoint b = river.Points[i];
                int radius = (int)Math.Round(Math.Max(a.HalfWidth, b.HalfWidth) / metresPerPixel); // the real width, so estuaries and pools show
                Line(
                    pixels,
                    width,
                    height,
                    ((a.X + half) / metresPerPixel, (a.Z + half) / metresPerPixel),
                    ((b.X + half) / metresPerPixel, (b.Z + half) / metresPerPixel),
                    radius);
            }
        }
    }

    private static void Line(byte[] pixels, int width, int height, (double X, double Y) from, (double X, double Y) to, int radius)
    {
        double length = Math.Max(Math.Abs(to.X - from.X), Math.Abs(to.Y - from.Y));
        int steps = Math.Max(1, (int)Math.Ceiling(length * 2));
        for (int s = 0; s <= steps; s++)
        {
            double t = s / (double)steps;
            Plot(pixels, width, height, (int)Math.Round(from.X + ((to.X - from.X) * t)), (int)Math.Round(from.Y + ((to.Y - from.Y) * t)), radius);
        }
    }

    private static void Plot(byte[] pixels, int width, int height, int px, int py, int radius)
    {
        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                int x = px + dx;
                int y = py + dy;
                if ((uint)x >= (uint)width || (uint)y >= (uint)height)
                {
                    continue;
                }

                int o = ((y * width) + x) * BytesPerPixel;
                pixels[o] = TerrainColouring.WaterRed;
                pixels[o + 1] = TerrainColouring.WaterGreen;
                pixels[o + 2] = TerrainColouring.WaterBlue;
            }
        }
    }

    private static byte ToByte(double value) => (byte)Math.Clamp(Math.Round(value), 0, 255);
}
