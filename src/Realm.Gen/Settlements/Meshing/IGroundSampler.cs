namespace Realm.Gen.Settlements.Meshing;

/// <summary>The height of the ground under a city, in the city's own plan frame.</summary>
/// <remarks>Implementations must be pure and thread-safe: the meshers call this from worker threads.</remarks>
public interface IGroundSampler
{
    /// <summary>Height of the ground at a point of the city plan.</summary>
    /// <param name="x">Plan X in metres (the city's local frame, central square at the origin).</param>
    /// <param name="y">Plan Y in metres (world Z in the city's frame).</param>
    /// <returns>Height in metres above the city's origin (the settlement's ground height).</returns>
    double HeightAt(double x, double y);
}
