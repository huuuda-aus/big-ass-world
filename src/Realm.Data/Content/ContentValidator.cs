using System;
using Realm.Core.Validation;

namespace Realm.Data.Content;

/// <summary>Checks rules that span several records, after loading.</summary>
/// <remarks>
/// Determinism: records are visited in sorted ID order, so issue order is stable.
/// Thread-safety: stateless.
/// </remarks>
public static class ContentValidator
{
    /// <summary>Reports every reference that points at a record which does not exist.</summary>
    /// <remarks>Covers <c>item.material</c> and <c>region.biome</c>.</remarks>
    /// <param name="database">The loaded content.</param>
    /// <param name="report">Receives <see cref="ValidatorCodes.DanglingReference"/> errors.</param>
    public static void ValidateReferences(ContentDatabase database, ValidationReport report)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(report);

        foreach (ItemDefinition item in database.Items.Values)
        {
            if (item.Material is { } material && !database.Materials.ContainsKey(material))
            {
                database.TryGetSource(item.Id, out string location);
                report.Error(
                    ValidatorCodes.DanglingReference,
                    location,
                    $"Item '{item.Id}' references material '{material}', which does not exist.",
                    "Fix the ID or add the material.");
            }
        }

        foreach (RegionDefinition region in database.Regions.Values)
        {
            if (!database.Biomes.ContainsKey(region.Biome))
            {
                database.TryGetSource(region.Id, out string location);
                report.Error(
                    ValidatorCodes.DanglingReference,
                    location,
                    $"Region '{region.Id}' references biome '{region.Biome}', which does not exist.",
                    "Fix the ID or add the biome.");
            }
        }
    }
}
