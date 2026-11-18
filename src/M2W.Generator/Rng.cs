namespace M2W.Generator;

/// <summary>
/// Deterministic pseudo-random numbers (xoshiro256**, seeded with SplitMix64). Own implementation so the output never
/// depends on the .NET version. <see cref="Fork(string)"/> derives an independent stream from this stream's seed and a
/// tag without consuming any numbers, so adding a draw in one place does not shift every later case.
/// </summary>
public sealed class Rng
{
    private ulong _s0, _s1, _s2, _s3;

    public Rng(ulong seed)
    {
        Seed = seed;
        var x = seed;
        _s0 = SplitMix(ref x);
        _s1 = SplitMix(ref x);
        _s2 = SplitMix(ref x);
        _s3 = SplitMix(ref x);
    }

    public ulong Seed { get; }

    public static ulong SplitMix(ref ulong x)
    {
        x += 0x9E3779B97F4A7C15UL;
        var z = x;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>Stable 64-bit hash of a seed and a tag (FNV-1a over the tag, then SplitMix).</summary>
    public static ulong Hash(ulong seed, string tag)
    {
        var h = 0xCBF29CE484222325UL;
        foreach (var c in tag)
        {
            h ^= c;
            h *= 0x100000001B3UL;
        }
        var x = seed ^ h;
        return SplitMix(ref x);
    }

    public Rng Fork(string tag) => new(Hash(Seed, tag));

    public ulong NextULong()
    {
        var result = RotL(_s1 * 5, 7) * 9;
        var t = _s1 << 17;
        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = RotL(_s3, 45);
        return result;
    }

    private static ulong RotL(ulong x, int k) => (x << k) | (x >> (64 - k));

    /// <summary>Uniform in [0, 1).</summary>
    public double NextDouble() => (NextULong() >> 11) * (1.0 / (1UL << 53));

    /// <summary>Uniform integer in [min, maxExclusive).</summary>
    public int Int(int min, int maxExclusive)
    {
        if (maxExclusive <= min) throw new ArgumentException($"Empty range [{min}, {maxExclusive}).");
        var span = (ulong)(maxExclusive - min);
        return min + (int)(NextULong() % span);
    }

    /// <summary>Uniform integer in [min, max] (both inclusive).</summary>
    public int IntIn(int min, int max) => Int(min, max + 1);

    public double Range(double min, double max) => min + (max - min) * NextDouble();

    /// <summary>Uniform value in [min, max] on a grid of <paramref name="step"/>, rounded to 6 decimals.</summary>
    public double Step(double min, double max, double step)
    {
        var lo = (long)Math.Ceiling(min / step - 1e-9);
        var hi = (long)Math.Floor(max / step + 1e-9);
        if (hi < lo) return Math.Round(lo * step, 6);
        var k = lo + (long)(NextULong() % (ulong)(hi - lo + 1));
        return Math.Round(k * step, 6);
    }

    public bool Chance(double p) => NextDouble() < p;

    public T Pick<T>(IReadOnlyList<T> items)
    {
        if (items.Count == 0) throw new ArgumentException("Cannot pick from an empty list.");
        return items[Int(0, items.Count)];
    }

    public void Shuffle<T>(IList<T> items)
    {
        for (var i = items.Count - 1; i > 0; i--)
        {
            var j = Int(0, i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

    /// <summary>A random unit vector built from small integers and one square root (no trigonometry, so it is
    /// bit-identical on every platform).</summary>
    public (double C, double S) Direction()
    {
        while (true)
        {
            var a = Int(-40, 41);
            var b = Int(-40, 41);
            if (a == 0 && b == 0) continue;
            var n = Math.Sqrt((double)a * a + (double)b * b);
            return (a / n, b / n);
        }
    }
}
