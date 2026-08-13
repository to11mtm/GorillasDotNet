using Akka.Actor;
using Akka.Hosting;
using Gorillas.Actors;
using Gorillas.Contracts;
using Gorillas.Core.Ai;
using Microsoft.AspNetCore.SignalR;

namespace Gorillas.Server.Realtime;

/// <summary>
/// Thin transport in front of the actor layer. It never decides anything about the game: it
/// resolves who the caller is, forwards the intent, and returns the actor's answer.
/// </summary>
public sealed class GameHub(
    ActorRegistry actors,
    ConnectionRegistry connections,
    ILogger<GameHub> logger) : Hub<IGameClient>
{
    private static readonly TimeSpan AskTimeout = TimeSpan.FromSeconds(15);

    private IActorRef Lobby => actors.Get<LobbyActor>();

    public async Task<JoinResult> CreateGame(string playerId, string nickname)
    {
        var result = await Lobby.Ask<JoinResult>(
            new LobbyMessages.CreateGame(playerId, Sanitize(nickname)), AskTimeout);

        await AttachAsync(result, playerId, nickname);
        return result;
    }

    public async Task<JoinResult> CreateSoloGame(string playerId, string nickname, AiDifficulty difficulty)
    {
        var result = await Lobby.Ask<JoinResult>(
            new LobbyMessages.CreateSoloGame(playerId, Sanitize(nickname), difficulty), AskTimeout);

        await AttachAsync(result, playerId, nickname);
        return result;
    }

    public async Task<JoinResult> JoinGame(string code, string playerId, string nickname, bool asObserver)
    {
        var result = await Lobby.Ask<JoinResult>(
            new LobbyMessages.JoinByCode(code, playerId, Sanitize(nickname), asObserver), AskTimeout);

        await AttachAsync(result, playerId, nickname);
        return result;
    }

    public Task<CommandAck> Throw(double angleDegrees, double velocity) =>
        SendAsync(state => new GameMessages.Throw(state.PlayerId, angleDegrees, velocity));

    public Task<CommandAck> NextRound() =>
        SendAsync(state => new GameMessages.StartNextRound(state.PlayerId));

    public Task<CommandAck> Forfeit() =>
        SendAsync(state => new GameMessages.Forfeit(state.PlayerId));

    /// <summary>Catch-up after a dropped connection: hand back everything past the client's cursor.</summary>
    public async Task<EventBatch> Resync(long afterSequence)
    {
        var state = connections.Get(Context.ConnectionId);
        if (state is null)
        {
            return new EventBatch(string.Empty, []);
        }

        var game = await ResolveGameAsync(state.GameId);
        return game is null
            ? new EventBatch(state.GameId, [])
            : await game.Ask<EventBatch>(new GameMessages.Resync(afterSequence), AskTimeout);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var state = connections.Detach(Context.ConnectionId);

        if (state is not null)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GameHubRoutes.GroupFor(state.GameId));

            // Only report absence once every connection for that player has gone, so a
            // second tab closing does not mark an active player as offline.
            if (connections.IsFullyDisconnected(state.GameId, state.PlayerId) &&
                await ResolveGameAsync(state.GameId) is { } game)
            {
                game.Tell(new GameMessages.Disconnected(state.PlayerId));
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    private async Task<CommandAck> SendAsync(Func<ConnectionState, object> message)
    {
        var state = connections.Get(Context.ConnectionId);
        if (state is null)
        {
            return CommandAck.Fail("You are not connected to a game.");
        }

        var game = await ResolveGameAsync(state.GameId);
        if (game is null)
        {
            return CommandAck.Fail("That game is no longer active.");
        }

        try
        {
            return await game.Ask<CommandAck>(message(state), AskTimeout);
        }
        catch (AskTimeoutException)
        {
            logger.LogWarning("Timed out talking to match {GameId}.", state.GameId);
            return CommandAck.Fail("The game did not respond. Please try again.");
        }
    }

    private async Task AttachAsync(JoinResult result, string playerId, string nickname)
    {
        if (!result.Success)
        {
            return;
        }

        connections.Attach(
            Context.ConnectionId,
            new ConnectionState(result.GameId, playerId, Sanitize(nickname), result.Role));

        await Groups.AddToGroupAsync(Context.ConnectionId, GameHubRoutes.GroupFor(result.GameId));
    }

    private async Task<IActorRef?> ResolveGameAsync(string gameId)
    {
        var reference = await Lobby.Ask<LobbyMessages.GameRef>(new LobbyMessages.ResolveGame(gameId), AskTimeout);
        return reference.Actor;
    }

    private static string Sanitize(string nickname)
    {
        var trimmed = (nickname ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return "Anonymous";
        }

        return trimmed.Length > 20 ? trimmed[..20] : trimmed;
    }
}
