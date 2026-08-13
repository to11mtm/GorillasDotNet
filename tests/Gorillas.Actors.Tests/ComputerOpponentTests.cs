using Akka.Actor;
using Akka.TestKit.Xunit2;
using Gorillas.Contracts;
using Gorillas.Core;
using Gorillas.Core.Ai;
using Gorillas.Core.Events;

namespace Gorillas.Actors.Tests;

public class ComputerOpponentTests : TestKit
{
    private static readonly TempJournal Journal = TempJournal.Create();
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly RecordingPublisher _publisher = new();
    private readonly InMemoryMatchDirectory _directory = new();

    public ComputerOpponentTests()
        : base(Journal.Config)
    {
    }

    private IActorRef NewLobby() =>
        Sys.ActorOf(LobbyActor.PropsFor(_publisher, new NullMatchProjection(), _directory));

    private JoinResult StartSolo(IActorRef lobby, AiDifficulty difficulty = AiDifficulty.Normal)
    {
        lobby.Tell(new LobbyMessages.CreateSoloGame("human", "Ada", difficulty));
        return ExpectMsg<JoinResult>(Timeout);
    }

    private GameState StateOf(IActorRef game)
    {
        game.Tell(new GameMessages.Resync(0));
        return GameState.Replay(ExpectMsg<EventBatch>(Timeout).Events.Select(e => e.Event));
    }

    private IActorRef GameOf(IActorRef lobby, string gameId)
    {
        lobby.Tell(new LobbyMessages.ResolveGame(gameId));
        return ExpectMsg<LobbyMessages.GameRef>(Timeout).Actor!;
    }

    [Fact]
    public void ASoloGameSeatsTheHumanAndAComputer()
    {
        var lobby = NewLobby();
        var result = StartSolo(lobby);

        Assert.True(result.Success);
        Assert.Equal(0, result.Slot);

        var game = GameOf(lobby, result.GameId);

        AwaitAssert(
            () =>
            {
                var state = StateOf(game);
                Assert.Equal(2, state.Players.Count);
                Assert.True(state.PlayerInSlot(1)!.IsComputer);
                Assert.False(state.PlayerInSlot(0)!.IsComputer);
            },
            Timeout);
    }

    [Fact]
    public void TheChosenDifficultyIsRecordedInTheLog()
    {
        var lobby = NewLobby();
        var result = StartSolo(lobby, AiDifficulty.Hard);
        var game = GameOf(lobby, result.GameId);

        AwaitAssert(
            () =>
            {
                game.Tell(new GameMessages.Resync(0));
                var batch = ExpectMsg<EventBatch>(Timeout);

                var joined = batch.Events
                    .Select(e => e.Event)
                    .OfType<PlayerJoined>()
                    .SingleOrDefault(e => e.IsComputer);

                Assert.NotNull(joined);
                Assert.Equal(AiDifficulty.Hard, joined.Difficulty);
            },
            Timeout);
    }

    [Fact]
    public void TheComputerTakesItsTurnWithoutBeingAsked()
    {
        var lobby = NewLobby();
        var result = StartSolo(lobby);
        var game = GameOf(lobby, result.GameId);

        // Wait for the round to start, then play the human's shot.
        AwaitAssert(() => Assert.Equal(GamePhase.Aiming, StateOf(game).Phase), Timeout);

        var state = StateOf(game);
        if (state.ActiveSlot == 0)
        {
            game.Tell(new GameMessages.Throw("human", 5, 6));
            Assert.True(ExpectMsg<CommandAck>(Timeout).Accepted);
        }

        // Nobody tells the computer to move; its own timer must do it.
        AwaitAssert(
            () => Assert.Contains(
                _publisher.AllEvents.Select(e => e.Event).OfType<BananaThrown>(),
                thrown => thrown.Slot == 1),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMilliseconds(250));
    }

    [Fact]
    public void TheComputerKeepsPlayingSoTheMatchProgresses()
    {
        var lobby = NewLobby();
        var result = StartSolo(lobby, AiDifficulty.Hard);
        var game = GameOf(lobby, result.GameId);

        AwaitAssert(() => Assert.Equal(GamePhase.Aiming, StateOf(game).Phase), Timeout);

        // Feed deliberately hopeless human shots; a Hard computer should score meanwhile.
        for (var i = 0; i < 6; i++)
        {
            var state = StateOf(game);
            if (state.Phase == GamePhase.Aiming && state.ActiveSlot == 0)
            {
                game.Tell(new GameMessages.Throw("human", 5, 6));
                ExpectMsg<CommandAck>(Timeout);
            }
            else if (state.Phase == GamePhase.RoundOver)
            {
                break;
            }

            Thread.Sleep(600);
        }

        AwaitAssert(
            () =>
            {
                var state = StateOf(game);
                Assert.True(
                    state.Scores[1] > 0 || state.Skyline.Craters.Count > 0,
                    "The computer never managed to land a shot.");
            },
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public void ObserversCanWatchASoloGame()
    {
        var lobby = NewLobby();
        var result = StartSolo(lobby);

        lobby.Tell(new LobbyMessages.JoinByCode(result.GameCode, "watcher", "Nosy", AsObserver: true));
        var watcher = ExpectMsg<JoinResult>(Timeout);

        Assert.True(watcher.Success);
        Assert.Equal(GameRole.Observer, watcher.Role);
    }
}
