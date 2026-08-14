using Gorillas.Core.Model;
using Gorillas.Core.Primitives;
using Gorillas.Core.Simulation;

namespace Gorillas.Core.Tests;

/// <summary>
/// Angles above 90 degrees lob the banana back over the gorilla's own shoulder — the play that
/// rescues a shot in a strong headwind or over a tall neighbouring tower.
/// </summary>
public class BackwardsThrowTests
{
    private static readonly GameSettings Settings = GameSettings.Default;

    private static IReadOnlyList<Gorilla> OpenFieldGorillas() =>
    [
        new Gorilla(0, new Vec2(160, 100), 0),
        new Gorilla(1, new Vec2(260, 100), 1),
    ];

    [Fact]
    public void AnAngleBeyond90SendsTheBananaBackwards()
    {
        var gorillas = OpenFieldGorillas();

        var forward = BananaSimulator.Simulate(Skyline.Empty, gorillas, Settings, 0, 45, 50, wind: 0);
        var backward = BananaSimulator.Simulate(Skyline.Empty, gorillas, Settings, 0, 135, 50, wind: 0);

        Assert.True(forward.Impact.Position.X > gorillas[0].Feet.X);
        Assert.True(backward.Impact.Position.X < gorillas[0].Feet.X);
    }

    [Fact]
    public void PlayerTwoAlsoThrowsBackwardsBeyond90()
    {
        var gorillas = OpenFieldGorillas();

        var backward = BananaSimulator.Simulate(Skyline.Empty, gorillas, Settings, 1, 135, 50, wind: 0);

        // Slot 1 faces left, so its backwards throw travels right.
        Assert.True(backward.Impact.Position.X > gorillas[1].Feet.X);
    }

    [Fact]
    public void ABackwardsThrowDoesNotImmediatelyHitTheThrower()
    {
        var gorillas = OpenFieldGorillas();

        foreach (var angle in new[] { 95.0, 120, 150, 179 })
        {
            var trajectory = BananaSimulator.Simulate(Skyline.Empty, gorillas, Settings, 0, angle, 60, wind: 0);

            Assert.True(
                trajectory.Points.Count > 5,
                $"A {angle} degree throw ended almost immediately, suggesting it launched inside the thrower.");
        }
    }

    [Fact]
    public void MirroredForwardAndBackwardThrowsAreSymmetric()
    {
        var gorillas = OpenFieldGorillas();

        var forward = BananaSimulator.Simulate(Skyline.Empty, gorillas, Settings, 0, 60, 45, wind: 0);
        var backward = BananaSimulator.Simulate(Skyline.Empty, gorillas, Settings, 0, 120, 45, wind: 0);

        var forwardRange = forward.Impact.Position.X - gorillas[0].Feet.X;
        var backwardRange = gorillas[0].Feet.X - backward.Impact.Position.X;

        Assert.Equal(forwardRange, backwardRange, 6);
    }

    /// <summary>The whole point of the feature: a strong headwind can carry a backwards lob home.</summary>
    [Fact]
    public void AStrongTailwindCarriesABackwardsLobOntoTheTarget()
    {
        IReadOnlyList<Gorilla> gorillas =
        [
            new Gorilla(0, new Vec2(120, 100), 0),
            new Gorilla(1, new Vec2(220, 100), 1),
        ];

        var landedForward = false;

        // Thrown backwards, then blown forwards hard enough to come back over the thrower.
        for (var velocity = 30.0; velocity <= 80; velocity += 1)
        {
            var trajectory = BananaSimulator.Simulate(
                Skyline.Empty, gorillas, Settings, 0, 110, velocity, wind: Settings.MaxWind);

            if (trajectory.Impact.Position.X > gorillas[0].Feet.X)
            {
                landedForward = true;
                break;
            }
        }

        Assert.True(landedForward, "A hard tailwind never turned a backwards lob around.");
    }

    [Fact]
    public void TheSimulationStaysDeterministicForBackwardsThrows()
    {
        var gorillas = OpenFieldGorillas();

        var first = BananaSimulator.Simulate(Skyline.Empty, gorillas, Settings, 0, 143.25, 62.5, wind: -3.5);
        var second = BananaSimulator.Simulate(Skyline.Empty, gorillas, Settings, 0, 143.25, 62.5, wind: -3.5);

        Assert.Equal(first.Points, second.Points);
        Assert.Equal(first.Impact, second.Impact);
    }

    [Fact]
    public void AVerticalThrowStillGoesStraightUp()
    {
        var gorillas = OpenFieldGorillas();

        var trajectory = BananaSimulator.Simulate(Skyline.Empty, gorillas, Settings, 0, 90, 40, wind: 0);
        var apex = trajectory.Points.Max(p => p.Y);

        Assert.True(apex > gorillas[0].Feet.Y + 20);
        Assert.All(trajectory.Points, p => Assert.InRange(p.X, gorillas[0].Feet.X - 20, gorillas[0].Feet.X + 20));
    }
}
