using System.Collections.Generic;
using Realm.Core.Ids;

namespace Realm.Data.Content;

/// <summary>
/// A species of plant the world generator scatters: a tree, a bush or grass.
/// Schema reference: docs/reference/content/foliage.md.
/// </summary>
/// <param name="Id">Stable identifier, for example <c>foliage.oak</c>.</param>
/// <param name="Name">Display name.</param>
/// <param name="Kind"><c>tree</c>, <c>bush</c> or <c>grass</c> (see <see cref="FoliageVocabulary"/>).</param>
/// <param name="Tags">Tags such as <c>deciduous</c>; enumerates in sorted order.</param>
/// <param name="PerHectare">Plants (or grass tufts) per hectare in each biome where the species grows, sorted by biome ID.</param>
/// <param name="MinSpacingMeters">Smallest distance between two plants of this species; not used for grass.</param>
/// <param name="MinHeightMeters">Lowest ground it grows on, metres above sea level.</param>
/// <param name="MaxHeightMeters">Highest ground it grows on.</param>
/// <param name="MaxSlopeDegrees">Steepest ground it grows on.</param>
/// <param name="MinScale">Smallest size multiplier.</param>
/// <param name="MaxScale">Largest size multiplier.</param>
/// <param name="ClusterMeters">Size of the clumps it grows in; 0 spreads it evenly.</param>
/// <param name="ClusterStrength">0 to 1: how empty the gaps between clumps are.</param>
/// <param name="TrunkRadiusMeters">Radius of the collision a walking player bumps into; 0 means none.</param>
public sealed record FoliageDefinition(
    ContentId Id,
    string Name,
    string Kind,
    TagSet Tags,
    IReadOnlyDictionary<ContentId, double> PerHectare,
    double MinSpacingMeters,
    double MinHeightMeters,
    double MaxHeightMeters,
    double MaxSlopeDegrees,
    double MinScale,
    double MaxScale,
    double ClusterMeters,
    double ClusterStrength,
    double TrunkRadiusMeters)
{
    /// <summary>Plants per hectare in a biome.</summary>
    /// <param name="biome">The biome ID.</param>
    /// <returns>The density; 0 when the species does not grow there.</returns>
    public double DensityIn(ContentId biome) => PerHectare.TryGetValue(biome, out double d) ? d : 0;

    /// <summary>The highest density over all biomes.</summary>
    public double MaxDensity
    {
        get
        {
            double max = 0;
            foreach (double d in PerHectare.Values)
            {
                max = System.Math.Max(max, d);
            }

            return max;
        }
    }
}
