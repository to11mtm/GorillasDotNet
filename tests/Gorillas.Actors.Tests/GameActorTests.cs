using Akka.Actor;
using Akka.TestKit.Xunit2;
using Gorillas.Contracts;
using Gorillas.Core;
using Gorillas.Core.Events;
using Gorillas.Core.Model;

namespace Gorillas.Actors.Tests;

public class GameActorTests : TestKit
{
    private static readonly TempJournal Journal = TempJournal.Create();

    private readonly RecordingPublisher _publisher = new();

    public GameActorTests()
        : base(Journal.Config)
    {
    }

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private (IActorRef Game, string GameId) NewGame(string? gameId = null)
    {
        var id = gameId ?? Guid.NewGuid().ToString("n");
        var game = Sys.ActorOf(GameActor.PropsFor(id, "BAN-7Q3", GameSettings.Default, _publisher, new NullMatchProjection()));
        return (game, id);
    }

    private JoinResult Join(IActorRef game, string playerId, string nickname, bool asObserver = false)
    {
        game.Tell(new GameMessages.Join(playerId, nickname, asObserver));
        return ExpectMsg<JoinResult>(Timeout);
    }

    private (IActorRef Game, string GameId) StartedGame()
    {
        var (game, id) = NewGame();
        Join(game, "p1", "Ada");
        Join(game, "p2", "Grace");
        return (game, id);
    }

    private GameState StateOf(IActorRef game)
    {
        game.Tell(new GameMessages.Resync(0));
        var batch = ExpectMsg<EventBatch>(Timeout);
        return GameState.Replay(batch.Events.Select(e => e.Event));
    }

    [Fact]
    public void ANewGameJournalsItsCreationEvent()
    {
        var (game, _) = NewGame();

        game.Tell(new GameMessages.Resync(0));
        var batch = ExpectMsg<EventBatch>(Timeout);

        var created = Assert.IsType<GameCreated>(Assert.Single(batch.Events).Event);
        Assert.Equal("BAN-7Q3", created.GameCode);
        Assert.NotEqual(0UL, created.Seed);
    }

    [Fact]
    public void TheFirstTwoPlayersTakeTheTwoSeats()
    {
        var (game, _) = NewGame();

        var first = Join(game, "p1", "Ada");
        var second = Join(game, "p2", "Grace");

        Assert.True(first.Success);
        Assert.Equal(GameRole.Player, first.Role);
        Assert.Equal(0, first.Slot);

        Assert.True(second.Success);
        Assert.Equal(GameRole.Player, second.Role);
        Assert.Equal(1, second.Slot);
    }

    [Fact]
    public void TheSecondJoinStartsTheFirstRound()
    {
        var (game, _) = StartedGame();

        var state = StateOf(game);

        Assert.Equal(GamePhase.Aiming, state.Phase);
        Assert.Equal(1, state.RoundNumber);
        Assert.NotEmpty(state.Skyline.Buildings);
    }

    [Fact]
    public void AThirdPlayerBecomesAnObserver()
    {
        var (game, _) = StartedGame();

        var third = Join(game, "p3", "Watcher");

        Assert.True(third.Success);
        Assert.Equal(GameRole.Observer, third.Role);
        Assert.Null(third.Slot);
    }

    [Fact]
    public void ObserversCannotThrow()
    {
        var (game, _) = StartedGame();
        Join(game, "p3", "Watcher");

        game.Tell(new GameMessages.Throw("p3", 45, 50));
        var ack = ExpectMsg<CommandAck>(Timeout);

        Assert.False(ack.Accepted);
    }

    [Fact]
    public void StrangersCannotThrow()
    {
        var (game, _) = StartedGame();

        game.Tell(new GameMessages.Throw("nobody", 45, 50));
        var ack = ExpectMsg<CommandAck>(Timeout);

        Assert.False(ack.Accepted);
    }

    [Fact]
    public void ThrowingOutOfTurnIsRejected()
    {
        var (game, _) = StartedGame();
        var state = StateOf(game);
        var waiting = state.ActiveSlot == 0 ? "p2" : "p1";

        game.Tell(new GameMessages.Throw(waiting, 45, 50));
        var ack = ExpectMsg<CommandAck>(Timeout);

        Assert.False(ack.Accepted);
    }

    [Fact]
    public void InvalidAimIsRejected()
    {
        var (game, _) = StartedGame();
        var active = StateOf(game).ActiveSlot == 0 ? "p1" : "p2";

        game.Tell(new GameMessages.Throw(active, 400, 50));

        Assert.False(ExpectMsg<CommandAck>(Timeout).Accepted);
    }

    [Fact]
    public void AValidThrowIsAcceptedAndBroadcast()
    {
        var (game, gameId) = StartedGame();
        var active = StateOf(game).ActiveSlot == 0 ? "p1" : "p2";

        game.Tell(new GameMessages.Throw(active, 45, 50));
        Assert.True(ExpectMsg<CommandAck>(Timeout).Accepted);

        AwaitAssert(
            () => Assert.Contains(_publisher.AllEvents, e => e.Event is BananaThrown),
            Timeout);

        Assert.All(_publisher.Batches, batch => Assert.Equal(gameId, batch.GameId));
    }

    [Fact]
    public void BroadcastSequenceNumbersAreGaplessAndOrdered()
    {
        var (game, _) = StartedGame();

        for (var i = 0; i < 3; i++)
        {
            var active = StateOf(game).ActiveSlot == 0 ? "p1" : "p2";
            game.Tell(new GameMessages.Throw(active, 30 + i, 40 + i));
            ExpectMsg<CommandAck>(Timeout);
        }

        AwaitAssert(
            () =>
            {
                var sequences = _publisher.AllEvents.Select(e => e.Sequence).ToList();
                Assert.NotEmpty(sequences);
                Assert.Equal(Enumerable.Range(1, sequences.Count).Select(i => (long)i), sequences);
            },
            Timeout);
    }

