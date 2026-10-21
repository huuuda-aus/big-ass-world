using System;
using System.Collections.Generic;
using Realm.Core.Random;

namespace Realm.Gen.Settlements.Placement;

/// <summary>
/// Makes placeholder names for settlements from syllables: a stem and an ending, such as <c>Ashminster</c> or <c>Thornby</c>.
/// Cities and towns use different endings, so the size of a place can be guessed from its name. Real names are a design task.
/// </summary>
/// <remarks>Determinism: depends only on the generator passed in and the names already used. Thread-safety: pure.</remarks>
public static class SettlementNamer
{
    private const int Attempts = 100;

    private static readonly string[] Stems =
    {
        "Ald", "Ash", "Black", "Bright", "Cold", "Crow", "Dun", "East", "Elm", "Fair", "Frost", "Gold", "Grey", "Hal", "High",
        "Iron", "King", "Lang", "Mar", "Mist", "Nor", "Oak", "Raven", "Red", "Salt", "Silver", "Stone", "Storm", "Thorn",
        "West", "White", "Wolf", "Brook", "Mill", "Clay", "Reed", "Amber", "Cinder", "Hollow", "Marble", "Briar",
    };

    private static readonly string[] CityEndings = { "haven", "burg", "minster", "gate", "helm", "ford", "ton", "mere", "crest", "hold" };

    private static readonly string[] TownEndings = { "by", "wick", "stead", "ham", "dale", "field", "worth", "ley", "combe", "marsh" };

    /// <summary>Makes a name that is not in <paramref name="used"/>.</summary>
    /// <param name="rng">Source of randomness; derive it with <see cref="Rng.ForKey"/>.</param>
    /// <param name="tier">City or town (chooses the endings).</param>
    /// <param name="used">Names already given out; the new name is added to it.</param>
    /// <returns>A capitalised name of letters only. When 100 attempts all collide, a number is appended.</returns>
    public static string Generate(Rng rng, SettlementTier tier, ISet<string> used)
    {
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(used);
        string[] endings = tier == SettlementTier.City ? CityEndings : TownEndings;
        for (int attempt = 0; attempt < Attempts; attempt++)
        {
            string stem = Stems[rng.NextInt(Stems.Length)];
            string ending = endings[rng.NextInt(endings.Length)];
            // Avoid doubled letters at the join (Hal + ley is fine, Marl + lea is not).
            string name = stem[^1] == ending[0] ? stem + ending[1..] : stem + ending;
            if (used.Add(name))
            {
                return name;
            }
        }

        int number = 2;
        string baseName = Stems[rng.NextInt(Stems.Length)] + endings[rng.NextInt(endings.Length)];
        while (!used.Add($"{baseName} {number}"))
        {
            number++;
        }

        return $"{baseName} {number}";
    }
}
