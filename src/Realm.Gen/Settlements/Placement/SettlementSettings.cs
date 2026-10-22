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

    /// <summary>Default number of cities in the mountains.</summary>
    public const int DefaultMountainCities = 1;

    /// <summary>
    /// Settlements of at most this many houses are small: they have no palisade (<see cref="Settlement.Fortification"/>) and may be laid out
    /// as a cross of streets (<see cref="CrossShare"/>). Larger ones, cities and towns, always have radial wards.
    /// </summary>
    public const int SmallSettlementMaxBuildings = 24;

    /// <summary>Share of small settlements (at most <see cref="SmallSettlementMaxBuildings"/> houses) laid out as a cross of streets instead of radial wards, from 0 to 1.</summary>
    public double CrossShare { get; init; } = 1.0;

    /// <summary>Number of smaller towns to place.</summary>
    public int Towns { get; init; } = DefaultTowns;

    /// <summary>Default number of hamlets.</summary>
    public const int DefaultHamlets = 3;

    /// <summary>Default number of cities that a river runs through.</summary>
    public const int DefaultRiverCities = 1;

    /// <summary>Number of extra cities that a river runs through, splitting them in two (`settlement.river_N`); the river is left untouched and bridged.</summary>
    public int RiverCities { get; init; } = DefaultRiverCities;

    /// <summary>
    /// A river city is planned this many times larger than its house cap, because the river and the bank buffer take houses away, so that the
    /// houses left on the two banks come to about the cap.
    /// </summary>
    public double RiverCityPlanFactor { get; init; } = 1.35;

    /// <summary>The river must pass within this distance of a river city's centre, in metres (the centre itself, with a margin, stays dry).</summary>
    public double RiverCityMaxCentreDistanceMeters { get; init; } = 70;

    /// <summary>The square of a river city and this much round it must be dry, in metres.</summary>
    public double RiverCityDryCentreMeters { get; init; } = 16;

    /// <summary>Number of extra large cities built in mountainous terrain (see <see cref="MountainMinHeightMeters"/>).</summary>
    public int MountainCities { get; init; } = DefaultMountainCities;

    /// <summary>Number of hamlets (a few poor houses, no wall) to place after the towns.</summary>
    public int Hamlets { get; init; } = DefaultHamlets;

    /// <summary>Highest ground a hamlet may stand on, in metres: hamlets are lowland outposts, and the roads to a hamlet in the high mountains need cuts of 60 m and more.</summary>
    public double HamletMaxHeightMeters { get; init; } = 200;

    /// <summary>Fewest houses in a hamlet; each hamlet gets a number between this and <see cref="HamletBuildings"/> (small settlements, at most 24).</summary>
    public int HamletMinBuildings { get; init; } = 8;

    /// <summary>Most houses in a hamlet.</summary>
    public int HamletBuildings { get; init; } = 22;

    /// <summary>Most houses in a city (a city of this size has a wall radius of roughly 150 m).</summary>
    public int CityBuildings { get; init; } = 250;

    /// <summary>Most houses in a town (a town of this size has a wall radius of roughly 80 m).</summary>
    public int TownBuildings { get; init; } = 50;

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

    /// <summary>Lowest ground height of a mountain city above sea level, in metres. Also the height that counts as mountainous when no region data is given.</summary>
    public double MountainMinHeightMeters { get; init; } = 330;

    /// <summary>Highest ground height of a mountain city above sea level, in metres.</summary>
    public double MountainMaxHeightMeters { get; init; } = 1100;

    /// <summary>Largest allowed height difference across the pad of a mountain city, as a fraction of the pad radius.</summary>
    public double MountainMaxReliefPerRadius { get; init; } = 0.35;

    /// <summary>
    /// How much the terrain under a settlement is pulled toward a level pad, from 0 (not at all: the city is built on the natural
    /// ground, with foundations where it slopes) to 1 (fully level, as before ADR 0014).
    /// </summary>
    public double FlattenStrength { get; init; }

    /// <summary>Largest allowed height difference across the pad, as a fraction of the pad radius (0.2 means 66 m over 330 m).</summary>
    public double MaxReliefPerRadius { get; init; } = 0.2;

    /// <summary>
    /// How much of the pad radius, from the centre, must be free of water: the square and the streets round it stay on land, while a river or
    /// lake may reach the outer wards, where the wall is cut at the water's edge.
    /// </summary>
    public double WaterFreeCentreShare { get; init; } = 0.3;

    /// <summary>Score bonus for a site whose pad reaches a river or lake: settlements prefer the waterfront.</summary>
    public double WaterfrontBonus { get; init; } = 1.2;

    /// <summary>Open ground kept between the central square and any river or lake, in metres (on top of <see cref="WaterFreeCentreShare"/>).</summary>
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
        if (Cities < 0 || Towns < 0 || MountainCities < 0 || Hamlets < 0 || RiverCities < 0 || Cities + Towns + MountainCities + Hamlets + RiverCities > 64)
        {
            problem = "Cities, RiverCities, MountainCities, Towns and Hamlets must not be negative and together at most 64.";
        }
        else if (CityBuildings < 3 || TownBuildings < 3 || HamletBuildings < 3 || HamletMinBuildings < 3 || HamletMinBuildings > HamletBuildings)
        {
            problem = "CityBuildings, TownBuildings, HamletBuildings and HamletMinBuildings must be at least 3, and HamletMinBuildings at most HamletBuildings.";
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
        else if (!(RiverCityPlanFactor >= 1) || !(RiverCityMaxCentreDistanceMeters > RiverCityDryCentreMeters) || !(RiverCityDryCentreMeters >= 0))
        {
            problem = "RiverCityPlanFactor must be at least 1 and RiverCityMaxCentreDistanceMeters above RiverCityDryCentreMeters.";
        }
        else if (!(CrossShare >= 0 && CrossShare <= 1))
        {
            problem = "CrossShare must be between 0 and 1.";
        }
        else if (!(HamletMaxHeightMeters > MinHeightMeters))
        {
            problem = "HamletMaxHeightMeters must be above MinHeightMeters.";
        }
        else if (!(MountainMinHeightMeters < MountainMaxHeightMeters) || !(MountainMaxReliefPerRadius > 0) || !(FlattenStrength >= 0 && FlattenStrength <= 1))
        {
            problem = "MountainMinHeightMeters must be below MountainMaxHeightMeters, MountainMaxReliefPerRadius positive and FlattenStrength between 0 and 1.";
        }
        else
        {
            problem = null;
            return true;
        }

        return false;
    }
}
