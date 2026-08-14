using Gorillas.Client.Game;
using Gorillas.Client.Rendering;
using Gorillas.Contracts;
using Gorillas.Core;
using Gorillas.Core.Events;

namespace Gorillas.Client.Tests;

public class ReplaySessionTests
{
    private static readonly IReadOnlyList<EventEnvelope> Log = MatchLogFactory.Build();

    private static GameState FoldFirst(int count) =>
        GameState.Replay(Log.Take(count).Select(e => e.Event));

    [Fact]
    public void ANewReplayStartsAtTheBeginning()
    {
        var session = new ReplaySession(Log);

        Assert.Equal(0, session.Cursor);
        Assert.Equal(Log.Count, session.Length);
        Assert.False(session.IsPlaying);
        Assert.False(session.AtEnd);
        Assert.Equal(GameState.Initial, session.State);
    }

    [Fact]
    public void AReplayIsAlwaysWatchedNeverPlayed()
    {
        var session = new ReplaySession(Log);

        Assert.True(session.IsSpectator);
        Assert.False(session.CanAct);
        Assert.Null(session.MySlot);
    }

    /// <summary>The whole point of the deterministic fold: seeking must be exact, not approximate.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(8)]
    public void SeekingMatchesFoldingTheFirstNEvents(int cursor)
    {
        var session = new ReplaySession(Log);

        session.SeekTo(cursor);

        Assert.Equal(cursor, session.Cursor);
        Assert.Equal(FoldFirst(cursor), session.State);
    }

    [Fact]
    public void SeekingToEveryPositionIsExact()
    {
        var session = new ReplaySession(Log);

        for (var cursor = 0; cursor <= Log.Count; cursor++)
        {
            session.SeekTo(cursor);
            Assert.Equal(FoldFirst(cursor), session.State);
        }
    }

    [Fact]
    public void ScrubbingBackAndForthLandsOnTheSameState()
    {
        var session = new ReplaySession(Log);
        var target = Log.Count / 2;

        session.SeekTo(target);
        var expected = session.State;

        session.SeekTo(Log.Count);
        session.SeekTo(0);
        session.SeekTo(target);

        Assert.Equal(expected, session.State);
    }

    [Fact]
    public void SeekingIsClampedToTheLog()
    {
        var session = new ReplaySession(Log);

        session.SeekTo(-50);
        Assert.Equal(0, session.Cursor);

        session.SeekTo(Log.Count + 50);
        Assert.Equal(Log.Count, session.Cursor);
        Assert.True(session.AtEnd);
    }

    [Fact]
    public void SeekingToTheEndReproducesTheFinalState()
    {
        var session = new ReplaySession(Log);

        session.SeekToEnd();

        Assert.Equal(GameState.Replay(Log.Select(e => e.Event)), session.State);
    }

    [Fact]
    public void RestartReturnsToTheOpeningState()
    {
        var session = new ReplaySession(Log);
        session.SeekToEnd();

        session.Restart();

        Assert.Equal(0, session.Cursor);
        Assert.Equal(GameState.Initial, session.State);
    }

    [Fact]
    public void SteppingForwardMakesProgress()
    {
        var session = new ReplaySession(Log);

        session.StepForward();

        Assert.True(session.Cursor > 0);
        Assert.Equal(FoldFirst(session.Cursor), session.State);
    }

    [Fact]
    public void SteppingBackUndoesExactlyOneEvent()
    {
        var session = new ReplaySession(Log);
        session.SeekTo(5);

        session.StepBack();

        Assert.Equal(4, session.Cursor);
        Assert.Equal(FoldFirst(4), session.State);
    }

    [Fact]
    public void SteppingBackAtTheStartStaysAtTheStart()
    {
        var session = new ReplaySession(Log);

        session.StepBack();

        Assert.Equal(0, session.Cursor);
    }

    [Fact]
    public void SteppingThroughTheWholeLogEndsOnTheFinalState()
    {
        var session = new ReplaySession(Log);
        var guard = 0;

        while (!session.AtEnd && guard++ < 500)
        {
            session.StepForward();
            session.CompleteThrow();
        }

        Assert.True(session.AtEnd);
        Assert.Equal(GameState.Replay(Log.Select(e => e.Event)), session.State);
    }

    [Fact]
    public void ReachingAThrowRaisesAnAnimation()
    {
        var session = new ReplaySession(Log);
        ThrowAnimation? animation = null;
        session.ThrowStarted += a => animation = a;

        var guard = 0;
        while (animation is null && !session.AtEnd && guard++ < 100)
        {
            session.StepForward();
            session.CompleteThrow();
        }

        Assert.NotNull(animation);
        Assert.NotEmpty(animation.Points);
    }

