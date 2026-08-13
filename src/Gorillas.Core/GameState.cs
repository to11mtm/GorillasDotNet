using Gorillas.Core.Events;
using Gorillas.Core.Model;
using Gorillas.Core.Primitives;
using Gorillas.Core.Simulation;

namespace Gorillas.Core;

public enum GamePhase
{
    WaitingForPlayers,
    Aiming,
    BananaInFlight,
    RoundOver,
    MatchOver,
}

public sealed record PlayerInfo(int Slot, string PlayerId, string Nickname, bool IsComputer, bool Connected = true);

/// <summary>
/// Immutable snapshot of a match. Built only by folding <see cref="GameEvent"/>s through
/// <see cref="Apply"/>; nothing else may mutate it.
/// </summary>
public sealed record GameState
{
    public static GameState Initial { get; } = new();

    public string GameId { get; init; } = string.Empty;

    public string GameCode { get; init; } = string.Empty;

    public ulong Seed { get; init; }

    public GameSettings Settings { get; init; } = GameSettings.Default;

    public GamePhase Phase { get; init; } = GamePhase.WaitingForPlayers;

    public IReadOnlyList<PlayerInfo> Players { get; init; } = [];

    public Skyline Skyline { get; init; } = Skyline.Empty;

    public IReadOnlyList<Gorilla> Gorillas { get; init; } = [];

    public int RoundNumber { get; init; }

    public double Wind { get; init; }

    public int ActiveSlot { get; init; }

    public IReadOnlyList<int> Scores { get; init; } = [0, 0];

    public BananaThrown? PendingThrow { get; init; }

    public int? MatchWinnerSlot { get; init; }

    public bool IsFull => Players.Count >= 2;

    public PlayerInfo? PlayerInSlot(int slot) => Players.FirstOrDefault(p => p.Slot == slot);

    public bool Equals(GameState? other) =>
        other is not null &&
        GameId == other.GameId &&
        GameCode == other.GameCode &&
        Seed == other.Seed &&
        Settings == other.Settings &&
        Phase == other.Phase &&
        RoundNumber == other.RoundNumber &&
        Wind.Equals(other.Wind) &&
        ActiveSlot == other.ActiveSlot &&
        PendingThrow == other.PendingThrow &&
        MatchWinnerSlot == other.MatchWinnerSlot &&
        Skyline == other.Skyline &&
        StructuralEquality.SequenceEqual(Players, other.Players) &&
        StructuralEquality.SequenceEqual(Gorillas, other.Gorillas) &&
        StructuralEquality.SequenceEqual(Scores, other.Scores);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(GameId);
        hash.Add(Seed);
        hash.Add(Phase);
        hash.Add(RoundNumber);
        hash.Add(Wind);
        hash.Add(ActiveSlot);
        hash.Add(Skyline);
        hash.Add(StructuralEquality.SequenceHash(Players));
        hash.Add(StructuralEquality.SequenceHash(Gorillas));
        hash.Add(StructuralEquality.SequenceHash(Scores));
        return hash.ToHashCode();
    }

    public GameState Apply(GameEvent @event) => @event switch
    {
        GameCreated e => this with
        {
            GameId = e.GameId,
            GameCode = e.GameCode,
            Seed = e.Seed,
            Settings = e.Settings,
            Scores = [0, 0],
            Phase = GamePhase.WaitingForPlayers,
        },

        PlayerJoined e => this with
        {
            Players = [.. Players.Where(p => p.Slot != e.Slot), new PlayerInfo(e.Slot, e.PlayerId, e.Nickname, e.IsComputer)],
        },

        PlayerLeft e => this with
        {
            Players = [.. Players.Select(p => p.Slot == e.Slot ? p with { Connected = false } : p)],
        },

        RoundStarted e => StartRound(e),

        BananaThrown e => this with { Phase = GamePhase.BananaInFlight, PendingThrow = e },

        BananaImpacted e => ApplyImpact(e),

        TurnAdvanced e => this with { ActiveSlot = e.Slot, Phase = GamePhase.Aiming, PendingThrow = null },

        RoundEnded e => ApplyRoundEnded(e),

        MatchEnded e => this with { Phase = GamePhase.MatchOver, MatchWinnerSlot = e.WinnerSlot },

        _ => this,
    };

    public static GameState Replay(IEnumerable<GameEvent> events)
    {
        var state = Initial;
        foreach (var @event in events)
        {
            state = state.Apply(@event);
        }

        return state;
    }

    private GameState StartRound(RoundStarted e)
    {
        var skyline = SkylineGenerator.Generate(Settings, Seed, e.RoundNumber);
        return this with
        {
            RoundNumber = e.RoundNumber,
            Wind = e.Wind,
            Skyline = skyline,
            Gorillas = SkylineGenerator.PlaceGorillas(skyline),
            ActiveSlot = e.StartingSlot,
            Phase = GamePhase.Aiming,
            PendingThrow = null,
        };
    }

    private GameState ApplyImpact(BananaImpacted e)
    {
        var skyline = e.CraterRadius > 0
            ? Skyline.WithCrater(new Crater(e.Position, e.CraterRadius))
            : Skyline;

        return this with { Skyline = skyline, PendingThrow = null };
    }

    private GameState ApplyRoundEnded(RoundEnded e)
    {
        var scores = Scores;
        if (e.WinnerSlot is { } winner)
        {
            var updated = Scores.ToArray();
            updated[winner]++;
            scores = updated;
        }

        return this with { Scores = scores, Phase = GamePhase.RoundOver, PendingThrow = null };
    }
}
