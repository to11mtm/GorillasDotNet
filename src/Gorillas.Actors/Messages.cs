using Akka.Actor;
using Gorillas.Core.Ai;
using Gorillas.Core.Model;

namespace Gorillas.Actors;

/// <summary>Requests understood by <see cref="GameActor"/>.</summary>
public static class GameMessages
{
    public sealed record Join(string PlayerId, string Nickname, bool AsObserver = false, bool IsComputer = false, AiDifficulty? Difficulty = null);

    /// <summary>
    /// Self-sent when a computer gorilla should take its shot. Carries the sequence it was
    /// scheduled at so a stale timer cannot fire a second throw after the state has moved on.
    /// </summary>
    public sealed record AiTurn(long AtSequence);

    public sealed record Throw(string PlayerId, double AngleDegrees, double Velocity);

    public sealed record StartNextRound(string PlayerId);

    public sealed record Forfeit(string PlayerId);

    public sealed record Resync(long AfterSequence);

    public sealed record Disconnected(string PlayerId);

    public sealed record GetSnapshot;

    public sealed record SnapshotReply(string GameId, string GameCode, long Sequence, int PlayerCount);
}

/// <summary>Requests understood by <see cref="LobbyActor"/>.</summary>
public static class LobbyMessages
{
    public sealed record CreateGame(string PlayerId, string Nickname, GameSettings? Settings = null);

    public sealed record CreateSoloGame(string PlayerId, string Nickname, AiDifficulty Difficulty, GameSettings? Settings = null);

    public sealed record JoinByCode(string Code, string PlayerId, string Nickname, bool AsObserver = false);

    public sealed record ResolveGame(string GameId);

    public sealed record GameRef(IActorRef? Actor, string? Error);
}
