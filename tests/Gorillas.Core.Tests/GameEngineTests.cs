using Gorillas.Core.Commands;
using Gorillas.Core.Events;
using Gorillas.Core.Model;
using Gorillas.Core.Primitives;
using Gorillas.Core.Simulation;

namespace Gorillas.Core.Tests;

public class GameEngineTests
{
    private static (GameState State, List<GameEvent> Log) StartedGame(ulong seed = 4242)
    {
        var random = new DeterministicRandom(seed);
        var log = new List<GameEvent>();
        var state = GameState.Initial;

        state = ApplyCommand(state, new CreateGame("game-1", "BANANA-1", GameSettings.Default), random, log);
        state = ApplyCommand(state, new JoinGame("p1", "Ada"), random, log);
        state = ApplyCommand(state, new JoinGame("p2", "Grace"), random, log);

        return (state, log);
    }

    private static GameState ApplyCommand(GameState state, GameCommand command, IRandomSource random, List<GameEvent> log)
    {
        var decision = GameEngine.Decide(state, command, random);
        Assert.True(decision.IsAccepted, decision.Error);

        foreach (var @event in decision.Events)
        {
            log.Add(@event);
            state = state.Apply(@event);
        }

        return state;
    }

    /// <summary>Finds a shot that lands on the opponent, proving a winning line exists on this skyline.</summary>
    private static ThrowBanana? FindWinningShot(GameState state)
    {
        var target = 1 - state.ActiveSlot;

        for (var angle = 5.0; angle <= 89.0; angle += 0.5)
        {
            for (var velocity = 5.0; velocity <= state.Settings.MaxVelocity; velocity += 0.5)
            {
                var trajectory = BananaSimulator.Simulate(
                    state.Skyline, state.Gorillas, state.Settings, state.ActiveSlot, angle, velocity, state.Wind);

                if (trajectory.Impact.VictimSlot == target)
                {
                    return new ThrowBanana(state.ActiveSlot, angle, velocity);
                }
            }
        }

        return null;
    }

    [Fact]
    public void CreatingAGameEmitsGameCreatedWithSettings()
    {
        var decision = GameEngine.Decide(GameState.Initial, new CreateGame("g", "CODE-1", GameSettings.Default), new DeterministicRandom(1));

        var created = Assert.IsType<GameCreated>(Assert.Single(decision.Events));
        Assert.Equal("g", created.GameId);
        Assert.Equal("CODE-1", created.GameCode);
        Assert.Equal(GameSettings.Default, created.Settings);
    }

    [Fact]
    public void CreatingTheSameGameTwiceIsRejected()
    {
        var random = new DeterministicRandom(1);
        var state = GameState.Initial.Apply(new GameCreated("g", "CODE-1", 7, GameSettings.Default));

        var decision = GameEngine.Decide(state, new CreateGame("g", "CODE-1", GameSettings.Default), random);

        Assert.False(decision.IsAccepted);
    }

    [Fact]
    public void TheSecondJoinStartsTheFirstRound()
    {
        var (state, log) = StartedGame();

        Assert.Equal(GamePhase.Aiming, state.Phase);
        Assert.Equal(1, state.RoundNumber);
        Assert.Equal(2, state.Players.Count);
        Assert.Contains(log, e => e is RoundStarted);
        Assert.NotEmpty(state.Skyline.Buildings);
        Assert.Equal(2, state.Gorillas.Count);
        Assert.InRange(Math.Abs(state.Wind), 0, state.Settings.MaxWind);
    }

    [Fact]
    public void AThirdPlayerCannotJoin()
    {
        var (state, _) = StartedGame();

        var decision = GameEngine.Decide(state, new JoinGame("p3", "Mallory"), new DeterministicRandom(3));

        Assert.False(decision.IsAccepted);
    }

    [Fact]
    public void RejoiningWithTheSamePlayerIdIsRejected()
    {
        var random = new DeterministicRandom(9);
        var log = new List<GameEvent>();
        var state = ApplyCommand(GameState.Initial, new CreateGame("g", "CODE-1", GameSettings.Default), random, log);
        state = ApplyCommand(state, new JoinGame("p1", "Ada"), random, log);

        var decision = GameEngine.Decide(state, new JoinGame("p1", "Ada"), random);

        Assert.False(decision.IsAccepted);
    }

    [Fact]
    public void ThrowingOutOfTurnIsRejected()
    {
        var (state, _) = StartedGame();

        var decision = GameEngine.Decide(state, new ThrowBanana(1 - state.ActiveSlot, 45, 50), new DeterministicRandom(1));

        Assert.False(decision.IsAccepted);
    }

    [Theory]
    [InlineData(-1, 50)]
    [InlineData(180, 50)]
    [InlineData(45, 0)]
    [InlineData(45, 1_000)]
    [InlineData(double.NaN, 50)]
    [InlineData(45, double.NaN)]
    public void InvalidAimIsRejected(double angle, double velocity)
    {
        var (state, _) = StartedGame();

        var decision = GameEngine.Decide(state, new ThrowBanana(state.ActiveSlot, angle, velocity), new DeterministicRandom(1));

        Assert.False(decision.IsAccepted);
    }

