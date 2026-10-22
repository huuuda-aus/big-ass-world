using Realm.Gen.Settlements.Geometry;

namespace Realm.Gen.Settlements.Model;

/// <summary>A bridge carrying a street over a river or lake inside a city, from one bank to the other.</summary>
/// <param name="From">The land end on one bank, in the city's plan coordinates.</param>
/// <param name="To">The land end on the other bank.</param>
/// <param name="WidthMeters">Width of the roadway between the parapets.</param>
public sealed record CityBridge(Vec2 From, Vec2 To, double WidthMeters)
{
    /// <summary>Length from bank to bank, in metres.</summary>
    public double LengthMeters => Vec2.Distance(From, To);
}
