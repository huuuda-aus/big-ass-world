using System;
using Realm.Core.Validation;
using Realm.Data.Content;

namespace Realm.Gen.Terrain;

/// <summary>Shared reading and checking of numeric biome properties for the terrain steps.</summary>
internal static class BiomePropertyReader
{
    /// <summary>Reads a required property; reports a missing or out-of-range value and returns 0 in that case.</summary>
    public static double Require(
        BiomeDefinition biome, string name, ValidationReport report, Func<double, bool> isValid, string rule)
    {
        string where = biome.Id.Value;
        if (!biome.Properties.TryGetValue(name, out double value))
        {
            report.Error(
                TerrainCodes.BiomeMissingProperty,
                where,
                $"Biome '{biome.Id}' is missing required property '{name}'.",
                $"Add '{name}' under 'properties' in the biome file.");
            return 0;
        }

        if (!isValid(value))
        {
            report.Error(
                TerrainCodes.BiomePropertyOutOfRange,
                where,
                $"Biome '{biome.Id}' property '{name}' is {value} but {rule}.");
            return 0;
        }

        return value;
    }

    /// <summary>Warns that a property does not apply to this kind of biome and is ignored.</summary>
    /// <summary>Reads an optional property; absent means 0, present must satisfy <paramref name="isValid"/>.</summary>
    public static double Optional(
        BiomeDefinition biome, string name, ValidationReport report, Func<double, bool> isValid, string rule)
    {
        if (!biome.Properties.TryGetValue(name, out double value))
        {
            return 0;
        }

        if (!isValid(value))
        {
            report.Error(
                TerrainCodes.BiomePropertyOutOfRange,
                biome.Id.Value,
                $"Biome '{biome.Id}' property '{name}' is {value} but {rule}.");
            return 0;
        }

        return value;
    }

    public static void WarnIgnored(BiomeDefinition biome, string name, string kind, ValidationReport report)
    {
        if (biome.Properties.ContainsKey(name))
        {
            report.Warning(
                TerrainCodes.BiomePropertyIgnored,
                biome.Id.Value,
                $"Property '{name}' does not apply to {kind} biomes and is ignored.",
                $"Remove '{name}' from '{biome.Id}'.");
        }
    }
}