    [Fact]
    public void RejoiningWithTheSamePlayerIdReturnsTheSameSeatAndTheWholeLog()
    {
        var (game, _) = StartedGame();
        var active = StateOf(game).ActiveSlot == 0 ? "p1" : "p2";

        game.Tell(new GameMessages.Throw(active, 45, 50));
        ExpectMsg<CommandAck>(Timeout);

        var rejoin = Join(game, "p1", "Ada");

        Assert.True(rejoin.Success);
        Assert.Equal(GameRole.Player, rejoin.Role);
        Assert.Equal(0, rejoin.Slot);
        Assert.Equal(rejoin.Sequence, rejoin.Backlog.Count);
        Assert.Contains(rejoin.Backlog, e => e.Event is BananaThrown);
    }

    [Fact]
    public void ResyncReturnsOnlyTheEventsAfterTheCursor()
    {
        var (game, _) = StartedGame();
        var before = StateOf(game);
        var cursor = 3L;

        var active = before.ActiveSlot == 0 ? "p1" : "p2";
        game.Tell(new GameMessages.Throw(active, 45, 50));
        ExpectMsg<CommandAck>(Timeout);

        game.Tell(new GameMessages.Resync(cursor));
        var tail = ExpectMsg<EventBatch>(Timeout);

        Assert.All(tail.Events, e => Assert.True(e.Sequence > cursor));
        Assert.Equal(cursor + 1, tail.Events[0].Sequence);
    }

    /// <summary>A client that missed messages must fold the delta onto its stale head and match.</summary>
    [Fact]
    public void CatchingUpFromAnyCursorReconstructsTheAuthoritativeState()
    {
        var (game, _) = StartedGame();

        for (var i = 0; i < 3; i++)
        {
            var active = StateOf(game).ActiveSlot == 0 ? "p1" : "p2";
            game.Tell(new GameMessages.Throw(active, 25 + (i * 5), 35 + (i * 5)));
            ExpectMsg<CommandAck>(Timeout);
        }

        game.Tell(new GameMessages.Resync(0));
        var full = ExpectMsg<EventBatch>(Timeout);
        var authoritative = GameState.Replay(full.Events.Select(e => e.Event));

        for (var cursor = 0; cursor <= full.Events.Count; cursor++)
        {
            var head = GameState.Replay(full.Events.Take(cursor).Select(e => e.Event));

            game.Tell(new GameMessages.Resync(cursor));
            var delta = ExpectMsg<EventBatch>(Timeout);

            var caughtUp = delta.Events.Aggregate(head, (state, envelope) => state.Apply(envelope.Event));

            Assert.Equal(authoritative, caughtUp);
        }
    }

    [Fact]
    public void AMatchIsRecoveredFromTheJournalAfterTheActorDies()
    {
        var gameId = Guid.NewGuid().ToString("n");
        var (game, _) = NewGame(gameId);

        Join(game, "p1", "Ada");
        Join(game, "p2", "Grace");
        var active = StateOf(game).ActiveSlot == 0 ? "p1" : "p2";
        game.Tell(new GameMessages.Throw(active, 40, 55));
        ExpectMsg<CommandAck>(Timeout);

        var expected = StateOf(game);

        Watch(game);
        game.Tell(PoisonPill.Instance);
        ExpectTerminated(game, Timeout);

        var (revived, _) = NewGame(gameId);

        Assert.Equal(expected, StateOf(revived));
    }

    [Fact]
    public void ARecoveredMatchLetsTheOriginalPlayersReclaimTheirSeats()
    {
        var gameId = Guid.NewGuid().ToString("n");
        var (game, _) = NewGame(gameId);
        Join(game, "p1", "Ada");
        Join(game, "p2", "Grace");

        Watch(game);
        game.Tell(PoisonPill.Instance);
        ExpectTerminated(game, Timeout);

        var (revived, _) = NewGame(gameId);
        var rejoin = Join(revived, "p1", "Ada");

        Assert.True(rejoin.Success);
        Assert.Equal(GameRole.Player, rejoin.Role);
        Assert.Equal(0, rejoin.Slot);
    }

    [Fact]
    public void ForfeitingEndsTheMatch()
    {
        var (game, _) = StartedGame();

        game.Tell(new GameMessages.Forfeit("p2"));
        Assert.True(ExpectMsg<CommandAck>(Timeout).Accepted);

        var state = StateOf(game);
        Assert.Equal(GamePhase.MatchOver, state.Phase);
        Assert.Equal(0, state.MatchWinnerSlot);
    }

    [Fact]
    public void PresenceIsPublishedWhenPlayersJoinAndDisconnect()
    {
        var (game, _) = StartedGame();

        game.Tell(new GameMessages.Disconnected("p1"));

        AwaitAssert(
            () =>
            {
                var latest = _publisher.Presence.LastOrDefault();
                Assert.NotNull(latest);
                Assert.Contains(latest.Participants, p => p.PlayerId == "p1" && !p.Connected);
            },
            Timeout);
    }

    [Fact]
    public void ASnapshotReportsTheCurrentSequence()
    {
        var (game, gameId) = StartedGame();

        game.Tell(new GameMessages.GetSnapshot());
        var snapshot = ExpectMsg<GameMessages.SnapshotReply>(Timeout);

        Assert.Equal(gameId, snapshot.GameId);
        Assert.Equal("BAN-7Q3", snapshot.GameCode);
        Assert.Equal(2, snapshot.PlayerCount);
        Assert.True(snapshot.Sequence > 0);
    }
}
