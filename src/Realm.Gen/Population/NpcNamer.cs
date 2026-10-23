using System;
using Realm.Core.Random;

namespace Realm.Gen.Population;

/// <summary>Makes placeholder names for characters: a given name and a family name from short lists.</summary>
/// <remarks>Determinism: pure functions of the <see cref="Rng"/> passed in. Thread-safety: stateless.</remarks>
public static class NpcNamer
{
    private static readonly string[] Given =
    {
        "Aldric", "Bram", "Cedric", "Dunstan", "Edmund", "Fenn", "Godric", "Hale", "Ivo", "Jory", "Kellan", "Leofric", "Merrick", "Nigel", "Osric",
        "Perrin", "Quill", "Rowan", "Silas", "Tobias", "Ulric", "Varrin", "Wulf", "Yorick", "Alys", "Brenna", "Cora", "Edda", "Freya", "Gwen",
        "Hild", "Isolde", "Joan", "Kenna", "Lysa", "Maud", "Nell", "Odette", "Petra", "Rhosyn", "Sunniva", "Tilda", "Una", "Wenna", "Ysolde",
    };

    private static readonly string[] Family =
    {
        "Ashby", "Barlow", "Corwin", "Dale", "Eastwood", "Fletcher", "Garrick", "Holloway", "Ingram", "Jarrow", "Kemp", "Lowell", "Marsh", "Norwood",
        "Oakes", "Pryor", "Quincey", "Radley", "Sutton", "Thorne", "Underhill", "Vance", "Wick", "Yarrow", "Ashdown", "Brook", "Cooper", "Draper",
        "Fenwick", "Greaves", "Hartley", "Kirby", "Langley", "Mercer", "Nash", "Pembroke", "Rook", "Stannard", "Tanner", "Whitlock",
    };

    /// <summary>Makes a name.</summary>
    /// <param name="rng">The character's own generator.</param>
    /// <returns>A given name and a family name, for example <c>Bram Fletcher</c>.</returns>
    public static string Name(Rng rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        return $"{Given[rng.NextInt(Given.Length)]} {Family[rng.NextInt(Family.Length)]}";
    }
}
