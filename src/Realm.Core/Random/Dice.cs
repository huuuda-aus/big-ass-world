using System;

namespace Realm.Core.Random;

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

    /// <summary>Most dice <see cref="TryParse"/> accepts in one expression.</summary>
    public const int MaxCount = 100;

    /// <summary>Most sides <see cref="TryParse"/> accepts per die.</summary>
    public const int MaxSides = 1000;

    /// <summary>Largest flat bonus (either sign) <see cref="TryParse"/> accepts.</summary>
    public const int MaxBonus = 10_000;

    /// <summary>
    /// Parses <c>NdS</c>, <c>NdS+B</c>, <c>NdS-B</c> or a plain whole number (a fixed amount), for example
    /// <c>2d6+1</c> or <c>5</c>. Spaces are not allowed.
    /// </summary>
    /// <param name="text">The expression; may be null.</param>
    /// <param name="dice">The dice when parsing succeeds.</param>
    /// <returns>True when <paramref name="text"/> is a valid expression within <see cref="MaxCount"/>, <see cref="MaxSides"/> and <see cref="MaxBonus"/>.</returns>
    public static bool TryParse(string? text, out Dice dice)
    {
        dice = default;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        int d = text.IndexOf('d', StringComparison.Ordinal);
        if (d < 0)
        {
            if (!TryParseSigned(text, out int fixedAmount))
            {
                return false;
            }

            dice = new Dice(0, 0, fixedAmount);
            return true;
        }

        int sign = text.IndexOfAny(new[] { '+', '-' }, d);
        string sidesText = sign < 0 ? text[(d + 1)..] : text[(d + 1)..sign];
        int bonus = 0;
        if (!TryParseDigits(text[..d], out int count) || count < 1 || count > MaxCount
            || !TryParseDigits(sidesText, out int sides) || sides < 1 || sides > MaxSides
            || (sign >= 0 && !TryParseSigned(text[sign..], out bonus)))
        {
            return false;
        }

        dice = new Dice(count, sides, bonus);
        return true;
    }

    /// <summary>Formats as <c>NdS+B</c>, or just the number when there are no dice; <see cref="TryParse"/> reads it back.</summary>
    /// <returns>The dice expression.</returns>
    public override string ToString()
    {
        if (Count == 0)
        {
            return Bonus.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return Bonus switch
        {
            0 => $"{Count}d{Sides}",
            > 0 => $"{Count}d{Sides}+{Bonus}",
            _ => $"{Count}d{Sides}{Bonus}",
        };
    }

    private static bool TryParseDigits(string text, out int value)
    {
        value = 0;
        if (text.Length == 0 || text.Length > 6)
        {
            return false;
        }

        foreach (char c in text)
        {
            if (c is < '0' or > '9')
            {
                return false;
            }

            value = (value * 10) + (c - '0');
        }

        return true;
    }

    private static bool TryParseSigned(string text, out int value)
    {
        value = 0;
        bool negative = text.StartsWith('-');
        string digits = text.StartsWith('-') || text.StartsWith('+') ? text[1..] : text;
        if (!TryParseDigits(digits, out int magnitude) || magnitude > MaxBonus)
        {
            return false;
        }

        value = negative ? -magnitude : magnitude;
        return true;
    }
}
