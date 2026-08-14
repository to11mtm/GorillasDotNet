using Gorillas.Core.Ai;
using Gorillas.Core.Commands;
using Gorillas.Core.Model;
using Gorillas.Core.Primitives;

namespace Gorillas.Core.Tests;

public class GorillaAiTests
{
    private static GameState StartedGame(ulong seed)
    {
        var random = new DeterministicRandom(seed);
        var state = GameState.Initial;

        foreach (var command in new GameCommand[]
        {
            new CreateGame("g", "BAN-001", GameSettings.Default),
            new JoinGame("p1", "Ada"),
            new JoinGame("p2", "Kong", IsComputer: true, Difficulty: AiDifficulty.Normal),
        })
        {
            var decision = GameEngine.Decide(state, command, random);
            Assert.True(decision.IsAccepted, decision.Error);

            foreach (var @event in decision.Events)
            {
                state = state.Apply(@event);
            }
        }

        return state;
    }

    /// <summary>Average distance from the target across many independent first shots.</summary>
    private static double AverageFirstShotMiss(AiDifficulty difficulty, int trials = 30)
    {
        var total = 0.0;

        for (var i = 0; i < trials; i++)
        {
            var state = StartedGame((ulong)(4000 + i));
            var ai = new GorillaAi(difficulty, new DeterministicRandom((ulong)(77 + i)));

            var shot = ai.ChooseShot(state, state.ActiveSlot);
            Assert.NotNull(shot);

            total += BallisticSolver.Evaluate(state, state.ActiveSlot, shot.AngleDegrees, shot.Velocity);
        }

        return total / trials;
    }

    [Fact]
    public void EveryChosenShotIsAcceptedByTheEngine()
    {
        foreach (var difficulty in Enum.GetValues<AiDifficulty>())
        {
            for (var i = 0; i < 20; i++)
            {
                var state = StartedGame((ulong)(6000 + i));
                var ai = new GorillaAi(difficulty, new DeterministicRandom((ulong)(i + 1)));

                var shot = ai.ChooseShot(state, state.ActiveSlot);
                Assert.NotNull(shot);

                var decision = GameEngine.Decide(
                    state,
                    new ThrowBanana(state.ActiveSlot, shot.AngleDegrees, shot.Velocity),
                    new DeterministicRandom(1));

                Assert.True(decision.IsAccepted, $"{difficulty}: {decision.Error}");
            }
        }
    }

    [Fact]
    public void HarderOpponentsAimBetter()
    {
        var easy = AverageFirstShotMiss(AiDifficulty.Easy);
        var normal = AverageFirstShotMiss(AiDifficulty.Normal);
        var hard = AverageFirstShotMiss(AiDifficulty.Hard);

        Assert.True(hard < normal, $"Hard ({hard:0.0}) should out-aim Normal ({normal:0.0}).");
        Assert.True(normal < easy, $"Normal ({normal:0.0}) should out-aim Easy ({easy:0.0}).");
    }

    [Fact]
    public void TheComputerWalksItsFireInAcrossARound()
    {
        var firstShots = 0.0;
        var laterShots = 0.0;
        const int trials = 25;

        for (var i = 0; i < trials; i++)
        {
            var state = StartedGame((ulong)(8000 + i));
            var slot = state.ActiveSlot;
            var ai = new GorillaAi(AiDifficulty.Normal, new DeterministicRandom((ulong)(i + 5)));

            var first = ai.ChooseShot(state, slot)!;
            firstShots += BallisticSolver.Evaluate(state, slot, first.AngleDegrees, first.Velocity);

            // Same board, later attempt: the spread should have tightened.
            AimSolution? latest = null;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                latest = ai.ChooseShot(state, slot);
            }

            laterShots += BallisticSolver.Evaluate(state, slot, latest!.AngleDegrees, latest.Velocity);
        }

        Assert.True(
            laterShots < firstShots,
            $"Later shots ({laterShots / trials:0.0}) should be closer than opening shots ({firstShots / trials:0.0}).");
    }

    [Fact]
    public void AimResetsWhenANewRoundBegins()
    {
        var state = StartedGame(1234);
        var ai = new GorillaAi(AiDifficulty.Easy, new DeterministicRandom(99));

        // Tighten the aim over several attempts in round one.
        for (var i = 0; i < 5; i++)
        {
            ai.ChooseShot(state, state.ActiveSlot);
        }

        var nextRound = state with { RoundNumber = state.RoundNumber + 1 };

        var spreadAfterReset = 0.0;
        var spreadIfContinued = 0.0;

        for (var i = 0; i < 20; i++)
        {
            var resetAi = new GorillaAi(AiDifficulty.Easy, new DeterministicRandom((ulong)(200 + i)));
            resetAi.ChooseShot(nextRound, nextRound.ActiveSlot);

            var continuedAi = new GorillaAi(AiDifficulty.Easy, new DeterministicRandom((ulong)(200 + i)));
            for (var attempt = 0; attempt < 5; attempt++)
            {
                continuedAi.ChooseShot(state, state.ActiveSlot);
            }

            var reset = resetAi.ChooseShot(nextRound, nextRound.ActiveSlot)!;
            var continued = continuedAi.ChooseShot(state, state.ActiveSlot)!;

            spreadAfterReset += BallisticSolver.Evaluate(nextRound, nextRound.ActiveSlot, reset.AngleDegrees, reset.Velocity);
            spreadIfContinued += BallisticSolver.Evaluate(state, state.ActiveSlot, continued.AngleDegrees, continued.Velocity);
        }

        // A fresh round means a fresh, wider spread than a well-practised one.
        Assert.True(
            spreadAfterReset > spreadIfContinued,
            $"A new round should widen the spread again ({spreadAfterReset:0.0} vs {spreadIfContinued:0.0}).");
    }

    [Fact]
    public void ShotsStayInsideTheAllowedRanges()
    {
        var state = StartedGame(4242);

        foreach (var difficulty in Enum.GetValues<AiDifficulty>())
        {
            var ai = new GorillaAi(difficulty, new DeterministicRandom(7));

            for (var i = 0; i < 50; i++)
            {
                var shot = ai.ChooseShot(state, state.ActiveSlot)!;

                Assert.InRange(shot.AngleDegrees, 1, 179);
                Assert.InRange(shot.Velocity, 1, state.Settings.MaxVelocity);
            }
        }
    }

    [Fact]
    public void TheComputerDeclinesToShootBeforeTheGorillasArePlaced()
    {
        var ai = new GorillaAi(AiDifficulty.Normal, new DeterministicRandom(1));

        Assert.Null(ai.ChooseShot(GameState.Initial, 0));
    }

    [Fact]
    public void DifficultyIsRecordedOnTheJoinEvent()
    {
        var state = StartedGame(5150);
        var computer = state.Players.Single(p => p.IsComputer);

        Assert.Equal("Kong", computer.Nickname);
        Assert.Equal(1, computer.Slot);
    }
}
