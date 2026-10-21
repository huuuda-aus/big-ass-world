using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Realm.Core.Validation;
using Realm.Gen.Settlements.Roads;
using Realm.Gen.Terrain;
using Realm.Gen.Terrain.Erosion;

namespace Realm.Gen.Bake;

/// <summary>
/// Cuts a height field into region tiles for streaming (CLAUDE.md section 4.4: generate, bake, edit), paints rivers
/// and lakes into the colour maps, and writes the tiles plus a manifest.
/// </summary>
/// <remarks>
/// Region (0, 0) starts at the world origin. A region of <c>size</c> vertices at <c>spacing</c> metres is
/// <c>size x spacing</c> metres wide and holds the field vertices from its own corner up to, but not including,
/// its neighbour's corner, so adjacent regions share no duplicate edge vertices.
/// Determinism: tile contents are a pure function of the field and the river network; tiles are written in
/// parallel but never share data. Thread-safety: stateless.
/// </remarks>
public static class TerrainBaker
{
    /// <summary>Default vertices per region side (see ADR 0009).</summary>
    public const int DefaultRegionSize = 256;

    /// <summary>Terrain3D 1.0.2 holds at most this many regions per axis (ADR 0009).</summary>
    public const int DefaultMaxRegionsPerAxis = 32;

    private const double HalfDivisor = 2.0;
    private const double ShadeStrength = 0.9;   // how strongly the slope toward the light brightens (and away darkens) the colour
    private const double ShadeMinimum = 0.45;
    private const double ShadeMaximum = 1.35;
    private const double RoadRed = 168;
    private const double RoadGreen = 140;
    private const double RoadBlue = 100;
    private const double RoadPaintMargin = 0.5; // paint a little wider than the surface so a 4 m vertex grid always catches it
    private const double RiverMinimumHalfWidthMeters = 2.0;
    private const double RiverHalfWidthPerRootKm2 = 0.6;
    private const ulong Fnv64Offset = 14695981039346656037UL;
    private const ulong Fnv64Prime = 1099511628211UL;

    /// <summary>Checks the bake settings against a field.</summary>
    /// <param name="field">The height field.</param>
    /// <param name="regionSize">Vertices per region side.</param>
    /// <param name="maxRegionsPerAxis">Largest allowed number of regions per axis.</param>
    /// <param name="problem">The first problem found; otherwise null.</param>
    /// <returns><c>true</c> when valid.</returns>
    public static bool TryValidate(HeightField field, int regionSize, int maxRegionsPerAxis, out string? problem)
    {
        ArgumentNullException.ThrowIfNull(field);
        int extent = regionSize * field.SpacingMeters;
        if (regionSize < 2)
        {
            problem = "RegionSize must be at least 2.";
        }
        else if (field.WorldSizeMeters % extent != 0)
        {
            problem = $"A region is {extent} m wide ({regionSize} vertices x {field.SpacingMeters} m) and does not divide the world size {field.WorldSizeMeters}.";
        }
        else if (field.WorldSizeMeters / extent > maxRegionsPerAxis)
        {
            problem = $"The world needs {field.WorldSizeMeters / extent} regions per axis but at most {maxRegionsPerAxis} are allowed.";
        }
        else if ((field.WorldSizeMeters / extent) % 2 != 0)
        {
            problem = $"The world must hold an even number of regions per axis (has {field.WorldSizeMeters / extent}) so that the origin is a region corner.";
        }
        else
        {
            problem = null;
            return true;
        }

        return false;
    }

