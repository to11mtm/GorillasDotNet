using Gorillas.Core;
using Gorillas.Core.Commands;
using Gorillas.Core.Events;
using Gorillas.Core.Model;
using Gorillas.Core.Primitives;

namespace Gorillas.Data.Tests;

/// <summary>Produces realistic event logs by driving the real engine, not hand-written fixtures.</summary>
public static class MatchFactory
{
    public static (GameState State, List<GameEvent> Log) PlayMatch(ulong seed = 31337, int throws = 4)
    {
        var random = new DeterministicRandom(seed);
        var log = new List<GameEvent>();
        var state = GameState.Initial;

        state = Run(state, new CreateGame("match-1", "BAN-7Q3", GameSettings.Default), random, log);
        state = Run(state, new JoinGame("p1", "Ada"), random, log);
        state = Run(state, new JoinGame("p2", "Grace"), random, log);

        for (var i = 0; i < throws && state.Phase == GamePhase.Aiming; i++)
        {
            state = Run(state, new ThrowBanana(state.ActiveSlot, 20 + (i * 6), 25 + (i * 8)), random, log);
        }

        return (state, log);
    }

    public static (GameState State, List<GameEvent> Log) PlayToCompletion(ulong seed = 99)
    {
        var (state, log) = PlayMatch(seed, throws: 0);
        var random = new DeterministicRandom(seed);

        state = Run(state, new Forfeit(1), random, log);
        return (state, log);
    }

    private static GameState Run(GameState state, GameCommand command, IRandomSource random, List<GameEvent> log)
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
}
