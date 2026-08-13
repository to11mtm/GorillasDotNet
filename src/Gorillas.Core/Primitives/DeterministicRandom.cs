namespace Gorillas.Core.Primitives;

public interface IRandomSource
{
    double NextDouble();

    int NextInt(int minInclusive, int maxExclusive);
}

/// <summary>
/// xorshift64* — chosen over <see cref="Random"/> because the sequence must stay
/// identical across runtimes and framework versions for replay to be faithful.
/// </summary>
public sealed class DeterministicRandom : IRandomSource
{
    private ulong _state;

    public DeterministicRandom(ulong seed) => _state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;

    public static DeterministicRandom ForStream(ulong seed, ulong stream)
    {
        unchecked
        {
            var mixed = seed ^ ((stream + 0x9E3779B97F4A7C15UL) * 0xBF58476D1CE4E5B9UL);
            mixed ^= mixed >> 31;
            return new DeterministicRandom(mixed * 0x94D049BB133111EBUL);
        }
    }

    public ulong NextUInt64()
    {
        unchecked
        {
            _state ^= _state >> 12;
            _state ^= _state << 25;
            _state ^= _state >> 27;
            return _state * 2685821657736338717UL;
        }
    }

    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / 9007199254740992.0);

    public double NextDouble(double min, double max) => min + (NextDouble() * (max - min));

    public int NextInt(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive)
        {
            return minInclusive;
        }

        var range = (ulong)(maxExclusive - minInclusive);
        return minInclusive + (int)(NextUInt64() % range);
    }
}
