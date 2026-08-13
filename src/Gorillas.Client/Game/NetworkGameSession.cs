using Gorillas.Client.Rendering;
using Gorillas.Contracts;
using Gorillas.Core;
using Gorillas.Core.Ai;
using Gorillas.Core.Events;
using Gorillas.Core.Model;
using Microsoft.AspNetCore.SignalR.Client;

namespace Gorillas.Client.Game;

public enum ConnectionStatus
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
}

/// <summary>
/// A server-authoritative match. This client never decides anything: it sends intents and
/// folds the events the server sends back. Events arriving while a banana is mid-flight are
/// queued so the animation is never cut short or spoiled.
/// </summary>
public sealed class NetworkGameSession : IGameSession, IAsyncDisposable
{
    private readonly HubConnection _hub;
    private readonly Queue<EventEnvelope> _incoming = new();
    private readonly object _gate = new();

    private bool _animating;

    public NetworkGameSession(Uri hubUri, string playerId, string nickname)
    {
        PlayerId = playerId;
        Nickname = nickname;

        _hub = new HubConnectionBuilder()
            .WithUrl(hubUri)
            .WithAutomaticReconnect()
            .Build();

        _hub.On<EventBatch>(nameof(IGameClient.ReceiveEvents), batch => Ingest(batch.Events, animate: true));
        _hub.On<PresenceUpdate>(nameof(IGameClient.ReceivePresence), OnPresence);
        _hub.On<string>(nameof(IGameClient.ReceiveError), OnServerError);

        _hub.Reconnecting += _ =>
        {
            Status = ConnectionStatus.Reconnecting;
            Raise();
            return Task.CompletedTask;
        };

        _hub.Reconnected += async _ =>
        {
            Status = ConnectionStatus.Connected;
            Raise();
            await ResyncAsync();
        };

        _hub.Closed += _ =>
        {
            Status = ConnectionStatus.Disconnected;
            Raise();
            return Task.CompletedTask;
        };
    }

    public event Action? Changed;

    public event Action<ThrowAnimation>? ThrowStarted;

    public string PlayerId { get; }

    public string Nickname { get; }

    public GameState State { get; private set; } = GameState.Initial;

    public GameSettings Settings => State.Settings;

    public string? GameCode { get; private set; }

    public string? GameId { get; private set; }

    public int? MySlot { get; private set; }

    public bool IsSpectator => MySlot is null;

    public int? DefeatedSlot { get; private set; }

    public string? LastError { get; private set; }

    public ConnectionStatus Status { get; private set; } = ConnectionStatus.Disconnected;

    public IReadOnlyList<ParticipantInfo> Participants { get; private set; } = [];

    /// <summary>Last event sequence this client has folded. Doubles as the resync cursor.</summary>
    public long Sequence { get; private set; }

    public bool CanAct =>
        Status == ConnectionStatus.Connected &&
        !IsSpectator &&
        State.Phase == GamePhase.Aiming &&
        State.ActiveSlot == MySlot;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        Status = ConnectionStatus.Connecting;
        Raise();

        await _hub.StartAsync(ct);

