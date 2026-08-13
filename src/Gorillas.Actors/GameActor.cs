using Akka.Actor;
using Akka.Event;
using Akka.Persistence;
using Gorillas.Contracts;
using Gorillas.Core;
using Gorillas.Core.Ai;
using Gorillas.Core.Commands;
using Gorillas.Core.Events;
using Gorillas.Core.Model;
using Gorillas.Core.Primitives;

namespace Gorillas.Actors;

/// <summary>
/// Sole authority over one match. Clients send intents; this actor decides, journals the
/// resulting events, then broadcasts them. Its in-memory log is the full match history, which
/// serves reconnect catch-up, late-joining spectators and post-match replay from one place.
/// </summary>
/// <remarks>
/// Snapshots are deliberately not used. A whole match is on the order of a hundred tiny
/// events, so recovery is already fast, and skipping them avoids serializing the entire
/// <see cref="GameState"/> graph. Revisit if matches ever grow unbounded.
/// </remarks>
public sealed class GameActor : ReceivePersistentActor, IWithTimers
{
    private readonly string _gameId;
    private readonly string _gameCode;
    private readonly GameSettings _settings;
    private readonly IGameEventPublisher _publisher;
    private readonly IMatchProjection _projection;
    private readonly ILoggingAdapter _log = Context.GetLogger();

    private readonly List<GameEvent> _events = [];
    private readonly Dictionary<string, ParticipantInfo> _participants = [];
    private readonly Dictionary<int, GorillaAi> _computerPlayers = [];
    private readonly IRandomSource _random;

    private static readonly TimeSpan ThinkingDelay = TimeSpan.FromMilliseconds(1400);

    private const string AiTimerKey = "ai-turn";

    private GameState _state = GameState.Initial;
    private Task _outbound = Task.CompletedTask;

    public GameActor(
        string gameId,
        string gameCode,
        GameSettings settings,
        IGameEventPublisher publisher,
        IMatchProjection projection,
        ulong? randomSeed = null)
    {
        _gameId = gameId;
        _gameCode = gameCode;
        _settings = settings;
        _publisher = publisher;
        _projection = projection;
        _random = new DeterministicRandom(randomSeed ?? (ulong)Random.Shared.NextInt64(1, long.MaxValue));

        PersistenceId = PersistenceIdFor(gameId);

        Recover<GameEvent>(ApplyRecovered);
        Recover<RecoveryCompleted>(_ => OnRecoveryCompleted());

        Command<GameMessages.Join>(OnJoin);
        Command<GameMessages.AiTurn>(OnAiTurn);
        Command<GameMessages.Throw>(OnThrow);
        Command<GameMessages.StartNextRound>(OnStartNextRound);
        Command<GameMessages.Forfeit>(OnForfeit);
        Command<GameMessages.Resync>(OnResync);
        Command<GameMessages.Disconnected>(OnDisconnected);
        Command<GameMessages.GetSnapshot>(_ =>
            Sender.Tell(new GameMessages.SnapshotReply(_gameId, _gameCode, Sequence, _state.Players.Count)));
    }

    public override string PersistenceId { get; }

    public static string PersistenceIdFor(string gameId) => $"game-{gameId}";

    public static Props PropsFor(
        string gameId,
        string gameCode,
        GameSettings settings,
        IGameEventPublisher publisher,
        IMatchProjection projection) =>
        Props.Create(() => new GameActor(gameId, gameCode, settings, publisher, projection, null));

    private long Sequence => _events.Count;

    private void ApplyRecovered(GameEvent @event)
    {
        _events.Add(@event);
        _state = _state.Apply(@event);
    }

    private void OnRecoveryCompleted()
    {
        // A brand-new match has nothing journalled yet, so write its opening event.
        if (_events.Count == 0)
        {
            Emit(GameEngine.Decide(_state, new CreateGame(_gameId, _gameCode, _settings), _random));
            return;
        }

        RebuildParticipants();
        MaybeScheduleAiTurn();
        _log.Info("Recovered match {0} at sequence {1}.", _gameId, Sequence);
    }

