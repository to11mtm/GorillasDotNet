using System.Collections.Concurrent;
using Gorillas.Contracts;

namespace Gorillas.Server.Realtime;

public sealed record ConnectionState(string GameId, string PlayerId, string Nickname, GameRole Role);

/// <summary>
/// Maps SignalR connections to the seat they occupy, so a dropped connection can be reported
/// to the right match and later commands can be attributed without trusting the client.
/// </summary>
public sealed class ConnectionRegistry
{
    private readonly ConcurrentDictionary<string, ConnectionState> _connections = new();

    public void Attach(string connectionId, ConnectionState state) => _connections[connectionId] = state;

    public ConnectionState? Get(string connectionId) =>
        _connections.TryGetValue(connectionId, out var state) ? state : null;

    public ConnectionState? Detach(string connectionId) =>
        _connections.TryRemove(connectionId, out var state) ? state : null;

    /// <summary>True when the player has no other live connection to the match.</summary>
    public bool IsFullyDisconnected(string gameId, string playerId) =>
        !_connections.Values.Any(c => c.GameId == gameId && c.PlayerId == playerId);
}
