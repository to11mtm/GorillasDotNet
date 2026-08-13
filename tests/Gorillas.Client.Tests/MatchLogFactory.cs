using Gorillas.Contracts;
using Gorillas.Core;
using Gorillas.Core.Commands;
using Gorillas.Core.Events;
using Gorillas.Core.Model;
using Gorillas.Core.Primitives;

namespace Gorillas.Client.Tests;

/// <summary>Builds realistic match logs by driving the real engine, not hand-written fixtures.</summary>
public static class MatchLogFactory
{
    public static IReadOnlyList<EventEnvelope> Build(ulong seed = 4242, int throws = 6)
    {
        var random = new DeterministicRandom(seed);
        var events = new List<GameEvent>();
        var state = GameState.Initial;

        state = Run(state, new CreateGame("replay-1", "BAN-7Q3", GameSettings.Default), random, events);
        state = Run(state, new JoinGame("p1", "Ada"), random, events);
        state = Run(state, new JoinGame("p2", "Grace"), random, events);

        for (var i = 0; i < throws; i++)
        {
            if (state.Phase == GamePhase.RoundOver)
            {
                state = Run(state, new StartNextRound(), random, events);
            }

            if (state.Phase != GamePhase.Aiming)
            {
                break;
            }

            // Wrapped so long runs stay inside the engine's valid aim range.
            var angle = 15 + ((i * 7) % 70);
            var velocity = 25 + ((i * 11) % 90);

            state = Run(state, new ThrowBanana(state.ActiveSlot, angle, velocity), random, events);
        }

        return [.. events.Select((e, i) => new EventEnvelope(i + 1, e))];
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
