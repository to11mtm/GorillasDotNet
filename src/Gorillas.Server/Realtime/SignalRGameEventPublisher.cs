using Gorillas.Contracts;
using Microsoft.AspNetCore.SignalR;

namespace Gorillas.Server.Realtime;

/// <summary>
/// Bridges the actor layer to SignalR. Every match has a group, so one broadcast reaches both
/// players and every observer.
/// </summary>
public sealed class SignalRGameEventPublisher(IHubContext<GameHub, IGameClient> hub) : IGameEventPublisher
{
    public Task PublishEventsAsync(EventBatch batch) =>
        hub.Clients.Group(GameHubRoutes.GroupFor(batch.GameId)).ReceiveEvents(batch);

    public Task PublishPresenceAsync(PresenceUpdate presence) =>
        hub.Clients.Group(GameHubRoutes.GroupFor(presence.GameId)).ReceivePresence(presence);
}
