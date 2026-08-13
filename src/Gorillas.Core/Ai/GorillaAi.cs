using Gorillas.Core.Primitives;

namespace Gorillas.Core.Ai;

public enum AiDifficulty
{
    Easy,
    Normal,
    Hard,
}

/// <summary>
/// A computer gorilla. It solves for a good shot, then deliberately spoils its own aim by an
/// amount that shrinks with each attempt in the round, so it visibly walks its fire in rather
/// than either sniping instantly or flailing forever.
/// </summary>
public sealed class GorillaAi(AiDifficulty difficulty, IRandomSource random)
{
    private int _currentRound = -1;
    private int _attemptsThisRound;

    public AiDifficulty Difficulty { get; } = difficulty;

    /// <summary>Starting spread, as a fraction of the solved velocity.</summary>
    private double BaseVelocityError => Difficulty switch
    {
        AiDifficulty.Easy => 0.22,
        AiDifficulty.Normal => 0.10,
        _ => 0.035,
    };

    private double BaseAngleError => Difficulty switch
    {
        AiDifficulty.Easy => 12,
        AiDifficulty.Normal => 5,
        _ => 1.5,
    };

    /// <summary>How quickly the spread shrinks per attempt. Lower closes in faster.</summary>
    private double Decay => Difficulty switch
    {
        AiDifficulty.Easy => 0.80,
        AiDifficulty.Normal => 0.55,
        _ => 0.35,
    };

    /// <summary>Even a perfect player is never mechanically exact.</summary>
    private double FloorError => Difficulty switch
    {
        AiDifficulty.Easy => 0.05,
        AiDifficulty.Normal => 0.02,
        _ => 0.004,
    };

    public AimSolution? ChooseShot(GameState state, int slot)
    {
        TrackRound(state.RoundNumber);

        var solution = BallisticSolver.Solve(state, slot);
        if (solution is null)
        {
            return null;
        }

        var spread = Math.Pow(Decay, _attemptsThisRound);
        _attemptsThisRound++;

        var velocityError = Math.Max(BaseVelocityError * spread, FloorError);
        var angleError = BaseAngleError * spread;

        var velocity = solution.Velocity * (1 + (Gaussian() * velocityError));
        var angle = solution.AngleDegrees + (Gaussian() * angleError);

        return new AimSolution(
            Math.Clamp(angle, 1, 89),
            Math.Clamp(velocity, 1, state.Settings.MaxVelocity),
            solution.Miss);
    }

    private void TrackRound(int roundNumber)
    {
        if (roundNumber == _currentRound)
        {
            return;
        }

        _currentRound = roundNumber;
        _attemptsThisRound = 0;
    }

    /// <summary>
    /// Roughly normal error via the mean of two uniforms. Clustering near zero makes near
    /// misses common and wild shots rare, which reads as a player rather than a dice roll.
    /// </summary>
    private double Gaussian() => ((random.NextDouble() + random.NextDouble()) - 1);
}
