using System;
using System.Collections.Generic;
using System.Linq;
using Realm.Core.Validation;
using Realm.Gen.Settlements.Geometry;
using Realm.Gen.Settlements.Model;

namespace Realm.Gen.Settlements.Placement;

/// <summary>Checks that a generated city contains every service building its settlement asks for.</summary>
/// <remarks>Determinism: pure. Thread-safety: pure.</remarks>
public static class ServiceValidator
{
    /// <summary>Rounding slack when comparing a footprint area with the minimum, in square metres.</summary>
    private const double AreaToleranceMeters2 = 0.01;

    /// <summary>Checks one city.</summary>
    /// <param name="settlement">The settlement (its <see cref="Settlement.ServiceList"/> is what is required).</param>
    /// <param name="city">The city generated from <see cref="Settlement.ToCityParams()"/>.</param>
    /// <param name="report">Receives <c>SETL007</c> to <c>SETL009</c> issues.</param>
    public static void Validate(Settlement settlement, City city, ValidationReport report)
    {
        ArgumentNullException.ThrowIfNull(settlement);
        ArgumentNullException.ThrowIfNull(city);
        ArgumentNullException.ThrowIfNull(report);

        var insideWall = new HashSet<Building>(city.CityPatches.SelectMany(p => p.Buildings));
        foreach (ServiceSpec spec in settlement.ServiceList)
        {
            List<Building> found = city.AllBuildings.Where(b => b.Service?.Id == spec.Id).ToList();
            if (found.Count < spec.Count)
            {
                report.Error(
                    SettlementCodes.ServiceMissing,
                    settlement.Id,
                    $"'{settlement.Id}' has {found.Count} of '{spec.Id}' but needs {spec.Count}.",
                    spec.OutsideWall ? "There is not enough farmland next to the wall; lower the count or the minimum area." : "The city has too few free lots; raise the building count or lower the count.");
            }

            foreach (Building b in found)
            {
                bool inside = insideWall.Contains(b);
                if (inside == spec.OutsideWall)
                {
                    report.Error(
                        SettlementCodes.ServiceMisplaced,
                        settlement.Id,
                        $"'{spec.Id}' (building {b.Id}) stands {(inside ? "inside" : "outside")} the wall but should be {(spec.OutsideWall ? "outside" : "inside")}.");
                }

                if (b.Footprint.Area < spec.MinAreaMeters2 - AreaToleranceMeters2)
                {
                    report.Warning(
                        SettlementCodes.ServiceTooSmall,
                        settlement.Id,
                        $"'{spec.Id}' (building {b.Id}) has a footprint of {b.Footprint.Area:F0} m2 but asks for {spec.MinAreaMeters2:F0} m2.",
                        "Lower min_area_m2 or give the city more large lots.");
                }
            }
        }
    }
}
