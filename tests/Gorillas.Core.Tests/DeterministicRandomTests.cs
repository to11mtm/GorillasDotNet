using Gorillas.Core.Model;
using Gorillas.Core.Primitives;

namespace Gorillas.Core.Tests;

public class DeterministicRandomTests
{
    [Fact]
    public void SameSeedProducesSameSequence()
    {
        var a = new DeterministicRandom(2024);
        var b = new DeterministicRandom(2024);

        for (var i = 0; i < 100; i++)
        {
            Assert.Equal(a.NextUInt64(), b.NextUInt64());
        }
    }

    [Fact]
    public void DifferentStreamsDiverge()
    {
        var a = DeterministicRandom.ForStream(99, 1);
        var b = DeterministicRandom.ForStream(99, 2);

        Assert.NotEqual(a.NextUInt64(), b.NextUInt64());
    }

    [Fact]
    public void NextDoubleStaysInUnitInterval()
    {
        var rng = new DeterministicRandom(7);

        for (var i = 0; i < 10_000; i++)
        {
            var value = rng.NextDouble();
            Assert.InRange(value, 0.0, 1.0);
        }
    }

    [Fact]
    public void NextIntRespectsBounds()
    {
        var rng = new DeterministicRandom(7);

        for (var i = 0; i < 10_000; i++)
        {
            Assert.InRange(rng.NextInt(3, 9), 3, 8);
        }
    }

    [Fact]
    public void ZeroSeedDoesNotCollapseToZero()
    {
        var rng = new DeterministicRandom(0);

        Assert.NotEqual(0UL, rng.NextUInt64());
        Assert.NotEqual(0UL, rng.NextUInt64());
    }
}