    private void RebuildParticipants()
    {
        foreach (var player in _state.Players)
        {
            _participants[player.PlayerId] = new ParticipantInfo(
                player.PlayerId, player.Nickname, player.Slot, GameRole.Player, Connected: false);
        }
    }

    private void OnJoin(GameMessages.Join request)
    {
        var replyTo = Sender;

        // A known player id is a reconnect: hand back the same seat plus the whole log.
        if (_participants.TryGetValue(request.PlayerId, out var existing))
        {
            _participants[request.PlayerId] = existing with { Connected = true };
            replyTo.Tell(Accepted(existing.Role, existing.Slot));
            PublishPresence();
            return;
        }

        if (request.AsObserver || _state.IsFull)
        {
            _participants[request.PlayerId] = new ParticipantInfo(
                request.PlayerId, request.Nickname, null, GameRole.Observer, Connected: true);

            replyTo.Tell(Accepted(GameRole.Observer, null));
            PublishPresence();
            return;
        }

        var decision = GameEngine.Decide(
            _state,
            new JoinGame(request.PlayerId, request.Nickname, request.IsComputer, request.Difficulty),
            _random);

        if (!decision.IsAccepted)
        {
            replyTo.Tell(JoinResult.Failed(decision.Error!));
            return;
        }

        Emit(decision, () =>
        {
            var slot = _state.PlayerInSlot(0)?.PlayerId == request.PlayerId ? 0 : 1;
            _participants[request.PlayerId] = new ParticipantInfo(
                request.PlayerId, request.Nickname, slot, GameRole.Player, Connected: true);

            replyTo.Tell(Accepted(GameRole.Player, slot));
            PublishPresence();
        });
    }

    private void OnAiTurn(GameMessages.AiTurn request)
    {
        // The scheduled shot is stale if anything else has happened since it was queued.
        if (request.AtSequence != Sequence || _state.Phase != GamePhase.Aiming)
        {
            return;
        }

        var slot = _state.ActiveSlot;
        if (_state.PlayerInSlot(slot) is not { IsComputer: true })
        {
            return;
        }

        var ai = AiFor(slot);
        var shot = ai?.ChooseShot(_state, slot);

        if (shot is null)
        {
            _log.Warning("Computer gorilla in match {0} could not find a shot.", _gameId);
            return;
        }

        var decision = GameEngine.Decide(_state, new ThrowBanana(slot, shot.AngleDegrees, shot.Velocity), _random);
        if (decision.IsAccepted)
        {
            Emit(decision);
        }
        else
        {
            _log.Warning("Computer shot rejected in match {0}: {1}", _gameId, decision.Error);
        }
    }

    private GorillaAi? AiFor(int slot)
    {
        if (_state.PlayerInSlot(slot) is not { IsComputer: true })
        {
            return null;
        }

        if (!_computerPlayers.TryGetValue(slot, out var ai))
        {
            // Difficulty lives in the journal, so a recovered match keeps the same opponent.
            var difficulty = _events.OfType<PlayerJoined>()
                .FirstOrDefault(e => e.Slot == slot)?.Difficulty ?? AiDifficulty.Normal;

            ai = new GorillaAi(difficulty, _random);
            _computerPlayers[slot] = ai;
        }

        return ai;
    }

    /// <summary>
    /// Gives the computer a beat to "think" so its shot does not appear instantly, and so the
    /// human's own animation has finished before the reply lands. A single timer keyed by name
    /// means a re-schedule replaces the previous one, and Akka cancels it when the actor stops.
    /// </summary>
    private void MaybeScheduleAiTurn()
    {
        if (_state.Phase != GamePhase.Aiming)
        {
            return;
        }

        if (_state.PlayerInSlot(_state.ActiveSlot) is not { IsComputer: true })
        {
            return;
        }

        Timers.StartSingleTimer(AiTimerKey, new GameMessages.AiTurn(Sequence), ThinkingDelay);
    }

    public ITimerScheduler Timers { get; set; } = null!;

    private void OnThrow(GameMessages.Throw request)
    {
        if (!TryResolveSlot(request.PlayerId, out var slot, out var error))
        {
            Sender.Tell(CommandAck.Fail(error));
            return;
        }

        Execute(new ThrowBanana(slot, request.AngleDegrees, request.Velocity));
    }

