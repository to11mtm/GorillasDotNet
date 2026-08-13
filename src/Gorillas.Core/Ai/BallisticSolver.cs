using Gorillas.Core.Model;
using Gorillas.Core.Primitives;
using Gorillas.Core.Simulation;

namespace Gorillas.Core.Ai;

public sealed record AimSolution(double AngleDegrees, double Velocity, double Miss)
{
    /// <summary>Zero miss means the simulated banana actually lands on the opponent.</summary>
    public bool IsHit => Miss <= 0;
}

/// <summary>
/// Finds a shot by searching against the real simulator rather than a closed-form parabola.
/// That costs a few hundred simulations but means the answer already accounts for wind,
/// craters and buildings standing in the way — a closed-form solution would happily pick an
/// arc that flies straight into the neighbouring rooftop.
/// </summary>
public static class BallisticSolver
{
    private const double SelfHitPenalty = 100_000;

    public static AimSolution? Solve(GameState state, int slot)
    {
        if (state.Gorillas.Count < 2)
        {
            return null;
        }

        var best = Search(state, slot, 10, 86, 5, 10, state.Settings.MaxVelocity, 5, null);
        if (best is null)
        {
            return null;
        }

        // Two refinement passes around the coarse winner, each an order finer.
        best = Search(state, slot, best.AngleDegrees - 6, best.AngleDegrees + 6, 1.5,
            best.Velocity - 8, best.Velocity + 8, 1.5, best);

        best = Search(state, slot, best!.AngleDegrees - 1.5, best.AngleDegrees + 1.5, 0.3,
            best.Velocity - 2, best.Velocity + 2, 0.3, best);

        return best;
    }

    /// <summary>Scores a specific shot: 0 is a hit, larger is further from the target.</summary>
    public static double Evaluate(GameState state, int slot, double angleDegrees, double velocity)
    {
        var target = 1 - slot;
        var trajectory = BananaSimulator.Simulate(
            state.Skyline, state.Gorillas, state.Settings, slot, angleDegrees, velocity, state.Wind);

        if (trajectory.Impact.VictimSlot == target)
        {
            return 0;
        }

        if (trajectory.Impact.VictimSlot == slot)
        {
            return SelfHitPenalty;
        }

        return trajectory.Impact.Position.DistanceTo(TargetCentre(state, target));
    }

    private static Vec2 TargetCentre(GameState state, int target)
    {
        var gorilla = state.Gorillas[target];
        return new Vec2(gorilla.Feet.X, gorilla.Feet.Y + (state.Settings.GorillaHeight / 2));
    }

    private static AimSolution? Search(
        GameState state,
        int slot,
        double angleFrom,
        double angleTo,
        double angleStep,
        double velocityFrom,
        double velocityTo,
        double velocityStep,
        AimSolution? incumbent)
    {
        var best = incumbent;

        angleFrom = Math.Max(angleFrom, 1);
        angleTo = Math.Min(angleTo, 89);
        velocityFrom = Math.Max(velocityFrom, 1);
        velocityTo = Math.Min(velocityTo, state.Settings.MaxVelocity);

        for (var angle = angleFrom; angle <= angleTo; angle += angleStep)
        {
            for (var velocity = velocityFrom; velocity <= velocityTo; velocity += velocityStep)
            {
                var miss = Evaluate(state, slot, angle, velocity);

                if (best is null || miss < best.Miss)
                {
                    best = new AimSolution(angle, velocity, miss);

                    if (miss <= 0)
                    {
                        return best;
                    }
                }
            }
        }

        return best;
    }
}
