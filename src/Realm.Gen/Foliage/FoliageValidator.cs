using System;
using System.Collections.Generic;
using Realm.Core.Ids;
using Realm.Core.Validation;
using Realm.Data.Content;
using Realm.Gen.Terrain;

namespace Realm.Gen.Foliage;

/// <summary>Checks generated foliage against the terrain and the exclusion raster (codes in <see cref="FoliageCodes"/>).</summary>
/// <remarks>Determinism: pure; issues come out in species order. Thread-safety: pure.</remarks>
public static class FoliageValidator
{
    private const double SpacingSlack = 1e-6;

    /// <summary>Validates foliage.</summary>
    /// <param name="data">The foliage.</param>
    /// <param name="species">The species definitions.</param>
    /// <param name="field">The terrain the foliage was placed on.</param>
    /// <param name="exclusion">The clear areas.</param>
    /// <param name="settings">Sea margin.</param>
    /// <param name="report">Receives FOL001 to FOL003.</param>
    public static void Validate(FoliageData data, IReadOnlyDictionary<ContentId, FoliageDefinition> species, HeightField field, FoliageExclusion exclusion, FoliageSettings? settings, ValidationReport report)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(species);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(exclusion);
        ArgumentNullException.ThrowIfNull(report);
        settings ??= new FoliageSettings();

        var placed = new int[data.Species.Count];
        var misplaced = new int[data.Species.Count];
        var example = new (double X, double Z)?[data.Species.Count];
        var byIndex = new Dictionary<int, List<FoliageInstance>>();
        foreach (FoliageInstance inst in data.Instances)
        {
            placed[inst.Species]++;
            FoliageDefinition def = species[ContentId.Parse(data.Species[inst.Species].Id)];
            // The file quantises positions by a few millimetres, so judge the ground with a little tolerance in height and slope.
            if (exclusion.IsBlocked(inst.X, inst.Z) || !FoliageGenerator.GroundSuits(def with { MaxSlopeDegrees = def.MaxSlopeDegrees + 0.5, MaxHeightMeters = def.MaxHeightMeters + 0.5 }, field, inst.X, inst.Z, settings, out _))
            {
                misplaced[inst.Species]++;
                example[inst.Species] ??= (inst.X, inst.Z);
            }

            if (!byIndex.TryGetValue(inst.Species, out List<FoliageInstance>? list))
            {
                byIndex[inst.Species] = list = new List<FoliageInstance>();
            }

            list.Add(inst);
        }

        for (int i = 0; i < data.Species.Count; i++)
        {
            FoliageSpecies s = data.Species[i];
            if (misplaced[i] > 0)
            {
                (double x, double z) = example[i]!.Value;
                report.Error(FoliageCodes.PlacedWhereItCannotGrow, s.Id, $"{misplaced[i]} of {placed[i]} '{s.Id}' plants stand where they cannot grow; for example at ({x:F0}, {z:F0}).", "Check the exclusion raster and the species' height and slope limits.");
            }

            if (s.Kind == FoliageVocabulary.Grass)
            {
                continue;
            }

            if (placed[i] == 0)
            {
                report.Warning(FoliageCodes.SpeciesNotPlaced, s.Id, $"Species '{s.Id}' placed no plants.", "Raise its density, widen its height and slope limits, or check its biomes.");
            }
            else
            {
                CheckSpacing(s, species[ContentId.Parse(s.Id)].MinSpacingMeters, byIndex[i], report);
            }
        }
    }

    private static void CheckSpacing(FoliageSpecies s, double spacing, List<FoliageInstance> plants, ValidationReport report)
    {
        var cells = new Dictionary<(int, int), List<FoliageInstance>>();
        foreach (FoliageInstance p in plants)
        {
            var key = ((int)Math.Floor(p.X / spacing), (int)Math.Floor(p.Z / spacing));
            if (!cells.TryGetValue(key, out List<FoliageInstance>? list))
            {
                cells[key] = list = new List<FoliageInstance>();
            }

            list.Add(p);
        }

        int bad = 0;
        FoliageInstance? first = null;
        foreach (FoliageInstance p in plants)
        {
            int bx = (int)Math.Floor(p.X / spacing);
            int bz = (int)Math.Floor(p.Z / spacing);
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (!cells.TryGetValue((bx + dx, bz + dz), out List<FoliageInstance>? list))
                    {
                        continue;
                    }

                    foreach (FoliageInstance o in list)
                    {
                        double d2 = ((o.X - p.X) * (o.X - p.X)) + ((o.Z - p.Z) * (o.Z - p.Z));
                        if (!o.Equals(p) && d2 < (spacing * spacing) - SpacingSlack)
                        {
                            bad++;
                            first ??= p;
                        }
                    }
                }
            }
        }

        if (bad > 0)
        {
            report.Error(FoliageCodes.TooClose, s.Id, $"{bad / 2} pair(s) of '{s.Id}' plants are closer than {spacing:0.0} m; for example near ({first.GetValueOrDefault().X:F0}, {first.GetValueOrDefault().Z:F0}).", "Raise min_spacing_m or lower the density.");
        }
    }
}