        Status = ConnectionStatus.Connected;
        Raise();
    }

    public async Task<JoinResult> CreateGameAsync(CancellationToken ct = default)
    {
        var result = await _hub.InvokeAsync<JoinResult>(nameof(GameHubMethods.CreateGame), PlayerId, Nickname, ct);
        AcceptJoin(result);
        return result;
    }

    public async Task<JoinResult> CreateSoloGameAsync(AiDifficulty difficulty, CancellationToken ct = default)
    {
        var result = await _hub.InvokeAsync<JoinResult>(
            nameof(GameHubMethods.CreateSoloGame), PlayerId, Nickname, difficulty, ct);

        AcceptJoin(result);
        return result;
    }

    public async Task<JoinResult> JoinGameAsync(string code, bool asObserver = false, CancellationToken ct = default)
    {
        var result = await _hub.InvokeAsync<JoinResult>(
            nameof(GameHubMethods.JoinGame), code, PlayerId, Nickname, asObserver, ct);

        AcceptJoin(result);
        return result;
    }

    public async Task ThrowAsync(double angleDegrees, double velocity)
    {
        LastError = null;

        var ack = await _hub.InvokeAsync<CommandAck>(nameof(GameHubMethods.Throw), angleDegrees, velocity);
        if (!ack.Accepted)
        {
            LastError = ack.Error;
            Raise();
        }
    }

    public async Task NextRoundAsync()
    {
        var ack = await _hub.InvokeAsync<CommandAck>(nameof(GameHubMethods.NextRound));
        if (!ack.Accepted)
        {
            LastError = ack.Error;
            Raise();
        }
    }

    public async Task ForfeitAsync()
    {
        var ack = await _hub.InvokeAsync<CommandAck>(nameof(GameHubMethods.Forfeit));
        if (!ack.Accepted)
        {
            LastError = ack.Error;
            Raise();
        }
    }

    public void CompleteThrow()
    {
        lock (_gate)
        {
            _animating = false;
        }

        Pump();
    }

    private void AcceptJoin(JoinResult result)
    {
        if (!result.Success)
        {
            LastError = result.Error;
            Raise();
            return;
        }

        GameId = result.GameId;
        GameCode = result.GameCode;
        MySlot = result.Slot;

        // The backlog is history, not live play, so it is folded without animating.
        Ingest(result.Backlog, animate: false);
    }

    private async Task ResyncAsync()
    {
        var batch = await _hub.InvokeAsync<EventBatch>(nameof(GameHubMethods.Resync), Sequence);
        Ingest(batch.Events, animate: false);
    }

    /// <param name="animate">
    /// False when catching up (initial join or reconnect): the client fast-forwards silently
    /// rather than replaying every banana that was thrown while it was away.
    /// </param>
    private void Ingest(IReadOnlyList<EventEnvelope> events, bool animate)
    {
        lock (_gate)
        {
            foreach (var envelope in events)
            {
                if (envelope.Sequence <= Sequence)
                {
                    continue;
                }

                _incoming.Enqueue(envelope);
            }

            if (!animate)
            {
                DrainSilently();
            }
        }

        if (animate)
        {
            Pump();
        }
        else
        {
            Raise();
        }
    }

    private void DrainSilently()
    {
        while (_incoming.Count > 0)
        {
            Apply(_incoming.Dequeue());
        }
    }

    /// <summary>Applies queued events, pausing on each throw so the board can animate it.</summary>
    private void Pump()
    {
        ThrowAnimation? animation = null;

        lock (_gate)
        {
            while (!_animating && _incoming.Count > 0)
            {
                var envelope = _incoming.Peek();

                if (envelope.Event is BananaThrown thrown)
                {
                    _incoming.Dequeue();
                    animation = SceneBuilder.BuildThrow(State, thrown.Slot, thrown.AngleDegrees, thrown.Velocity);
                    Apply(envelope);
                    _animating = true;
                    break;
                }

                _incoming.Dequeue();
                Apply(envelope);
            }
        }

        Raise();

        if (animation is not null)
        {
            ThrowStarted?.Invoke(animation);
        }
    }

    private void Apply(EventEnvelope envelope)
    {
        switch (envelope.Event)
        {
            case BananaImpacted { VictimSlot: { } victim }:
                DefeatedSlot = victim;
                break;
            case RoundStarted:
                DefeatedSlot = null;
                break;
        }

        State = State.Apply(envelope.Event);
        Sequence = envelope.Sequence;
    }

    private void OnPresence(PresenceUpdate presence)
    {
        Participants = presence.Participants;
        Raise();
    }

    private void OnServerError(string message)
    {
        LastError = message;
        Raise();
    }

    private void Raise() => Changed?.Invoke();

    public async ValueTask DisposeAsync() => await _hub.DisposeAsync();
}

/// <summary>Method names shared with the hub. Kept in one place so a rename cannot drift.</summary>
internal static class GameHubMethods
{
    public const string CreateGame = nameof(CreateGame);
    public const string CreateSoloGame = nameof(CreateSoloGame);
    public const string JoinGame = nameof(JoinGame);
    public const string Throw = nameof(Throw);
    public const string NextRound = nameof(NextRound);
    public const string Forfeit = nameof(Forfeit);
    public const string Resync = nameof(Resync);
}
