using System;
using System.Text;

namespace Realm.Core.Random;

/// <summary>
/// Seedable PCG32 (XSH-RR) pseudo-random number generator. The only source of randomness
/// allowed in Core, Sim and Gen (CLAUDE.md 4.2).
/// </summary>
/// <remarks>
/// Determinism: the output sequence for a given seed (and stream) is fixed across machines and
/// runs. Changing any algorithm in this type changes every generator's output and requires a
/// generator version bump plus regenerated goldens.
/// Thread-safety: instances are not thread-safe; use one instance per thread or per task.
/// </remarks>
public sealed class Rng
{
    private const ulong Multiplier = 6364136223846793005UL;
    private const ulong Fnv64Offset = 14695981039346656037UL;
    private const ulong Fnv64Prime = 1099511628211UL;
    private const double DoubleScale = 1.0 / 9007199254740992.0; // 2^-53

    private const byte TagString = 1;
    private const byte TagInteger = 2;

    private ulong _state;
    private readonly ulong _increment;

    /// <summary>Creates a generator from a raw PCG seed and stream selector.</summary>
    /// <param name="seed">Initial state seed.</param>
    /// <param name="stream">Stream selector; different streams give independent sequences.</param>
    public Rng(ulong seed, ulong stream = 0)
    {
        _state = 0;
        _increment = (stream << 1) | 1UL;
        Step();
        _state += seed;
        Step();
    }

    /// <summary>
    /// Creates a generator for a named place in the seed hierarchy, for example
    /// <c>Rng.ForKey(worldSeed, "dungeon", dungeonId, "sector", 3)</c>.
    /// </summary>
    /// <param name="seed">Root (for example world) seed.</param>
    /// <param name="keys">
    /// Path components. Each must be a <see cref="string"/> or an integer
    /// (<see cref="int"/>, <see cref="uint"/>, <see cref="long"/>, <see cref="ulong"/>).
    /// Order matters, and a string never collides with an integer of the same text.
    /// </param>
    /// <returns>A generator that depends only on <paramref name="seed"/> and <paramref name="keys"/>.</returns>
    /// <exception cref="ArgumentNullException">A key is null.</exception>
    /// <exception cref="ArgumentException">A key has an unsupported type.</exception>
    /// <remarks>Determinism: stable across machines and runs; never uses process-randomised hashing.</remarks>
    public static Rng ForKey(ulong seed, params object[] keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        ulong acc = Mix64(seed);
        foreach (object key in keys)
        {
            acc = Mix64(acc ^ HashKey(key));
        }

        return new Rng(Mix64(acc + 1), Mix64(acc + 2));
    }

    /// <summary>Returns the next 32 random bits.</summary>
    /// <returns>A uniformly distributed unsigned 32-bit value.</returns>
    public uint NextUInt32()
    {
        ulong old = _state;
        Step();
        uint xorShifted = (uint)(((old >> 18) ^ old) >> 27);
        int rot = (int)(old >> 59);
        return (xorShifted >> rot) | (xorShifted << (-rot & 31));
    }

    /// <summary>Returns the next 64 random bits.</summary>
    /// <returns>A uniformly distributed unsigned 64-bit value.</returns>
    public ulong NextUInt64()
    {
        ulong high = NextUInt32();
        return (high << 32) | NextUInt32();
    }

    /// <summary>Returns a uniform integer in [0, <paramref name="maxExclusive"/>).</summary>
    /// <param name="maxExclusive">Exclusive upper bound; must be positive.</param>
    /// <returns>An unbiased integer in range.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxExclusive"/> is not positive.</exception>
    public int NextInt(int maxExclusive)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxExclusive);
        return (int)NextBounded((uint)maxExclusive);
    }

    /// <summary>Returns a uniform integer in [<paramref name="minInclusive"/>, <paramref name="maxExclusive"/>).</summary>
    /// <param name="minInclusive">Inclusive lower bound.</param>
    /// <param name="maxExclusive">Exclusive upper bound; must be greater than the lower bound.</param>
    /// <returns>An unbiased integer in range.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxExclusive"/> is not greater than <paramref name="minInclusive"/>.</exception>
    public int NextInt(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive)
        {
            throw new ArgumentOutOfRangeException(nameof(maxExclusive), "Upper bound must exceed lower bound.");
        }

        uint range = (uint)((long)maxExclusive - minInclusive);
        return (int)(minInclusive + (long)NextBounded(range));
    }

    /// <summary>Returns a uniform double in [0, 1) with 53 bits of precision.</summary>
    /// <returns>A value in [0, 1).</returns>
    public double NextDouble() => (NextUInt64() >> 11) * DoubleScale;

    /// <summary>Returns a fair random boolean.</summary>
    /// <returns><c>true</c> or <c>false</c> with equal probability.</returns>
    public bool NextBool() => (NextUInt32() & 1U) != 0;

    private uint NextBounded(uint bound)
    {
        // Rejection sampling removes modulo bias.
        uint threshold = unchecked(0U - bound) % bound;
        while (true)
        {
            uint r = NextUInt32();
            if (r >= threshold)
            {
                return r % bound;
            }
        }
    }

    private void Step() => _state = unchecked(_state * Multiplier + _increment);

    private static ulong HashKey(object key)
    {
        switch (key)
        {
            case null:
                throw new ArgumentNullException(nameof(key), "Seed keys must not be null.");
            case string s:
                return HashBytes(TagString, Encoding.UTF8.GetBytes(s));
            case int i:
                return HashInteger(unchecked((ulong)(long)i));
            case uint u:
                return HashInteger(u);
            case long l:
                return HashInteger(unchecked((ulong)l));
            case ulong ul:
                return HashInteger(ul);
            default:
                throw new ArgumentException($"Unsupported seed key type '{key.GetType().Name}'.", nameof(key));
        }
    }

    private static ulong HashInteger(ulong value) => Mix64(value ^ ((ulong)TagInteger << 56));

    private static ulong HashBytes(byte tag, byte[] bytes)
    {
        ulong hash = Fnv64Offset ^ tag;
        foreach (byte b in bytes)
        {
            hash = unchecked((hash ^ b) * Fnv64Prime);
        }

        return Mix64(hash);
    }

    // SplitMix64 finaliser.
    private static ulong Mix64(ulong x)
    {
        unchecked
        {
            x += 0x9E3779B97F4A7C15UL;
            x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
            return x ^ (x >> 31);
        }
    }
}
