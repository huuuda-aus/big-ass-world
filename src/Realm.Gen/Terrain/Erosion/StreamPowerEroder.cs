using System;

namespace Realm.Gen.Terrain.Erosion;

/// <summary>
/// One implicit step of the stream-power erosion law (Braun and Willett's linear-slope scheme): each vertex
/// moves toward its receiver by a fraction that grows with <c>erodibility * flow^m / distance</c>.
/// </summary>
/// <remarks>
/// The implicit update is unconditionally stable: a vertex ends between its old height and its receiver's
/// new height, so it can neither overshoot below the receiver nor rise. Vertices are processed receivers
/// first. Depressions (filled vertices) and outlets are not eroded.
/// Determinism: fixed order, no randomness. Thread-safety: stateless.
/// </remarks>
internal static class StreamPowerEroder
{
    /// <summary>Exponent of the flow in the stream-power law (placeholder tuning; 0.5 is the textbook value).</summary>
    public const double FlowExponent = 0.5;

    /// <summary>Applies one erosion step.</summary>
    /// <param name="routing">Routing computed for <paramref name="heights"/>.</param>
    /// <param name="heights">Current heights.</param>
    /// <param name="flowKm2">Accumulated flow in effective km2.</param>
    /// <param name="erodibility">Per-vertex erodibility, 0 to 1.</param>
    /// <param name="strength">Overall multiplier for this step (strength times time step).</param>
    /// <returns>The new heights; never above the old ones.</returns>
    public static float[] Step(FlowRouting routing, float[] heights, double[] flowKm2, double[] erodibility, double strength)
    {
        int count = heights.Length;
        var result = new float[count];
        var level = new float[count]; // water level or ground height a downstream vertex presents to its donors

        foreach (int i in routing.Order)
        {
            bool pooled = routing.Filled[i] > heights[i];
            if (routing.Receiver[i] < 0 || pooled)
            {
                result[i] = heights[i];
                level[i] = pooled ? routing.Filled[i] : heights[i];
                continue;
            }

            int receiver = routing.Receiver[i];
            double factor = strength * erodibility[i] * Math.Pow(flowKm2[i], FlowExponent) / routing.DistanceToReceiver(i);
            double target = (heights[i] + (factor * level[receiver])) / (1.0 + factor);
            float eroded = (float)Math.Min(heights[i], target);
            result[i] = eroded;
            level[i] = eroded;
        }

        return result;
    }
}
