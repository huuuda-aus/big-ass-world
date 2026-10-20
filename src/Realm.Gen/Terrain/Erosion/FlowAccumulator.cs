using System;

namespace Realm.Gen.Terrain.Erosion;

/// <summary>
/// Adds up the water flowing through each vertex. Flow is measured in effective square kilometres of
/// catchment: a vertex with rainfall 1 contributes its own area; rainfall 0.5 contributes half.
/// </summary>
/// <remarks>Determinism: fixed downstream order, double arithmetic. Thread-safety: stateless.</remarks>
internal static class FlowAccumulator
{
    private const double MetresPerKilometre = 1000.0;

    /// <summary>Accumulates rainfall downstream, losing a fraction of the flow per kilometre travelled.</summary>
    /// <param name="routing">The routing.</param>
    /// <param name="rainfall">Per-vertex rainfall, 0 to 1.</param>
    /// <param name="riverLoss">Per-vertex fraction of flow lost per kilometre while draining from this vertex, 0 to 1.</param>
    /// <returns>Flow in effective km2 at each vertex (its own rain plus everything arriving from upstream).</returns>
    public static double[] Accumulate(FlowRouting routing, double[] rainfall, double[] riverLoss)
    {
        int count = routing.Side * routing.Side;
        double cellAreaKm2 = (routing.Spacing / MetresPerKilometre) * (routing.Spacing / MetresPerKilometre);
        var flow = new double[count];
        for (int i = 0; i < count; i++)
        {
            flow[i] = rainfall[i] * cellAreaKm2;
        }

        for (int k = count - 1; k >= 0; k--)
        {
            int i = routing.Order[k];
            int receiver = routing.Receiver[i];
            if (receiver < 0)
            {
                continue;
            }

            double distanceKm = routing.DistanceToReceiver(i) / MetresPerKilometre;
            double kept = riverLoss[i] == 0 ? 1.0 : Math.Pow(1.0 - riverLoss[i], distanceKm);
            flow[receiver] += flow[i] * kept;
        }

        return flow;
    }
}
