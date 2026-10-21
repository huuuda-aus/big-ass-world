using System;
using System.Collections.Generic;
using System.Text.Json;
using Realm.Core.Validation;

namespace Realm.Gen.Settlements.Roads;

/// <summary>The baked roads (<c>roads.json</c> next to the terrain tiles).</summary>
/// <remarks>Determinism: serialisation is canonical (fixed field order, invariant culture).</remarks>
public sealed class RoadManifest
{
    /// <summary>File name inside the baked directory.</summary>
    public const string FileName = "roads.json";

    /// <summary>The format version written and accepted.</summary>
    public const int CurrentFormat = 1;

    /// <summary>Format version of the file.</summary>
    public int Format { get; init; } = CurrentFormat;

    /// <summary>Content ID of the world map.</summary>
    public string World { get; init; } = string.Empty;

    /// <summary>World seed.</summary>
    public ulong Seed { get; init; }

    /// <summary>Generators that produced the roads, as <c>id vN</c>.</summary>
    public IReadOnlyList<string> Generators { get; init; } = Array.Empty<string>();

    /// <summary>The roads.</summary>
    public IReadOnlyList<Road> Roads { get; init; } = Array.Empty<Road>();

    /// <summary>Serialises to JSON.</summary>
    /// <returns>Compact JSON text (road points are many).</returns>
    public string ToJson()
    {
        var dto = new Dto { Format = Format, World = World, Seed = Seed, Generators = new List<string>(Generators) };
        foreach (Road r in Roads)
        {
            var road = new RoadDto { Id = r.Id, From = r.From, To = r.To, Width = r.WidthMeters };
            foreach (RoadPoint p in r.Points)
            {
                road.Points.Add(new[] { p.X, p.Z, p.Height });
            }

            foreach (RoadBridge b in r.Bridges)
            {
                road.Bridges.Add(new BridgeDto
                {
                    X = b.X, Z = b.Z, Yaw = b.YawRadians, Length = b.LengthMeters, Width = b.WidthMeters,
                    StartHeight = b.StartHeight, EndHeight = b.EndHeight, First = b.FirstPoint, Last = b.LastPoint,
                });
            }

            dto.Roads.Add(road);
        }

        return JsonSerializer.Serialize(dto, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }

    /// <summary>Reads a manifest.</summary>
    /// <param name="json">The file text.</param>
    /// <param name="report">Receives <see cref="RoadCodes.ManifestInvalid"/> when the text is unusable.</param>
    /// <returns>The manifest, or null when the text is damaged or its format is unsupported.</returns>
    public static RoadManifest? FromJson(string json, ValidationReport report)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(report);
        try
        {
            Dto? dto = JsonSerializer.Deserialize<Dto>(json, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            if (dto is null || dto.Format != CurrentFormat)
            {
                report.Error(RoadCodes.ManifestInvalid, FileName, $"Unsupported roads format {dto?.Format.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "(none)"}; expected {CurrentFormat}.");
                return null;
            }

            var roads = new List<Road>();
            foreach (RoadDto r in dto.Roads)
            {
                var points = new List<RoadPoint>();
                foreach (double[] p in r.Points)
                {
                    if (p.Length != 3)
                    {
                        report.Error(RoadCodes.ManifestInvalid, FileName, $"A point of '{r.Id}' does not have x, z and height.");
                        return null;
                    }

                    points.Add(new RoadPoint(p[0], p[1], p[2]));
                }

                var bridges = new List<RoadBridge>();
                foreach (BridgeDto b in r.Bridges)
                {
                    bridges.Add(new RoadBridge(b.X, b.Z, b.Yaw, b.Length, b.Width, b.StartHeight, b.EndHeight, b.First, b.Last));
                }

                roads.Add(new Road(r.Id, r.From, r.To, r.Width, points, bridges));
            }

            return new RoadManifest { Format = dto.Format, World = dto.World, Seed = dto.Seed, Generators = dto.Generators, Roads = roads };
        }
        catch (JsonException e)
        {
            report.Error(RoadCodes.ManifestInvalid, FileName, $"The roads file is not valid JSON: {e.Message}");
            return null;
        }
    }

    private sealed class Dto
    {
        public int Format { get; set; }

        public string World { get; set; } = string.Empty;

        public ulong Seed { get; set; }

        public List<string> Generators { get; set; } = new();

        public List<RoadDto> Roads { get; set; } = new();
    }

    private sealed class RoadDto
    {
        public string Id { get; set; } = string.Empty;

        public string From { get; set; } = string.Empty;

        public string To { get; set; } = string.Empty;

        public double Width { get; set; }

        public List<double[]> Points { get; set; } = new();

        public List<BridgeDto> Bridges { get; set; } = new();
    }

    private sealed class BridgeDto
    {
        public double X { get; set; }

        public double Z { get; set; }

        public double Yaw { get; set; }

        public double Length { get; set; }

        public double Width { get; set; }

        public double StartHeight { get; set; }

        public double EndHeight { get; set; }

        public int First { get; set; }

        public int Last { get; set; }
    }
}
