using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Realm.Core.Validation;
using Realm.Gen.Settlements.Model;

namespace Realm.Gen.Settlements.Placement;

/// <summary>The baked list of settlements (<c>settlements.json</c> next to the terrain tiles).</summary>
/// <remarks>Determinism: serialisation is canonical (fixed field order, invariant culture), so the same list gives the same text.</remarks>
public sealed class SettlementManifest
{
    /// <summary>File name inside the baked directory.</summary>
    public const string FileName = "settlements.json";

    /// <summary>The format version written and accepted.</summary>
    public const int CurrentFormat = 1;

    /// <summary>Format version of the file.</summary>
    public int Format { get; init; } = CurrentFormat;

    /// <summary>Content ID of the world map.</summary>
    public string World { get; init; } = string.Empty;

    /// <summary>World seed.</summary>
    public ulong Seed { get; init; }

    /// <summary>Generators that produced the list, as <c>id vN</c>.</summary>
    public IReadOnlyList<string> Generators { get; init; } = Array.Empty<string>();

    /// <summary>The settlements.</summary>
    public IReadOnlyList<Settlement> Settlements { get; init; } = Array.Empty<Settlement>();

    /// <summary>Serialises to JSON.</summary>
    /// <returns>Indented JSON text.</returns>
    public string ToJson()
    {
        var dto = new Dto
        {
            Format = Format,
            World = World,
            Seed = Seed,
            Generators = new List<string>(Generators),
            Settlements = new List<SettlementDto>(),
        };
        foreach (Settlement s in Settlements)
        {
            dto.Settlements.Add(new SettlementDto
            {
                Id = s.Id, Name = s.Name, Tier = s.Tier.ToString(), X = s.X, Z = s.Z, Rotation = s.RotationRadians,
                GroundHeight = s.GroundHeightMeters, WallRadius = s.WallRadiusMeters, PadRadius = s.PadRadiusMeters,
                CitySeed = s.CitySeed, MaxBuildings = s.MaxBuildings, Cathedral = s.Cathedral, Centre = s.Centre.ToString(),
                Services = s.ServiceList.Select(ServiceDto.From).ToList(), Highland = s.Highland, Layout = s.Layout.ToString(),
            });
        }

        return JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }

    /// <summary>Reads a manifest.</summary>
    /// <param name="json">The file text.</param>
    /// <param name="report">Receives <see cref="SettlementCodes.ManifestInvalid"/> when the text is unusable.</param>
    /// <returns>The manifest, or null when the text is damaged or its format is unsupported.</returns>
    public static SettlementManifest? FromJson(string json, ValidationReport report)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(report);
        try
        {
            Dto? dto = JsonSerializer.Deserialize<Dto>(json, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            if (dto is null || dto.Format != CurrentFormat)
            {
                report.Error(SettlementCodes.ManifestInvalid, FileName, $"Unsupported settlements format {dto?.Format.ToString(CultureInfo.InvariantCulture) ?? "(none)"}; expected {CurrentFormat}.");
                return null;
            }

            var list = new List<Settlement>();
            foreach (SettlementDto s in dto.Settlements)
            {
                if (!Enum.TryParse(s.Tier, out SettlementTier tier) || !Enum.TryParse(s.Centre, out CentrepieceKind centre) || !Enum.TryParse(s.Layout, out SettlementLayout layout))
                {
                    report.Error(SettlementCodes.ManifestInvalid, FileName, $"Settlement '{s.Id}' has an unknown tier '{s.Tier}', centre '{s.Centre}' or layout '{s.Layout}'.");
                    return null;
                }

                var services = new List<ServiceSpec>();
                foreach (ServiceDto service in s.Services)
                {
                    ServiceSpec? spec = service.ToSpec();
                    if (spec is null)
                    {
                        report.Error(SettlementCodes.ManifestInvalid, FileName, $"Settlement '{s.Id}' has a service '{service.Id}' with an unknown ward, placement, foundation or room word.");
                        return null;
                    }

                    services.Add(spec);
                }

                list.Add(new Settlement(s.Id, s.Name, tier, s.X, s.Z, s.Rotation, s.GroundHeight, s.WallRadius, s.PadRadius, s.CitySeed, s.MaxBuildings, s.Cathedral, centre, services.Count == 0 ? null : services, s.Highland, layout));
            }

            return new SettlementManifest { Format = dto.Format, World = dto.World, Seed = dto.Seed, Generators = dto.Generators, Settlements = list };
        }
        catch (JsonException e)
        {
            report.Error(SettlementCodes.ManifestInvalid, FileName, $"The settlements file is not valid JSON: {e.Message}");
            return null;
        }
    }

    private sealed class Dto
    {
        public int Format { get; set; }

        public string World { get; set; } = string.Empty;

        public ulong Seed { get; set; }

        public List<string> Generators { get; set; } = new();

        public List<SettlementDto> Settlements { get; set; } = new();
    }

    private sealed class SettlementDto
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Tier { get; set; } = string.Empty;

        public double X { get; set; }

        public double Z { get; set; }

        public double Rotation { get; set; }

        public double GroundHeight { get; set; }

        public double WallRadius { get; set; }

        public double PadRadius { get; set; }

        public int CitySeed { get; set; }

        public int MaxBuildings { get; set; }

        public bool Cathedral { get; set; }

        public string Centre { get; set; } = string.Empty;

        public List<ServiceDto> Services { get; set; } = new();

        public bool Highland { get; set; }

        public string Layout { get; set; } = nameof(SettlementLayout.Radial);
    }

    private sealed class ServiceDto
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public int Count { get; set; }

        public List<string> Wards { get; set; } = new();

        public string Near { get; set; } = nameof(ServiceNear.Any);

        public bool OutsideWall { get; set; }

        public double MinArea { get; set; }

        public string? Foundation { get; set; }

        public string MainRoom { get; set; } = nameof(RoomType.Hall);

        public static ServiceDto From(ServiceSpec spec) => new()
        {
            Id = spec.Id, Name = spec.Name, Count = spec.Count, Wards = spec.Wards.Select(w => w.ToString()).ToList(), Near = spec.Near.ToString(),
            OutsideWall = spec.OutsideWall, MinArea = spec.MinAreaMeters2, Foundation = spec.Foundation?.ToString(), MainRoom = spec.MainRoom.ToString(),
        };

        public ServiceSpec? ToSpec()
        {
            var wards = new List<WardType>();
            foreach (string w in Wards)
            {
                if (!Enum.TryParse(w, out WardType ward))
                {
                    return null;
                }

                wards.Add(ward);
            }

            FoundationStyle? foundation = null;
            if (Foundation is not null)
            {
                if (!Enum.TryParse(Foundation, out FoundationStyle style))
                {
                    return null;
                }

                foundation = style;
            }

            if (!Enum.TryParse(Near, out ServiceNear near) || !Enum.TryParse(MainRoom, out RoomType room))
            {
                return null;
            }

            return new ServiceSpec(Id, Name, Count, wards, near, OutsideWall, MinArea, foundation, room);
        }
    }
}
