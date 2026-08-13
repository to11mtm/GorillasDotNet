using Gorillas.Core.Ai;
using Gorillas.Core.Commands;
using Gorillas.Core.Events;
using Gorillas.Core.Model;
using Gorillas.Core.Primitives;
using Gorillas.Core.Simulation;

namespace Gorillas.Core.Tests;

public class BallisticSolverTests
{
    private static GameState StartedGame(ulong seed)
    {
        var random = new DeterministicRandom(seed);
        var state = GameState.Initial;

        state = Run(state, new CreateGame("g", "BAN-001", GameSettings.Default), random);
        state = Run(state, new JoinGame("p1", "Ada"), random);
        state = Run(state, new JoinGame("p2", "Grace"), random);

        return state;
    }

    private static GameState Run(GameState state, GameCommand command, IRandomSource random)
    {
        var decision = GameEngine.Decide(state, command, random);
        Assert.True(decision.IsAccepted, decision.Error);

        foreach (var @event in decision.Events)
        {
            state = state.Apply(@event);
        }

        return state;
    }

    [Fact]
    public void TheSolverFindsAHitOnMostSkylines()
    {
        var hits = 0;
        const int trials = 25;

        for (var i = 0; i < trials; i++)
        {
            var state = StartedGame((ulong)(1000 + i));
            var solution = BallisticSolver.Solve(state, state.ActiveSlot);

            if (solution?.IsHit == true)
            {
                hits++;
            }
        }

        // A shot always exists in principle, but wind and blocking towers make a few genuinely
        // hard. Anything below this would mean the search is broken, not merely unlucky.
        Assert.True(hits >= trials - 3, $"Solver only found {hits} hits out of {trials}.");
    }

    [Fact]
    public void ASolvedHitReallyLandsOnTheOpponent()
    {
        var state = StartedGame(2024);
        var slot = state.ActiveSlot;

        var solution = BallisticSolver.Solve(state, slot);
        Assert.NotNull(solution);
        Assert.True(solution.IsHit);

        var trajectory = BananaSimulator.Simulate(
            state.Skyline, state.Gorillas, state.Settings, slot, solution.AngleDegrees, solution.Velocity, state.Wind);

        Assert.Equal(ImpactKind.Gorilla, trajectory.Impact.Kind);
        Assert.Equal(1 - slot, trajectory.Impact.VictimSlot);
    }

    [Fact]
    public void TheSolutionIsAlwaysAValidCommand()
    {
        for (var i = 0; i < 15; i++)
        {
            var state = StartedGame((ulong)(500 + i));
            var solution = BallisticSolver.Solve(state, state.ActiveSlot);

            Assert.NotNull(solution);
            Assert.InRange(solution.AngleDegrees, 1, 89);
            Assert.InRange(solution.Velocity, 0.1, state.Settings.MaxVelocity);

            var decision = GameEngine.Decide(
                state,
                new ThrowBanana(state.ActiveSlot, solution.AngleDegrees, solution.Velocity),
                new DeterministicRandom(1));

            Assert.True(decision.IsAccepted, decision.Error);
        }
    }

    [Fact]
    public void EvaluateScoresAHitAsZeroAndAMissAsADistance()
    {
        var state = StartedGame(31337);
        var slot = state.ActiveSlot;
        var solution = BallisticSolver.Solve(state, slot);

        Assert.NotNull(solution);
        Assert.Equal(0, BallisticSolver.Evaluate(state, slot, solution.AngleDegrees, solution.Velocity));

        // A feeble lob cannot reach the opponent, so it must score worse than the solution.
        Assert.True(BallisticSolver.Evaluate(state, slot, 45, 5) > 0);
    }

    [Fact]
    public void TheSolverNeverRecommendsShootingItself()
    {
        for (var i = 0; i < 15; i++)
        {
            var state = StartedGame((ulong)(9000 + i));
            var slot = state.ActiveSlot;
            var solution = BallisticSolver.Solve(state, slot);

            Assert.NotNull(solution);

            var trajectory = BananaSimulator.Simulate(
                state.Skyline, state.Gorillas, state.Settings, slot,
                solution.AngleDegrees, solution.Velocity, state.Wind);

            Assert.NotEqual(slot, trajectory.Impact.VictimSlot);
        }
    }

    [Fact]
    public void TheSolverCopesWithBothSeats()
    {
        var state = StartedGame(777);

        Assert.NotNull(BallisticSolver.Solve(state, 0));
        Assert.NotNull(BallisticSolver.Solve(state, 1));
    }

    [Fact]
    public void TheSolverReturnsNothingBeforeTheGorillasArePlaced()
    {
        Assert.Null(BallisticSolver.Solve(GameState.Initial, 0));
    }
}
