using Gorillas.Client.Rendering;
using Gorillas.Core;
using Gorillas.Core.Commands;
using Gorillas.Core.Events;
using Gorillas.Core.Model;
using Gorillas.Core.Primitives;

namespace Gorillas.Client.Game;

/// <summary>
/// Drives a hot-seat match entirely in this browser session. Uses the same decide-then-apply
/// split as the networked session, so the board behaves identically in both modes.
/// </summary>
public sealed class LocalGameSession : IGameSession
{
    private readonly List<GameEvent> _log = [];
    private readonly Queue<GameEvent> _deferred = new();
    private readonly DeterministicRandom _random;

    public LocalGameSession(GameSettings? settings = null, ulong? seed = null)
    {
        Settings = settings ?? GameSettings.Default;
        _random = new DeterministicRandom(seed ?? (ulong)Random.Shared.NextInt64(1, long.MaxValue));
    }

    public event Action? Changed;

    public event Action<ThrowAnimation>? ThrowStarted;

    public GameSettings Settings { get; }

    public GameState State { get; private set; } = GameState.Initial;

    public string? GameCode => State.GameCode;

    /// <summary>In hot seat the local player is whoever's turn it is.</summary>
    public int? MySlot => State.ActiveSlot;

    public bool IsSpectator => false;

    public IReadOnlyList<GameEvent> Log => _log;

    public string? LastError { get; private set; }

    public int? DefeatedSlot { get; private set; }

    public bool CanAct => State.Phase == GamePhase.Aiming;

    public void Start(string playerOne, string playerTwo)
    {
        _log.Clear();
        _deferred.Clear();
        DefeatedSlot = null;
        LastError = null;
        State = GameState.Initial;

        Execute(new CreateGame(Guid.NewGuid().ToString("n"), GameCodes.Generate(_random), Settings));
        Execute(new JoinGame("local-1", playerOne));
        Execute(new JoinGame("local-2", playerTwo));
        Changed?.Invoke();
    }

    public Task ThrowAsync(double angleDegrees, double velocity)
    {
        LastError = null;

        var decision = GameEngine.Decide(State, new ThrowBanana(State.ActiveSlot, angleDegrees, velocity), _random);
        if (!decision.IsAccepted)
        {
            LastError = decision.Error;
            Changed?.Invoke();
            return Task.CompletedTask;
        }

        var animation = SceneBuilder.BuildThrow(State, State.ActiveSlot, angleDegrees, velocity);

        foreach (var @event in decision.Events)
        {
            _log.Add(@event);

            if (@event is BananaThrown)
            {
                State = State.Apply(@event);
            }
            else
            {
                _deferred.Enqueue(@event);
            }
        }

        Changed?.Invoke();
        ThrowStarted?.Invoke(animation);
        return Task.CompletedTask;
    }

    public void CompleteThrow()
    {
        while (_deferred.Count > 0)
        {
            var @event = _deferred.Dequeue();
            if (@event is BananaImpacted { VictimSlot: { } victim })
            {
                DefeatedSlot = victim;
            }

            State = State.Apply(@event);
        }

        Changed?.Invoke();
    }

    public Task NextRoundAsync()
    {
        DefeatedSlot = null;
        Execute(new Core.Commands.StartNextRound());
        Changed?.Invoke();
        return Task.CompletedTask;
    }

    private void Execute(GameCommand command)
    {
        var decision = GameEngine.Decide(State, command, _random);
        if (!decision.IsAccepted)
        {
            LastError = decision.Error;
            return;
        }

        foreach (var @event in decision.Events)
        {
            _log.Add(@event);
            State = State.Apply(@event);
        }
    }
}
