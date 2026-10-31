using System;
using Realm.Core.Random;

namespace Realm.Sim.Combat;

/// <summary>A dice expression such as <c>2d6+1</c>: <see cref="Count"/> dice with <see cref="Sides"/> sides, plus <see cref="Bonus"/>.</summary>
/// <param name="Count">Number of dice; zero or more.</param>
/// <param name="Sides">Sides per die; at least 1 when <paramref name="Count"/> is positive.</param>
/// <param name="Bonus">Flat amount added to the total; may be negative.</param>
/// <remarks>Determinism: rolls draw only from the given <see cref="Rng"/>, one draw per die. Thread-safety: immutable.</remarks>
public readonly record struct Dice(int Count, int Sides, int Bonus = 0)
{
    /// <summary>The mean result, used by AI to compare attacks.</summary>
    public double Average => Count * (Sides + 1) / 2.0 + Bonus;

    /// <summary>Rolls the dice.</summary>
    /// <param name="rng">Source of randomness.</param>
    /// <returns>The total, never below zero.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rng"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The expression is malformed (negative count, or dice with fewer than one side).</exception>
    public int Roll(Rng rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        if (Count < 0 || (Count > 0 && Sides < 1))
        {
            throw new InvalidOperationException($"Malformed dice {this}.");
        }

        int total = Bonus;
        for (int i = 0; i < Count; i++)
        {
            total += rng.NextInt(1, Sides + 1);
        }

        return Math.Max(0, total);
    }

    /// <summary>Formats as <c>NdS+B</c>.</summary>
    /// <returns>The dice expression.</returns>
    public override string ToString() => Bonus switch
    {
        0 => $"{Count}d{Sides}",
        > 0 => $"{Count}d{Sides}+{Bonus}",
        _ => $"{Count}d{Sides}{Bonus}",
    };
}
