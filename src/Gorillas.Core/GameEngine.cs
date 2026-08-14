using Gorillas.Core.Commands;
using Gorillas.Core.Events;
using Gorillas.Core.Primitives;
using Gorillas.Core.Simulation;

namespace Gorillas.Core;

public sealed record Decision(IReadOnlyList<GameEvent> Events, string? Error = null)
{
    public bool IsAccepted => Error is null;

    public static Decision Accept(params GameEvent[] events) => new(events);

    public static Decision Reject(string error) => new([], error);
}

/// <summary>
/// The only place game decisions are made. Pure apart from the injected randomness, and any
/// randomness it consumes is written into the resulting events so replays stay faithful.
/// </summary>
public static class GameEngine
{
    public static Decision Decide(GameState state, GameCommand command, IRandomSource random) => command switch
    {
        CreateGame c => DecideCreate(state, c, random),
        JoinGame c => DecideJoin(state, c, random),
        ThrowBanana c => DecideThrow(state, c, random),
        StartNextRound => DecideStartNextRound(state, random),
        Forfeit c => DecideForfeit(state, c),
        _ => Decision.Reject($"Unsupported command '{command.GetType().Name}'."),
    };

    private static Decision DecideCreate(GameState state, CreateGame command, IRandomSource random)
    {
        if (state.GameId.Length > 0)
        {
            return Decision.Reject("Game already exists.");
        }

        var seed = (ulong)random.NextInt(int.MinValue, int.MaxValue) ^ ((ulong)random.NextInt(int.MinValue, int.MaxValue) << 32);
        return Decision.Accept(new GameCreated(command.GameId, command.GameCode, seed, command.Settings));
    }

    private static Decision DecideJoin(GameState state, JoinGame command, IRandomSource random)
    {
        if (state.Players.Any(p => p.PlayerId == command.PlayerId))
        {
            return Decision.Reject("Player has already joined this game.");
        }

        if (state.IsFull)
        {
            return Decision.Reject("Game is full.");
        }

        var slot = state.Players.Count == 0 ? 0 : 1 - state.Players[0].Slot;
        var joined = new PlayerJoined(slot, command.PlayerId, command.Nickname, command.IsComputer)
        {
            Difficulty = command.Difficulty,
        };

        if (state.Players.Count + 1 < 2)
        {
            return Decision.Accept(joined);
        }

        return Decision.Accept(joined, NewRound(state, 1, random));
    }

    private static Decision DecideThrow(GameState state, ThrowBanana command, IRandomSource random)
    {
        if (state.Phase != GamePhase.Aiming)
        {
            return Decision.Reject($"Cannot throw while the game is {state.Phase}.");
        }

        if (command.Slot != state.ActiveSlot)
        {
            return Decision.Reject("It is not that player's turn.");
        }

        if (double.IsNaN(command.AngleDegrees) || command.AngleDegrees < 0 || command.AngleDegrees >= 180)
        {
            return Decision.Reject("Angle must be between 0 and 180 degrees.");
        }

        if (double.IsNaN(command.Velocity) || command.Velocity <= 0 || command.Velocity > state.Settings.MaxVelocity)
        {
            return Decision.Reject($"Velocity must be between 0 and {state.Settings.MaxVelocity}.");
        }

        var trajectory = BananaSimulator.Simulate(
            state.Skyline,
            state.Gorillas,
            state.Settings,
            command.Slot,
            command.AngleDegrees,
            command.Velocity,
            state.Wind);

        var craterRadius = trajectory.Impact.Kind is ImpactKind.Building or ImpactKind.Gorilla
            ? state.Settings.ExplosionRadius
            : 0;

        var events = new List<GameEvent>
        {
            new BananaThrown(command.Slot, command.AngleDegrees, command.Velocity),
            new BananaImpacted(trajectory.Impact.Kind, trajectory.Impact.Position, trajectory.Impact.VictimSlot, craterRadius),
        };

        if (trajectory.Impact.VictimSlot is { } victim)
        {
            var winner = 1 - victim;
            events.Add(new RoundEnded(state.RoundNumber, winner));

            var scoreAfter = state.Scores[winner] + 1;
            if (scoreAfter >= state.Settings.RoundsToWin)
            {
                events.Add(new MatchEnded(winner, MatchEndReason.ScoreReached));
            }
        }
        else
        {
            events.Add(new TurnAdvanced(1 - command.Slot));
        }

        return Decision.Accept([.. events]);
    }

    private static Decision DecideStartNextRound(GameState state, IRandomSource random)
    {
        if (state.Phase != GamePhase.RoundOver)
        {
            return Decision.Reject("The current round is still in progress.");
        }

        return Decision.Accept(NewRound(state, state.RoundNumber + 1, random));
    }

    private static Decision DecideForfeit(GameState state, Forfeit command)
    {
        if (state.Phase == GamePhase.MatchOver)
        {
            return Decision.Reject("The match is already over.");
        }

        return Decision.Accept(new MatchEnded(1 - command.Slot, MatchEndReason.Forfeit));
    }

    private static RoundStarted NewRound(GameState state, int roundNumber, IRandomSource random)
    {
        var wind = ((random.NextDouble() * 2) - 1) * state.Settings.MaxWind;

        // Alternate who opens each round so neither player keeps the first-shot advantage.
        var startingSlot = (roundNumber - 1) % 2;
        return new RoundStarted(roundNumber, wind, startingSlot);
    }
}
