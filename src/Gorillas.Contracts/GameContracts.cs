using Gorillas.Core.Events;

namespace Gorillas.Contracts;

public enum GameRole
{
    Player,
    Observer,
}

/// <summary>An event plus its position in the match log. The sequence is the resync cursor.</summary>
public sealed record EventEnvelope(long Sequence, GameEvent Event);

public sealed record EventBatch(string GameId, IReadOnlyList<EventEnvelope> Events);

public sealed record ParticipantInfo(string PlayerId, string Nickname, int? Slot, GameRole Role, bool Connected);

public sealed record PresenceUpdate(string GameId, IReadOnlyList<ParticipantInfo> Participants);

public sealed record JoinResult(
    bool Success,
    string? Error,
    string GameId,
    string GameCode,
    GameRole Role,
    int? Slot,
    long Sequence,
    IReadOnlyList<EventEnvelope> Backlog)
{
    public static JoinResult Failed(string error) =>
        new(false, error, string.Empty, string.Empty, GameRole.Observer, null, 0, []);
}

public sealed record CommandAck(bool Accepted, string? Error)
{
    public static CommandAck Ok { get; } = new(true, null);

    public static CommandAck Fail(string error) => new(false, error);
}

/// <summary>Server-to-client calls. Implemented by the SignalR client proxy.</summary>
public interface IGameClient
{
    Task ReceiveEvents(EventBatch batch);

    Task ReceivePresence(PresenceUpdate presence);

    Task ReceiveError(string message);
}

/// <summary>
/// Lets the actor layer broadcast without referencing the web host. The server implementation
/// forwards to a SignalR group; tests substitute a recording fake.
/// </summary>
public interface IGameEventPublisher
{
    Task PublishEventsAsync(EventBatch batch);

    Task PublishPresenceAsync(PresenceUpdate presence);
}

public static class GameHubRoutes
{
    public const string Path = "/hubs/game";

    public static string GroupFor(string gameId) => $"game:{gameId}";
}
