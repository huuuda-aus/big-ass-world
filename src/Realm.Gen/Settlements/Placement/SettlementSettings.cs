namespace Realm.Gen.Settlements.Placement;

/// <summary>Tuning for <see cref="SettlementPlacer"/> and <see cref="SettlementTerrain"/>. All values are placeholders until the developer tunes them.</summary>
public sealed record SettlementSettings
{
    /// <summary>Default number of cities.</summary>
    public const int DefaultCities = 3;

    /// <summary>Default number of towns.</summary>
    public const int DefaultTowns = 5;

    /// <summary>Number of large cities to place.</summary>
    public int Cities { get; init; } = DefaultCities;

    /// <summary>Number of smaller towns to place.</summary>
    public int Towns { get; init; } = DefaultTowns;

    /// <summary>Houses in a city (about 291 m wall radius at 1,200).</summary>
    public int CityBuildings { get; init; } = 1200;

    /// <summary>Houses in a town (about 189 m wall radius at 400).</summary>
    public int TownBuildings { get; init; } = 400;

    /// <summary>Least distance between two settlement centres, on top of their pads.</summary>
    public double MinSpacingMeters { get; init; } = 2500;

    /// <summary>Extra flattened ground beyond the wall, in metres.</summary>
    public double PadMarginMeters { get; init; } = 30;

    /// <summary>Width of the smooth slope from the flat pad back to the natural terrain, in metres.</summary>
    public double BlendMeters { get; init; } = 100;

    /// <summary>Spacing of candidate positions, in metres.</summary>
    public double CandidateStepMeters { get; init; } = 128;

    /// <summary>Lowest allowed ground height of a settlement above sea level, in metres.</summary>
    public double MinHeightMeters { get; init; } = 15;

    /// <summary>Highest allowed ground height of a settlement above sea level, in metres.</summary>
    public double MaxHeightMeters { get; init; } = 320;

    /// <summary>Largest allowed height difference across the pad, as a fraction of the pad radius (0.1 means 33 m over 330 m).</summary>
    public double MaxReliefPerRadius { get; init; } = 0.1;

    /// <summary>Open ground kept between the pad and any river or lake, in metres.</summary>
    public double WaterClearanceMeters { get; init; } = 40;

    /// <summary>Settlements prefer a river within this distance beyond the clearance (cities like water), in metres.</summary>
    public double RiverAttractionMeters { get; init; } = 1500;

    /// <summary>Score bonus for having a river within <see cref="RiverAttractionMeters"/>.</summary>
    public double RiverBonus { get; init; } = 0.25;

    /// <summary>Keeps the pad and its blend this far inside the world edge, in metres.</summary>
    public double EdgeMarginMeters { get; init; } = 400;

    /// <summary>Checks the settings.</summary>
    /// <param name="problem">The first problem found; otherwise null.</param>
    /// <returns><c>true</c> when valid.</returns>
    public bool TryValidate(out string? problem)
    {
        if (Cities < 0 || Towns < 0 || Cities + Towns > 64)
        {
            problem = "Cities and Towns must not be negative and together at most 64.";
        }
        else if (CityBuildings < 3 || TownBuildings < 3)
        {
            problem = "CityBuildings and TownBuildings must be at least 3.";
        }
        else if (!(MinSpacingMeters >= 0) || !(PadMarginMeters >= 0) || !(BlendMeters > 0) || !(WaterClearanceMeters >= 0) || !(EdgeMarginMeters >= 0))
        {
            problem = "Distances must not be negative (BlendMeters must be positive).";
        }
        else if (!(CandidateStepMeters >= 16))
        {
            problem = "CandidateStepMeters must be at least 16.";
        }
        else if (!(MinHeightMeters < MaxHeightMeters) || !(MaxReliefPerRadius > 0))
        {
            problem = "MinHeightMeters must be below MaxHeightMeters and MaxReliefPerRadius positive.";
        }
        else
        {
            problem = null;
            return true;
        }

        return false;
    }
}
