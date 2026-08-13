using Gorillas.Core.Model;
using Gorillas.Core.Primitives;
using Gorillas.Core.Simulation;

namespace Gorillas.Core.Tests;

public class BananaSimulatorTests
{
    private static readonly GameSettings Settings = GameSettings.Default;

    /// <summary>Open sky and widely separated gorillas, so only gravity and wind are in play.</summary>
    private static IReadOnlyList<Gorilla> OpenFieldGorillas() =>
    [
        new Gorilla(0, new Vec2(20, 100), 0),
        new Gorilla(1, new Vec2(300, 100), 1),
    ];

    private static double AnalyticGroundRange(Vec2 origin, double angleDegrees, double velocity, double gravity)
    {
        var radians = angleDegrees * Math.PI / 180.0;
        var vx = Math.Cos(radians) * velocity;
        var vy = Math.Sin(radians) * velocity;
        var time = (vy + Math.Sqrt((vy * vy) + (2 * gravity * origin.Y))) / gravity;
        return origin.X + (vx * time);
    }

    [Fact]
    public void TrajectoryMatchesTheClosedFormSolutionWithoutWind()
    {
        var gorillas = OpenFieldGorillas();
        var origin = gorillas[0].ThrowOrigin(Settings, 1);
        var expected = AnalyticGroundRange(origin, 45, 50, Settings.Gravity);

        var trajectory = BananaSimulator.Simulate(Skyline.Empty, gorillas, Settings, 0, 45, 50, wind: 0);

        Assert.Equal(ImpactKind.Ground, trajectory.Impact.Kind);
        Assert.Equal(expected, trajectory.Impact.Position.X, expected * 0.02);
    }

    [Fact]
    public void SimulationIsRepeatable()
    {
        var gorillas = OpenFieldGorillas();

        var a = BananaSimulator.Simulate(Skyline.Empty, gorillas, Settings, 0, 37.5, 63.25, wind: 2.5);
        var b = BananaSimulator.Simulate(Skyline.Empty, gorillas, Settings, 0, 37.5, 63.25, wind: 2.5);

        Assert.Equal(a.Points, b.Points);
        Assert.Equal(a.Impact, b.Impact);
    }

    [Fact]
    public void TailwindExtendsTheShotAndHeadwindShortensIt()
    {
        var gorillas = OpenFieldGorillas();

        var still = BananaSimulator.Simulate(Skyline.Empty, gorillas, Settings, 0, 45, 50, wind: 0);
        var tail = BananaSimulator.Simulate(Skyline.Empty, gorillas, Settings, 0, 45, 50, wind: 6);
        var head = BananaSimulator.Simulate(Skyline.Empty, gorillas, Settings, 0, 45, 50, wind: -6);

        Assert.True(tail.Impact.Position.X > still.Impact.Position.X);
        Assert.True(head.Impact.Position.X < still.Impact.Position.X);
    }

    [Fact]
    public void HarderThrowsTravelFurther()
    {
        var gorillas = OpenFieldGorillas();

        var soft = BananaSimulator.Simulate(Skyline.Empty, gorillas, Settings, 0, 45, 30, wind: 0);
        var hard = BananaSimulator.Simulate(Skyline.Empty, gorillas, Settings, 0, 45, 55, wind: 0);

        Assert.True(hard.Impact.Position.X > soft.Impact.Position.X);
    }

    [Fact]
    public void PlayerTwoThrowsLeftwards()
    {
        var gorillas = OpenFieldGorillas();

        var trajectory = BananaSimulator.Simulate(Skyline.Empty, gorillas, Settings, 1, 45, 50, wind: 0);

        Assert.True(trajectory.Impact.Position.X < gorillas[1].Feet.X);
    }

    [Fact]
    public void MirroredThrowsAreSymmetric()
    {
        IReadOnlyList<Gorilla> gorillas =
        [
            new Gorilla(0, new Vec2(60, 100), 0),
            new Gorilla(1, new Vec2(260, 100), 1),
        ];

        var left = BananaSimulator.Simulate(Skyline.Empty, gorillas, Settings, 0, 50, 45, wind: 0);
        var right = BananaSimulator.Simulate(Skyline.Empty, gorillas, Settings, 1, 50, 45, wind: 0);

        var leftDistance = left.Impact.Position.X - gorillas[0].Feet.X;
        var rightDistance = gorillas[1].Feet.X - right.Impact.Position.X;

        Assert.Equal(leftDistance, rightDistance, 6);
    }

    [Fact]
    public void ThrowerIsNotHitByItsOwnLaunch()
    {
        var gorillas = OpenFieldGorillas();

        var trajectory = BananaSimulator.Simulate(Skyline.Empty, gorillas, Settings, 0, 80, 40, wind: 0);

        Assert.NotEqual(0, trajectory.Impact.VictimSlot);
    }

    [Fact]
    public void AHeadwindCanBlowTheBananaBackOntoTheThrower()
    {
        var gorillas = OpenFieldGorillas();
        var selfHits = 0;

        for (var wind = -Settings.MaxWind; wind <= 0; wind += 0.1)
        {
            var trajectory = BananaSimulator.Simulate(Skyline.Empty, gorillas, Settings, 0, 85, 40, wind);
            if (trajectory.Impact.VictimSlot == 0)
            {
                selfHits++;
            }
        }

        Assert.True(selfHits > 0, "No headwind between -MaxWind and 0 blew a lobbed banana back onto the thrower.");
    }

    [Fact]
    public void BananaHittingABuildingStopsThere()
    {
        var skyline = new Skyline([new Building(0, 320, 60, 0)], []);
        IReadOnlyList<Gorilla> gorillas =
        [
            new Gorilla(0, new Vec2(40, 60), 0),
            new Gorilla(1, new Vec2(280, 60), 0),
        ];

        var trajectory = BananaSimulator.Simulate(skyline, gorillas, Settings, 0, 45, 40, wind: 0);

        Assert.Equal(ImpactKind.Building, trajectory.Impact.Kind);
        Assert.True(trajectory.Impact.Position.Y > 0);
    }

    [Fact]
    public void CratersLetLaterBananasPassThroughDamagedWalls()
    {
        var building = new Building(100, 40, 120, 0);
        var probe = new Vec2(120, 60);
        var intact = new Skyline([building], []);

        Assert.True(intact.IsSolidAt(probe));

        var damaged = intact.WithCrater(new Crater(probe, 12));

        Assert.False(damaged.IsSolidAt(probe));
        Assert.True(damaged.IsSolidAt(new Vec2(120, 30)));
    }

    [Fact]
    public void BananasLeavingTheScreenAreLost()
    {
        var gorillas = OpenFieldGorillas();

        var trajectory = BananaSimulator.Simulate(Skyline.Empty, gorillas, Settings, 0, 45, Settings.MaxVelocity, wind: 0);

        Assert.Equal(ImpactKind.LostOffScreen, trajectory.Impact.Kind);
    }

    [Fact]
    public void TrajectoryPointsStartAtTheThrowOrigin()
    {
        var gorillas = OpenFieldGorillas();

        var trajectory = BananaSimulator.Simulate(Skyline.Empty, gorillas, Settings, 0, 45, 50, wind: 0);

        Assert.Equal(gorillas[0].ThrowOrigin(Settings, 1), trajectory.Points[0]);
        Assert.True(trajectory.Points.Count > 1);
        Assert.True(trajectory.Duration > 0);
    }
}
