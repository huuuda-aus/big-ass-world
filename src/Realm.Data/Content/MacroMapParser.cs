using System;
using System.Collections.Generic;
using Realm.Core.Geometry;
using Realm.Core.Ids;
using Realm.Core.Validation;

namespace Realm.Data.Content;

/// <summary>Parses and validates one <c>world</c> (macro map) YAML record.</summary>
/// <remarks>
/// Checks each polygon on its own (valid, simple, inside the world). Overlaps and gaps between
/// regions are checked by the terrain generator's rasteriser. Determinism: no randomness.
/// Thread-safety: stateless.
/// </remarks>
public static class MacroMapParser
{
    private const string SchemaPage = "docs/reference/content/world.md";
    private const string RegionType = "region";
    private const string BiomeType = "biome";
    private const double HalfDivisor = 2.0;

    /// <summary>Parses one YAML document into a macro map, reporting every problem found.</summary>
    /// <param name="yaml">The YAML text.</param>
    /// <param name="location">File location used in issues, for example <c>world/main.yaml</c>.</param>
    /// <param name="report">Receives all issues.</param>
    /// <returns>The macro map, or null when any error prevented building it.</returns>
    /// <remarks>Whether a region's biome exists is checked later by <see cref="ContentValidator"/>.</remarks>
    public static MacroMapDefinition? Parse(string yaml, string location, ValidationReport report)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(report);

        if (!ParserSupport.TryDeserialize(yaml, location, SchemaPage, report, out MacroMapDto dto))
        {
            return null;
        }

        int errorsBefore = report.Count(Severity.Error);

        ContentId id = ParserSupport.ParseId(dto.Id, location, report);
        ParserSupport.RequireName(dto.Name, location, report);

        int worldSize = 0;
        if (dto.WorldSizeM is null)
        {
            report.Error(ValidatorCodes.MissingField, location, "Missing required field 'world_size_m'.");
        }
        else if (dto.WorldSizeM <= 0)
        {
            report.Error(
                ValidatorCodes.InvalidWorldSize,
                location,
                $"world_size_m must be positive but is {dto.WorldSizeM}.");
        }
        else
        {
            worldSize = dto.WorldSizeM.Value;
        }

        var regions = new List<RegionDefinition>();
        var seen = new HashSet<ContentId>();
        if (dto.Regions is null || dto.Regions.Count == 0)
        {
            report.Error(ValidatorCodes.MissingField, location, "Missing required field 'regions' (at least one region).");
        }
        else
        {
            foreach (RegionDto regionDto in dto.Regions)
            {
                RegionDefinition? region = ParseRegion(regionDto, worldSize, location, report);
                if (region is null)
                {
                    continue;
                }

                if (!seen.Add(region.Id))
                {
                    report.Error(
                        ValidatorCodes.DuplicateId,
                        location,
                        $"Region '{region.Id}' is defined twice in this file.",
                        "Give each region a unique ID.");
                    continue;
                }

                regions.Add(region);
            }
        }

        if (report.Count(Severity.Error) > errorsBefore)
        {
            return null;
        }

        // Safe: a missing or blank name was reported as an error above, which returned null.
        return new MacroMapDefinition(id, dto.Name!, worldSize, regions);
    }

    private static RegionDefinition? ParseRegion(RegionDto dto, int worldSize, string location, ValidationReport report)
    {
        int errorsBefore = report.Count(Severity.Error);

        ContentId id = ParserSupport.ParseId(dto.Id, location, report);
        string label = id.Value.Length > 0 ? $"Region '{id}'" : "A region";

        if (id.Value.Length > 0 && id.Type != RegionType)
        {
            report.Error(
                ValidatorCodes.IdTypeMismatch,
                location,
                $"{label} has type '{id.Type}' but regions must start with '{RegionType}.'.",
                $"Rename it to '{RegionType}.{id.Value[(id.Type.Length + 1)..]}'.");
        }

        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            report.Error(ValidatorCodes.MissingField, location, $"{label}: missing required field 'name'.");
        }

        ContentId? biome = null;
        if (dto.Biome is null)
        {
            report.Error(ValidatorCodes.MissingField, location, $"{label}: missing required field 'biome'.");
        }
        else
        {
            biome = ParserSupport.ParseReference(dto.Biome, "biome", BiomeType, location, report);
        }

        var polygons = new List<Polygon>();
        if (dto.Polygons is null || dto.Polygons.Count == 0)
        {
            report.Error(ValidatorCodes.MissingField, location, $"{label}: missing required field 'polygons' (at least one).");
        }
        else
        {
            for (int ring = 0; ring < dto.Polygons.Count; ring++)
            {
                Polygon? polygon = ParsePolygon(dto.Polygons[ring], ring, label, worldSize, location, report);
                if (polygon is not null)
                {
                    polygons.Add(polygon);
                }
            }
        }

        if (report.Count(Severity.Error) > errorsBefore)
        {
            return null;
        }

        // Safe: a missing name or biome was reported as an error above, which returned null.
        return new RegionDefinition(id, dto.Name!, biome!.Value, polygons);
    }

    private static Polygon? ParsePolygon(
        List<List<double>>? raw, int ring, string label, int worldSize, string location, ValidationReport report)
    {
        string where = $"{label}, polygon {ring}";
        var points = new List<Point2D>();
        if (raw is null)
        {
            report.Error(ValidatorCodes.InvalidPolygon, location, $"{where}: is empty.");
            return null;
        }

        for (int i = 0; i < raw.Count; i++)
        {
            List<double>? pair = raw[i];
            if (pair is null || pair.Count != 2)
            {
                report.Error(
                    ValidatorCodes.InvalidPolygon,
                    location,
                    $"{where}: vertex {i} must be [x, z] (two numbers).",
                    "Write each vertex as [east_metres, south_metres].");
                return null;
            }

            points.Add(new Point2D(pair[0], pair[1]));
        }

        if (!Polygon.TryCreate(points, out Polygon? polygon, out string? problem))
        {
            report.Error(ValidatorCodes.InvalidPolygon, location, $"{where}: {problem}");
            return null;
        }

        if (worldSize > 0)
        {
            double half = worldSize / HalfDivisor;
            // Safe: TryCreate returned true, so the polygon is not null.
            if (polygon!.MinX < -half || polygon.MaxX > half || polygon.MinZ < -half || polygon.MaxZ > half)
            {
                report.Error(
                    ValidatorCodes.PolygonOutsideWorld,
                    location,
                    $"{where}: extends outside the world (allowed range is -{half} to {half} metres on both axes).",
                    "Move the vertices inside the world or increase world_size_m.");
                return null;
            }
        }

        return polygon;
    }

    private sealed class MacroMapDto
    {
        public string? Id { get; set; }

        public string? Name { get; set; }

        public int? WorldSizeM { get; set; }

        public List<RegionDto>? Regions { get; set; }
    }

    private sealed class RegionDto
    {
        public string? Id { get; set; }

        public string? Name { get; set; }

        public string? Biome { get; set; }

        public List<List<List<double>>>? Polygons { get; set; }
    }
}
