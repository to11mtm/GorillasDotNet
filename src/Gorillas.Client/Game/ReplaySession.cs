using Gorillas.Client.Rendering;
using Gorillas.Contracts;
using Gorillas.Core;
using Gorillas.Core.Events;
using Gorillas.Core.Model;

namespace Gorillas.Client.Game;

/// <summary>
/// Plays a finished match back from its event log. Because state is a pure fold, seeking is
/// simply "replay the first N events", which makes scrubbing exact rather than approximate.
/// </summary>
public sealed class ReplaySession : IGameSession
{
    private readonly IReadOnlyList<EventEnvelope> _log;
    private readonly List<int> _throwIndices = [];

    private bool _animating;
    private CancellationTokenSource? _playback;

    public ReplaySession(IReadOnlyList<EventEnvelope> log)
    {
        _log = log;

        for (var i = 0; i < log.Count; i++)
        {
            if (log[i].Event is BananaThrown)
            {
                _throwIndices.Add(i);
            }
        }

        Rebuild(0);
    }

    public event Action? Changed;

    public event Action<ThrowAnimation>? ThrowStarted;

    public GameState State { get; private set; } = GameState.Initial;

    public GameSettings Settings => State.Settings;

    public string? GameCode => State.GameCode;

    /// <summary>A replay is always watched, never played.</summary>
    public int? MySlot => null;

    public bool IsSpectator => true;

    public bool CanAct => false;

    public int? DefeatedSlot { get; private set; }

    public string? LastError => null;

    /// <summary>How many events have been applied. Also the scrub position.</summary>
    public int Cursor { get; private set; }

    public int Length => _log.Count;

    public bool IsPlaying { get; private set; }

    public double Speed { get; set; } = 1;

    public bool AtEnd => Cursor >= _log.Count;

    public int ShotCount => _throwIndices.Count;

    public int ShotsPlayed => _throwIndices.Count(index => index < Cursor);

    /// <summary>Pause between a landing and the next shot, so a replay has the rhythm of a real game.</summary>
    private TimeSpan Beat => TimeSpan.FromMilliseconds(700 / Math.Max(Speed, 0.1));

    public Task PlayAsync()
    {
        if (IsPlaying || AtEnd)
        {
            return Task.CompletedTask;
        }

        IsPlaying = true;
        _playback = new CancellationTokenSource();
        Changed?.Invoke();

        return RunAsync(_playback.Token);
    }

    public void Pause()
    {
        IsPlaying = false;
        _playback?.Cancel();
        _playback = null;
        Changed?.Invoke();
    }

    public void StepForward()
    {
        Pause();

        if (AtEnd)
        {
            return;
        }

        AdvanceOne();
    }

    public void StepBack() => SeekTo(Cursor - 1);

    public void Restart() => SeekTo(0);

    public void SeekToEnd() => SeekTo(_log.Count);

    /// <summary>
    /// Jump to the next or previous shot — the interesting moments in a match. Anchored on the
    /// cursor rather than a count of shots played, because sitting exactly on a shot's first
    /// event means it has not been counted as played yet, which used to pin this to shot one.
    /// </summary>
    public void SeekToShot(int delta)
    {
        if (_throwIndices.Count == 0)
        {
            return;
        }

        if (delta > 0)
        {
            var next = _throwIndices.Where(index => index > Cursor).ToList();

            if (next.Count == 0)
            {
                SeekToEnd();
                return;
            }

            SeekTo(next[0]);
            return;
        }

        var previous = _throwIndices.Where(index => index < Cursor).ToList();
        SeekTo(previous.Count == 0 ? 0 : previous[^1]);
    }

    public void SeekTo(int cursor)
    {
        Pause();
        Rebuild(Math.Clamp(cursor, 0, _log.Count));
        Changed?.Invoke();
    }

    public void CompleteThrow()
    {
        _animating = false;
        Changed?.Invoke();
    }

    // A replay is read-only; the board never surfaces these because CanAct is false.
    public Task ThrowAsync(double angleDegrees, double velocity) => Task.CompletedTask;

    public Task NextRoundAsync() => Task.CompletedTask;

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && !AtEnd)
            {
                AdvanceOne();

                // Hold while the board animates a banana, so playback matches what is on screen.
                while (_animating && !ct.IsCancellationRequested)
                {
                    await Task.Delay(50, ct);
                }

                await Task.Delay(Beat, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Paused or seeked; nothing to do.
        }

        if (!ct.IsCancellationRequested)
        {
            IsPlaying = false;
            Changed?.Invoke();
        }
    }

    /// <summary>Applies events up to the next shot, then animates that shot.</summary>
    private void AdvanceOne()
    {
        ThrowAnimation? animation = null;

        while (Cursor < _log.Count)
        {
            var envelope = _log[Cursor];

            if (envelope.Event is BananaThrown thrown)
            {
                animation = SceneBuilder.BuildThrow(State, thrown.Slot, thrown.AngleDegrees, thrown.Velocity);
                Apply(envelope);
                _animating = true;
                break;
            }

            Apply(envelope);

            // Stop on a round or match boundary so those moments are visible.
            if (envelope.Event is RoundEnded or MatchEnded or RoundStarted)
            {
                break;
            }
        }

        Changed?.Invoke();

        if (animation is not null)
        {
            ThrowStarted?.Invoke(animation);
        }
    }

    private void Rebuild(int cursor)
    {
        State = GameState.Initial;
        DefeatedSlot = null;
        _animating = false;
        Cursor = 0;

        for (var i = 0; i < cursor; i++)
        {
            Apply(_log[i]);
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
        Cursor++;
    }
}