    [Fact]
    public void AMissPassesTheTurnToTheOtherPlayer()
    {
        var (state, log) = StartedGame();
        var thrower = state.ActiveSlot;

        state = ApplyCommand(state, new ThrowBanana(thrower, 5, 6), new DeterministicRandom(1), log);

        Assert.Equal(GamePhase.Aiming, state.Phase);
        Assert.Equal(1 - thrower, state.ActiveSlot);
        Assert.Equal([0, 0], state.Scores);
    }

    [Fact]
    public void AHitScoresAndEndsTheRound()
    {
        var (state, log) = StartedGame();
        var shot = FindWinningShot(state);
        Assert.NotNull(shot);

        var thrower = state.ActiveSlot;
        state = ApplyCommand(state, shot, new DeterministicRandom(1), log);

        Assert.Equal(GamePhase.RoundOver, state.Phase);
        Assert.Equal(1, state.Scores[thrower]);
        Assert.Equal(0, state.Scores[1 - thrower]);
        Assert.Contains(log, e => e is BananaImpacted { Kind: ImpactKind.Gorilla });
    }

    [Fact]
    public void AnImpactCarvesACraterIntoTheSkyline()
    {
        var (state, log) = StartedGame();

        state = ApplyCommand(state, new ThrowBanana(state.ActiveSlot, 45, 40), new DeterministicRandom(1), log);

        var impact = log.OfType<BananaImpacted>().Single();
        if (impact.Kind is ImpactKind.Building or ImpactKind.Gorilla)
        {
            Assert.Single(state.Skyline.Craters);
            Assert.False(state.Skyline.IsSolidAt(impact.Position));
        }
        else
        {
            Assert.Empty(state.Skyline.Craters);
        }
    }

    [Fact]
    public void StartingTheNextRoundMidRoundIsRejected()
    {
        var (state, _) = StartedGame();

        var decision = GameEngine.Decide(state, new StartNextRound(), new DeterministicRandom(1));

        Assert.False(decision.IsAccepted);
    }

    [Fact]
    public void WinningEnoughRoundsEndsTheMatch()
    {
        var (state, log) = StartedGame();
        var random = new DeterministicRandom(1);
        var settings = state.Settings;

        for (var round = 1; round <= settings.RoundsToWin; round++)
        {
            // Force the same player to open every round so they can win them all.
            if (state.ActiveSlot != 0)
            {
                state = ApplyCommand(state, new ThrowBanana(state.ActiveSlot, 5, 6), random, log);
            }

            var shot = FindWinningShot(state);
            Assert.NotNull(shot);
            state = ApplyCommand(state, shot, random, log);

            if (round < settings.RoundsToWin)
            {
                Assert.Equal(GamePhase.RoundOver, state.Phase);
                state = ApplyCommand(state, new StartNextRound(), random, log);
                Assert.Equal(round + 1, state.RoundNumber);
                Assert.Empty(state.Skyline.Craters);
            }
        }

        Assert.Equal(GamePhase.MatchOver, state.Phase);
        Assert.Equal(0, state.MatchWinnerSlot);
        Assert.Equal(settings.RoundsToWin, state.Scores[0]);
    }

    [Fact]
    public void ForfeitingHandsTheMatchToTheOpponent()
    {
        var (state, log) = StartedGame();

        state = ApplyCommand(state, new Forfeit(1), new DeterministicRandom(1), log);

        Assert.Equal(GamePhase.MatchOver, state.Phase);
        Assert.Equal(0, state.MatchWinnerSlot);
        Assert.Contains(log, e => e is MatchEnded { Reason: MatchEndReason.Forfeit });
    }

    [Fact]
    public void ReplayingTheEventLogRebuildsTheExactSameState()
    {
        var (state, log) = StartedGame();
        var random = new DeterministicRandom(1);

        for (var i = 0; i < 6 && state.Phase == GamePhase.Aiming; i++)
        {
            state = ApplyCommand(state, new ThrowBanana(state.ActiveSlot, 20 + (i * 7), 30 + (i * 9)), random, log);
        }

        var replayed = GameState.Replay(log);

        Assert.Equal(state, replayed);
    }

    [Fact]
    public void PartialReplayMatchesTheStateAtThatPointInTime()
    {
        var (state, log) = StartedGame();
        var random = new DeterministicRandom(1);
        var midpoint = log.Count;
        var midpointState = state;

        state = ApplyCommand(state, new ThrowBanana(state.ActiveSlot, 30, 40), random, log);

        Assert.Equal(midpointState, GameState.Replay(log.Take(midpoint)));
        Assert.NotEqual(midpointState, state);
    }
}