    [Fact]
    public void TheAnimatedThrowMatchesTheRecordedThrow()
    {
        var session = new ReplaySession(Log);
        ThrowAnimation? animation = null;
        session.ThrowStarted += a => animation = a;

        var firstThrowIndex = Log.ToList().FindIndex(e => e.Event is BananaThrown);
        var recorded = (BananaThrown)Log[firstThrowIndex].Event;

        var guard = 0;
        while (animation is null && !session.AtEnd && guard++ < 100)
        {
            session.StepForward();
            session.CompleteThrow();
        }

        Assert.NotNull(animation);
        Assert.Equal(recorded.Slot, animation.Slot);
    }

    [Fact]
    public void ShotCountsReflectProgressThroughTheMatch()
    {
        var session = new ReplaySession(Log);
        var totalThrows = Log.Count(e => e.Event is BananaThrown);

        Assert.Equal(totalThrows, session.ShotCount);
        Assert.Equal(0, session.ShotsPlayed);

        session.SeekToEnd();
        Assert.Equal(totalThrows, session.ShotsPlayed);
    }

    [Fact]
    public void JumpingToTheNextShotLandsOnAThrow()
    {
        var session = new ReplaySession(Log);

        session.SeekToShot(1);

        Assert.Equal(FoldFirst(session.Cursor), session.State);
        Assert.True(session.Cursor < Log.Count);
        Assert.IsType<BananaThrown>(Log[session.Cursor].Event);
    }

    /// <summary>Repeated presses used to stay pinned to the first shot.</summary>
    [Fact]
    public void RepeatedNextShotJumpsVisitEveryShotInOrder()
    {
        var session = new ReplaySession(Log);
        var visited = new List<int>();

        for (var i = 0; i < session.ShotCount; i++)
        {
            session.SeekToShot(1);
            visited.Add(session.Cursor);
        }

        Assert.Equal(session.ShotCount, visited.Distinct().Count());
        Assert.Equal(visited.OrderBy(c => c), visited);
        Assert.All(visited, cursor => Assert.IsType<BananaThrown>(Log[cursor].Event));
    }

    [Fact]
    public void NextShotPastTheLastOneRunsToTheEnd()
    {
        var session = new ReplaySession(Log);

        for (var i = 0; i < session.ShotCount + 2; i++)
        {
            session.SeekToShot(1);
        }

        Assert.True(session.AtEnd);
    }

    [Fact]
    public void PreviousShotWalksBackThroughTheShots()
    {
        var session = new ReplaySession(Log);
        session.SeekToEnd();

        session.SeekToShot(-1);
        var last = session.Cursor;
        Assert.IsType<BananaThrown>(Log[last].Event);

        session.SeekToShot(-1);
        Assert.True(session.Cursor < last);
    }

    [Fact]
    public void PreviousShotFromTheStartStaysAtTheStart()
    {
        var session = new ReplaySession(Log);

        session.SeekToShot(-1);

        Assert.Equal(0, session.Cursor);
    }

    [Fact]
    public void SeekingCancelsPlayback()
    {
        var session = new ReplaySession(Log);

        session.SeekTo(2);

        Assert.False(session.IsPlaying);
    }

    [Fact]
    public void DefeatedGorillaIsShownThenClearedOnTheNextRound()
    {
        var log = MatchLogFactory.Build(seed: 20250813, throws: 40);
        var knockout = log.ToList().FindIndex(e => e.Event is BananaImpacted { VictimSlot: not null });

        if (knockout < 0)
        {
            return; // This seed produced no knockout; the other tests still cover the fold.
        }

        var session = new ReplaySession(log);

        session.SeekTo(knockout + 1);
        Assert.NotNull(session.DefeatedSlot);

        var nextRound = log.ToList().FindIndex(knockout, e => e.Event is RoundStarted);
        if (nextRound > 0)
        {
            session.SeekTo(nextRound + 1);
            Assert.Null(session.DefeatedSlot);
        }
    }

    [Fact]
    public void AnEmptyLogIsHandledGracefully()
    {
        var session = new ReplaySession([]);

        Assert.Equal(0, session.Length);
        Assert.True(session.AtEnd);
        Assert.Equal(0, session.ShotCount);

        session.StepForward();
        session.SeekToShot(1);

        Assert.Equal(0, session.Cursor);
    }
}