    /// <summary>Builds one region tile.</summary>
    /// <param name="field">The height field.</param>
    /// <param name="network">Rivers and lakes to paint; may be null.</param>
    /// <param name="regionX">Region coordinate, east.</param>
    /// <param name="regionZ">Region coordinate, south.</param>
    /// <param name="regionSize">Vertices per region side; must already be validated.</param>
    /// <param name="roads">Roads to paint into the colour map, or null.</param>
    /// <returns>The tile.</returns>
    public static RegionTile BuildTile(HeightField field, RiverNetwork? network, int regionX, int regionZ, int regionSize, IReadOnlyList<Road>? roads = null)
    {
        ArgumentNullException.ThrowIfNull(field);
        int regionsPerAxis = field.WorldSizeMeters / (regionSize * field.SpacingMeters);
        int originVertexX = (regionX + (regionsPerAxis / 2)) * regionSize;
        int originVertexZ = (regionZ + (regionsPerAxis / 2)) * regionSize;
        double half = field.WorldSizeMeters / HalfDivisor;

        var heights = new float[regionSize * regionSize];
        var colours = new uint[heights.Length];
        double top = field.MaxHeight;
        double depth = -field.MinHeight;
        for (int iz = 0; iz < regionSize; iz++)
        {
            for (int ix = 0; ix < regionSize; ix++)
            {
                float h = field.At(originVertexX + ix, originVertexZ + iz);
                int i = (iz * regionSize) + ix;
                heights[i] = h;
                (double slopeX, double slopeZ) = Slopes(field, originVertexX + ix, originVertexZ + iz);
                (double r, double g, double b) = TerrainColouring.Albedo(h, top, depth, Math.Sqrt((slopeX * slopeX) + (slopeZ * slopeZ)));
                double shade = Math.Clamp(1.0 + (ShadeStrength * (slopeX + slopeZ)), ShadeMinimum, ShadeMaximum);
                colours[i] = TerrainColouring.Pack(r * shade, g * shade, b * shade);
            }
        }

        if (network is not null)
        {
            double tileWestX = -half + (originVertexX * (double)field.SpacingMeters);
            double tileNorthZ = -half + (originVertexZ * (double)field.SpacingMeters);
            PaintWater(colours, regionSize, field.SpacingMeters, tileWestX, tileNorthZ, network);
        }

        if (roads is { Count: > 0 })
        {
            double westX = -half + (originVertexX * (double)field.SpacingMeters);
            double northZ = -half + (originVertexZ * (double)field.SpacingMeters);
            PaintRoads(colours, regionSize, field.SpacingMeters, westX, northZ, roads);
        }

        return new RegionTile(regionX, regionZ, regionSize, field.SpacingMeters, heights, colours);
    }

    // Ground slope in X and Z (rise over run). It drives the baked hill shading, lit from the north-west (a surface that
    // rises toward the south-east faces the light and brightens; one that falls toward the south-east darkens), and the
    // rock colour on steep ground. It reads the height field's own neighbours, so tile edges match seamlessly.
    private static (double X, double Z) Slopes(HeightField field, int vertexX, int vertexZ)
    {
        int last = field.VerticesPerSide - 1;
        double slopeX = (field.At(Math.Min(vertexX + 1, last), vertexZ) - field.At(Math.Max(vertexX - 1, 0), vertexZ))
                        / (2.0 * field.SpacingMeters);
        double slopeZ = (field.At(vertexX, Math.Min(vertexZ + 1, last)) - field.At(vertexX, Math.Max(vertexZ - 1, 0)))
                        / (2.0 * field.SpacingMeters);
        return (slopeX, slopeZ);
    }

    /// <summary>Bakes every region of a field to files and writes the manifest.</summary>
    /// <param name="field">The height field.</param>
    /// <param name="network">Rivers and lakes; may be null.</param>
    /// <param name="worldId">Content ID of the world map, recorded in the manifest.</param>
    /// <param name="seed">World seed, recorded in the manifest.</param>
    /// <param name="generators">Generators that produced the field, recorded in the manifest.</param>
    /// <param name="outputDirectory">Directory for the tiles and manifest; created when missing.</param>
    /// <param name="report">Receives problems.</param>
    /// <param name="regionSize">Vertices per region side.</param>
    /// <param name="maxRegionsPerAxis">Largest allowed number of regions per axis.</param>
    /// <param name="spawn">Where the player starts (world X and Z); by default beside the middle of the longest river.</param>
    /// <param name="roads">Roads to paint into the colour maps, or null.</param>
    /// <returns>The manifest, or null when the settings were invalid.</returns>
    public static TerrainManifest? Bake(
        HeightField field,
        RiverNetwork? network,
        string worldId,
        ulong seed,
        IReadOnlyList<string> generators,
        string outputDirectory,
        ValidationReport report,
        int regionSize = DefaultRegionSize,
        int maxRegionsPerAxis = DefaultMaxRegionsPerAxis,
        (double X, double Z)? spawn = null,
        IReadOnlyList<Road>? roads = null)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(outputDirectory);
        ArgumentNullException.ThrowIfNull(report);

        if (!TryValidate(field, regionSize, maxRegionsPerAxis, out string? problem))
        {
            report.Error(TerrainCodes.BakeSettingsInvalid, worldId, problem!);
            return null;
        }