    private void OnStartNextRound(GameMessages.StartNextRound request)
    {
        if (!TryResolveSlot(request.PlayerId, out _, out var error))
        {
            Sender.Tell(CommandAck.Fail(error));
            return;
        }

        Execute(new Core.Commands.StartNextRound());
    }

    private void OnForfeit(GameMessages.Forfeit request)
    {
        if (!TryResolveSlot(request.PlayerId, out var slot, out var error))
        {
            Sender.Tell(CommandAck.Fail(error));
            return;
        }

        Execute(new Core.Commands.Forfeit(slot));
    }

    private void Execute(GameCommand command)
    {
        var replyTo = Sender;
        var decision = GameEngine.Decide(_state, command, _random);

        if (!decision.IsAccepted)
        {
            replyTo.Tell(CommandAck.Fail(decision.Error!));
            return;
        }

        Emit(decision, () => replyTo.Tell(CommandAck.Ok));
    }

    private void OnResync(GameMessages.Resync request) => Sender.Tell(BatchFrom(request.AfterSequence));

    private void OnDisconnected(GameMessages.Disconnected request)
    {
        if (_participants.TryGetValue(request.PlayerId, out var participant))
        {
            _participants[request.PlayerId] = participant with { Connected = false };
            PublishPresence();
        }
    }

    private bool TryResolveSlot(string playerId, out int slot, out string error)
    {
        slot = -1;

        if (!_participants.TryGetValue(playerId, out var participant))
        {
            error = "You are not part of this game.";
            return false;
        }

        if (participant.Role != GameRole.Player || participant.Slot is not { } resolved)
        {
            error = "Observers cannot act in the game.";
            return false;
        }

        slot = resolved;
        error = string.Empty;
        return true;
    }

    private JoinResult Accepted(GameRole role, int? slot) =>
        new(true, null, _gameId, _gameCode, role, slot, Sequence, BatchFrom(0).Events);

    private EventBatch BatchFrom(long afterSequence)
    {
        var envelopes = new List<EventEnvelope>();
        for (var i = (int)Math.Max(afterSequence, 0); i < _events.Count; i++)
        {
            envelopes.Add(new EventEnvelope(i + 1, _events[i]));
        }

        return new EventBatch(_gameId, envelopes);
    }

    /// <summary>Journals the decision, then broadcasts it once every event is durable.</summary>
    private void Emit(Decision decision, Action? afterPersist = null)
    {
        if (decision.Events.Count == 0)
        {
            afterPersist?.Invoke();
            return;
        }

        var firstSequence = Sequence;
        var remaining = decision.Events.Count;

        PersistAll(decision.Events, @event =>
        {
            _events.Add(@event);
            _state = _state.Apply(@event);

            if (--remaining > 0)
            {
                return;
            }

            var batch = BatchFrom(firstSequence);
            Enqueue(() => _publisher.PublishEventsAsync(batch));
            Enqueue(() => _projection.RecordEventsAsync(_gameId, firstSequence, decision.Events));
            afterPersist?.Invoke();
            MaybeScheduleAiTurn();
        });
    }

    private void PublishPresence()
    {
        var presence = new PresenceUpdate(_gameId, [.. _participants.Values]);
        Enqueue(() => _publisher.PublishPresenceAsync(presence));
    }

    /// <summary>
    /// Chains outbound work so batches reach clients in journal order. Failures are logged
    /// rather than thrown — a broken subscriber must never stall the match.
    /// </summary>
    private void Enqueue(Func<Task> work)
    {
        var self = Self;
        _outbound = _outbound.ContinueWith(
            async _ =>
            {
                try
                {
                    await work();
                }
                catch (Exception ex)
                {
                    Context.System.Log.Warning(ex, "Outbound work failed for match {0} ({1}).", self.Path.Name, ex.Message);
                }
            },
            TaskScheduler.Default).Unwrap();
    }

    protected override void OnPersistFailure(Exception cause, object @event, long sequenceNr)
    {
        _log.Error(cause, "Failed to journal {0} for match {1}.", @event.GetType().Name, _gameId);
        base.OnPersistFailure(cause, @event, sequenceNr);
    }
}
