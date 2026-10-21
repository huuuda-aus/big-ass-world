namespace Realm.Gen.Settlements.Roads;

/// <summary>Tuning for <see cref="RoadPlanner"/> and <see cref="RoadTerrain"/>. All values are placeholders until the developer tunes them.</summary>
public sealed record RoadSettings
{
    /// <summary>Width of the road surface in metres.</summary>
    public double WidthMeters { get; init; } = 6;

    /// <summary>Soft verge on each side of the road surface, blended into the terrain, in metres.</summary>
    public double ShoulderMeters { get; init; } = 10;

    /// <summary>Spacing of the route search grid in metres.</summary>
    public double GridMeters { get; init; } = 32;

    /// <summary>Spacing of points along the finished road in metres.</summary>
    public double SampleMeters { get; init; } = 8;

    /// <summary>Extra cost per unit of squared slope; higher makes roads avoid hills.</summary>
    public double SlopePenalty { get; init; } = 25;

    /// <summary>Extra cost, in metres of detour, for each search cell that lies in a river (a bridge is needed there).</summary>
    public double RiverCrossingPenaltyMeters { get; init; } = 400;

    /// <summary>Lowest ground a road may use, in metres above sea level.</summary>
    public double MinHeightMeters { get; init; } = 2;

    /// <summary>Steepest grade (rise over run) of the road surface.</summary>
    public double MaxGrade { get; init; } = 0.12;

    /// <summary>Longest allowed bridge in metres.</summary>
    public double MaxBridgeMeters { get; init; } = 80;

    /// <summary>Deepest allowed cut or fill under a road, in metres.</summary>
    public double MaxCutFillMeters { get; init; } = 20;

    /// <summary>Distance from a settlement's wall that a road starts at, outside the gate, in metres.</summary>
    public double GateApproachMeters { get; init; } = 40;

    /// <summary>Extra links beyond the spanning tree: each settlement is also joined to its nearest unlinked neighbour when this is true.</summary>
    public bool ExtraLinks { get; init; } = true;

    /// <summary>Checks the settings.</summary>
    /// <param name="problem">The first problem found; otherwise null.</param>
    /// <returns><c>true</c> when valid.</returns>
    public bool TryValidate(out string? problem)
    {
        if (!(WidthMeters > 0) || !(ShoulderMeters >= 0))
        {
            problem = "WidthMeters must be positive and ShoulderMeters not negative.";
        }
        else if (!(GridMeters >= 8) || !(SampleMeters >= 2) || SampleMeters > GridMeters)
        {
            problem = "GridMeters must be at least 8, SampleMeters at least 2 and not above GridMeters.";
        }
        else if (!(SlopePenalty >= 0) || !(RiverCrossingPenaltyMeters >= 0))
        {
            problem = "SlopePenalty and RiverCrossingPenaltyMeters must not be negative.";
        }
        else if (!(MaxGrade > 0) || !(MaxBridgeMeters > 0) || !(MaxCutFillMeters > 0) || !(GateApproachMeters >= 0))
        {
            problem = "MaxGrade, MaxBridgeMeters and MaxCutFillMeters must be positive.";
        }
        else
        {
            problem = null;
            return true;
        }

        return false;
    }
}
