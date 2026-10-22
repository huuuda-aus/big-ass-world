namespace Realm.Gen.Settlements.Model;

/// <summary>Where the water is, in the city's own plan frame (river, lake or sea).</summary>
/// <remarks>Implementations must be pure and thread-safe: the generator and the meshers call this from worker threads.</remarks>
public interface IWaterSampler
{
    /// <summary>Whether a point of the city plan is under water.</summary>
    /// <param name="x">Plan X in metres (the city's local frame, central square at the origin).</param>
    /// <param name="y">Plan Y in metres.</param>
    /// <returns><c>true</c> over water.</returns>
    bool IsWater(double x, double y);
}
