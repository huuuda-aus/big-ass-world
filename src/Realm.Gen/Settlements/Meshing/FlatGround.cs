namespace Realm.Gen.Settlements.Meshing;

/// <summary>Level ground at the city origin's height. Used when a city is built without terrain data, and by tests.</summary>
public sealed class FlatGround : IGroundSampler
{
    /// <summary>The one instance.</summary>
    public static readonly FlatGround Instance = new();

    private FlatGround()
    {
    }

    /// <inheritdoc />
    public double HeightAt(double x, double y) => 0;
}