        int extent = regionSize * field.SpacingMeters;
        int perAxis = field.WorldSizeMeters / extent;
        int min = -perAxis / 2;
        int max = (perAxis / 2) - 1;
        Directory.CreateDirectory(outputDirectory);

        int count = perAxis * perAxis;
        var checksums = new ulong[count];
        Parallel.For(0, count, index =>
        {
            int regionX = min + (index % perAxis);
            int regionZ = min + (index / perAxis);
            RegionTile tile = BuildTile(field, network, regionX, regionZ, regionSize, roads);
            byte[] bytes = RegionTileFile.ToBytes(tile);
            checksums[index] = BitConverter.ToUInt64(bytes, bytes.Length - sizeof(ulong));
            string path = Path.Combine(outputDirectory, RegionTile.FileName(regionX, regionZ));
            string temporary = path + ".tmp";
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path, overwrite: true);
        });

        ulong combined = Fnv64Offset;
        foreach (ulong checksum in checksums)
        {
            for (int shift = 0; shift < 64; shift += 8)
            {
                combined = unchecked((combined ^ ((checksum >> shift) & 0xFFUL)) * Fnv64Prime);
            }
        }

        (double spawnX, double spawnZ) = spawn ?? ChooseSpawn(field, network);
        var manifest = new TerrainManifest
        {
            World = worldId,
            WorldSizeMeters = field.WorldSizeMeters,
            SpacingMeters = field.SpacingMeters,
            RegionSize = regionSize,
            RegionExtentMeters = extent,
            RegionsPerAxis = perAxis,
            MinRegion = min,
            MaxRegion = max,
            Seed = seed,
            Generators = generators,
            MinHeight = field.MinHeight,
            MaxHeight = field.MaxHeight,
            RiverCount = network?.Rivers.Count ?? 0,
            LakeCount = network?.Lakes.Count ?? 0,
            SpawnX = spawnX,
            SpawnZ = spawnZ,
            Checksum = combined.ToString("x16"),
        };
        File.WriteAllText(Path.Combine(outputDirectory, TerrainManifest.FileName), manifest.ToJson());
        return manifest;
    }

    // Start beside the middle of the longest river when there is one (a good place to walk), otherwise the world centre.
    private static (double X, double Z) ChooseSpawn(HeightField field, RiverNetwork? network)
    {
        if (network is { Rivers.Count: > 0 })
        {
            River river = network.Rivers[0];
            RiverPoint middle = river.Points[river.Points.Count / 2];
            double offset = field.SpacingMeters * 4.0;
            double x = Math.Clamp(middle.X + offset, -field.WorldSizeMeters / HalfDivisor, (field.WorldSizeMeters / HalfDivisor) - 1);
            return (x, middle.Z);
        }

        return (0, 0);
    }

    /// <summary>Half the painted width of a river with the given flow, in metres.</summary>
    /// <param name="flowKm2">Flow in square kilometres of catchment.</param>
    /// <returns>Half width: at least 2 m, growing with the square root of the flow.</returns>
    public static double RiverHalfWidthMeters(double flowKm2) =>
        Math.Max(RiverMinimumHalfWidthMeters, RiverHalfWidthPerRootKm2 * Math.Sqrt(Math.Max(0, flowKm2)));

    private static void PaintRoads(uint[] colours, int size, int spacing, double westX, double northZ, IReadOnlyList<Road> roads)
    {
        double tileEast = westX + (size * (double)spacing);
        double tileSouth = northZ + (size * (double)spacing);
        uint surface = TerrainColouring.Pack(RoadRed, RoadGreen, RoadBlue);
        foreach (Road road in roads)
        {
            double halfWidth = (road.WidthMeters / 2) + RoadPaintMargin;
            for (int i = 1; i < road.Points.Count; i++)
            {
                RoadPoint a = road.Points[i - 1];
                RoadPoint b = road.Points[i];
                double minX = Math.Min(a.X, b.X) - halfWidth;
                double maxX = Math.Max(a.X, b.X) + halfWidth;
                double minZ = Math.Min(a.Z, b.Z) - halfWidth;
                double maxZ = Math.Max(a.Z, b.Z) + halfWidth;
                if (maxX < westX || minX > tileEast || maxZ < northZ || minZ > tileSouth)
                {
                    continue;
                }

                int ix0 = Math.Max(0, (int)Math.Floor((minX - westX) / spacing));
                int ix1 = Math.Min(size - 1, (int)Math.Ceiling((maxX - westX) / spacing));
                int iz0 = Math.Max(0, (int)Math.Floor((minZ - northZ) / spacing));
                int iz1 = Math.Min(size - 1, (int)Math.Ceiling((maxZ - northZ) / spacing));
                for (int iz = iz0; iz <= iz1; iz++)
                {
                    for (int ix = ix0; ix <= ix1; ix++)
                    {
                        double x = westX + (ix * (double)spacing);
                        double z = northZ + (iz * (double)spacing);
                        if (DistanceToSegment(x, z, a.X, a.Z, b.X, b.Z) <= halfWidth)
                        {
                            colours[(iz * size) + ix] = surface;
                        }
                    }
                }
            }
        }
    }

    private static void PaintWater(uint[] colours, int size, int spacing, double westX, double northZ, RiverNetwork network)
    {
        uint water = TerrainColouring.WaterPacked;
        double tileEast = westX + (size * (double)spacing);
        double tileSouth = northZ + (size * (double)spacing);

        foreach (Lake lake in network.Lakes)
        {
            double halfCell = Math.Max(spacing, network.WorkingSpacingMeters) / HalfDivisor;
            foreach ((double cx, double cz) in lake.Cells)
            {
                if (cx + halfCell < westX || cx - halfCell > tileEast || cz + halfCell < northZ || cz - halfCell > tileSouth)
                {
                    continue;
                }

                PaintBox(colours, size, spacing, westX, northZ, cx - halfCell, cz - halfCell, cx + halfCell, cz + halfCell, water);
            }
        }

        foreach (River river in network.Rivers)
        {
            for (int i = 1; i < river.Points.Count; i++)
            {
                RiverPoint a = river.Points[i - 1];
                RiverPoint b = river.Points[i];
                double halfWidth = Math.Max(RiverMinimumHalfWidthMeters, RiverHalfWidthPerRootKm2 * Math.Sqrt(b.FlowKm2));
                double minX = Math.Min(a.X, b.X) - halfWidth;
                double maxX = Math.Max(a.X, b.X) + halfWidth;
                double minZ = Math.Min(a.Z, b.Z) - halfWidth;
                double maxZ = Math.Max(a.Z, b.Z) + halfWidth;
                if (maxX < westX || minX > tileEast || maxZ < northZ || minZ > tileSouth)
                {
                    continue;
                }

                int ix0 = Math.Max(0, (int)Math.Floor((minX - westX) / spacing));
                int ix1 = Math.Min(size - 1, (int)Math.Ceiling((maxX - westX) / spacing));
                int iz0 = Math.Max(0, (int)Math.Floor((minZ - northZ) / spacing));
                int iz1 = Math.Min(size - 1, (int)Math.Ceiling((maxZ - northZ) / spacing));
                for (int iz = iz0; iz <= iz1; iz++)
                {
                    for (int ix = ix0; ix <= ix1; ix++)
                    {
                        double x = westX + (ix * (double)spacing);
                        double z = northZ + (iz * (double)spacing);
                        if (DistanceToSegment(x, z, a.X, a.Z, b.X, b.Z) <= halfWidth)
                        {
                            colours[(iz * size) + ix] = water;
                        }
                    }
                }
            }
        }
    }

    private static void PaintBox(
        uint[] colours, int size, int spacing, double westX, double northZ, double minX, double minZ, double maxX, double maxZ, uint colour)
    {
        int ix0 = Math.Max(0, (int)Math.Ceiling((minX - westX) / spacing));
        int ix1 = Math.Min(size - 1, (int)Math.Floor((maxX - westX) / spacing));
        int iz0 = Math.Max(0, (int)Math.Ceiling((minZ - northZ) / spacing));
        int iz1 = Math.Min(size - 1, (int)Math.Floor((maxZ - northZ) / spacing));
        for (int iz = iz0; iz <= iz1; iz++)
        {
            for (int ix = ix0; ix <= ix1; ix++)
            {
                colours[(iz * size) + ix] = colour;
            }
        }
    }

    private static double DistanceToSegment(double px, double pz, double ax, double az, double bx, double bz)
    {
        double dx = bx - ax;
        double dz = bz - az;
        double lengthSquared = (dx * dx) + (dz * dz);
        double t = lengthSquared == 0 ? 0 : Math.Clamp((((px - ax) * dx) + ((pz - az) * dz)) / lengthSquared, 0, 1);
        double cx = ax + (t * dx);
        double cz = az + (t * dz);
        return Math.Sqrt(((px - cx) * (px - cx)) + ((pz - cz) * (pz - cz)));
    }
}
