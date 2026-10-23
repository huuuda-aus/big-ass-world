using System;
using System.Linq;
using Realm.Core.Ids;
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
    /// <remarks>Covers <c>item.material</c>, <c>region.biome</c> and the biomes a foliage species grows in.</remarks>
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

        foreach (FoliageDefinition species in database.Foliage.Values)
        {
            foreach (ContentId biome in species.PerHectare.Keys)
            {
                if (!database.Biomes.ContainsKey(biome))
                {
                    database.TryGetSource(species.Id, out string location);
                    report.Error(
                        ValidatorCodes.DanglingReference,
                        location,
                        $"Foliage '{species.Id}' grows in biome '{biome}', which does not exist.",
                        "Fix the ID or add the biome.");
                }
            }
        }

        foreach (ProfessionDefinition profession in database.Professions.Values)
        {
            if (profession.WorkplaceService is { } service && !database.Services.ContainsKey(service))
            {
                database.TryGetSource(profession.Id, out string location);
                report.Error(
                    ValidatorCodes.DanglingReference,
                    location,
                    $"Profession '{profession.Id}' works in service '{service}', which does not exist.",
                    "Fix the ID or add the service building.");
            }
        }

        foreach (string tier in ProfessionVocabulary.Tiers)
        {
            double total = database.Professions.Values.Sum(p => p.ShareFor(tier));
            if (total > 1.0 + 1e-9)
            {
                report.Error(
                    ValidatorCodes.InvalidProfessionField,
                    "professions",
                    $"The shares of the professions for tier '{tier}' add up to {total:0.###}, more than 1.",
                    "Lower some shares; whoever is left over becomes the filler profession.");
            }
        }
    }
}
