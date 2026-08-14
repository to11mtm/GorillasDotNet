using Gorillas.Core.Model;
using Gorillas.Core.Primitives;

namespace Gorillas.Core.Simulation;

public enum ImpactKind
{
    Gorilla,
    Building,
    Ground,
    LostOffScreen,
    TimedOut,
}

public sealed record Impact(ImpactKind Kind, Vec2 Position, int? VictimSlot)
{
    public bool IsHit => Kind == ImpactKind.Gorilla;
}

public sealed record Trajectory(IReadOnlyList<Vec2> Points, Impact Impact, double Duration);

/// <summary>
/// Fixed-timestep ballistic integration. Deterministic for a given state and throw, so the
/// server and every client (and the replay viewer) all produce the identical arc.
/// </summary>
public static class BananaSimulator
{
    public static Trajectory Simulate(
        Skyline skyline,
        IReadOnlyList<Gorilla> gorillas,
        GameSettings settings,
        int slot,
        double angleDegrees,
        double velocity,
        double wind)
    {
        var thrower = gorillas[slot];
        var facing = DirectionFor(slot);
        var radians = angleDegrees * Math.PI / 180.0;
        var speed = Math.Clamp(velocity, 0, settings.MaxVelocity);

        var velocityVector = new Vec2(
            Math.Cos(radians) * speed * facing,
            Math.Sin(radians) * speed);

        // Angles beyond 90 degrees lob the banana back over the gorilla's own shoulder, so the
        // launch point must move to whichever side it is actually travelling towards —
        // otherwise a backwards throw starts inside its own thrower.
        var launchSide = velocityVector.X switch
        {
            > 0 => 1,
            < 0 => -1,
            _ => facing,
        };

        var position = thrower.ThrowOrigin(settings, launchSide);

        var points = new List<Vec2> { position };
        var dt = settings.TimeStep;
        var steps = (int)(settings.MaxFlightSeconds / dt);
        var clearedThrower = false;

        for (var step = 0; step < steps; step++)
        {
            velocityVector = new Vec2(
                velocityVector.X + (wind * dt),
                velocityVector.Y - (settings.Gravity * dt));
            position += velocityVector * dt;
            points.Add(position);

            if (!clearedThrower && !thrower.Contains(settings, position))
            {
                clearedThrower = true;
            }

            if (position.X < -settings.OutOfBoundsMargin ||
                position.X > settings.Width + settings.OutOfBoundsMargin)
            {
                return new Trajectory(points, new Impact(ImpactKind.LostOffScreen, position, null), (step + 1) * dt);
            }

            var victim = HitGorilla(gorillas, settings, position, slot, clearedThrower);
            if (victim is not null)
            {
                return new Trajectory(points, new Impact(ImpactKind.Gorilla, position, victim), (step + 1) * dt);
            }

            if (position.Y <= 0)
            {
                var ground = position with { Y = 0 };
                points[^1] = ground;
                return new Trajectory(points, new Impact(ImpactKind.Ground, ground, null), (step + 1) * dt);
            }

            if (skyline.IsSolidAt(position))
            {
                return new Trajectory(points, new Impact(ImpactKind.Building, position, null), (step + 1) * dt);
            }
        }

        return new Trajectory(points, new Impact(ImpactKind.TimedOut, position, null), settings.MaxFlightSeconds);
    }

    /// <summary>Slot 0 stands on the left and throws right; slot 1 mirrors it.</summary>
    public static int DirectionFor(int slot) => slot == 0 ? 1 : -1;

    private static int? HitGorilla(
        IReadOnlyList<Gorilla> gorillas,
        GameSettings settings,
        Vec2 position,
        int throwerSlot,
        bool clearedThrower)
    {
        for (var i = 0; i < gorillas.Count; i++)
        {
            if (i == throwerSlot && !clearedThrower)
            {
                continue;
            }

            if (gorillas[i].Contains(settings, position))
            {
                return i;
            }
        }

        return null;
    }
}
