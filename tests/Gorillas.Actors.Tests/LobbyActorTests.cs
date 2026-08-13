using Akka.Actor;
using Akka.TestKit.Xunit2;
using Gorillas.Contracts;
using Gorillas.Core;

namespace Gorillas.Actors.Tests;

public class LobbyActorTests : TestKit
{
    private static readonly TempJournal Journal = TempJournal.Create();
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly RecordingPublisher _publisher = new();
    private readonly InMemoryMatchDirectory _directory = new();

    public LobbyActorTests()
        : base(Journal.Config)
    {
    }

    private IActorRef NewLobby() =>
        Sys.ActorOf(LobbyActor.PropsFor(_publisher, new NullMatchProjection(), _directory));

    private JoinResult Create(IActorRef lobby, string playerId, string nickname)
    {
        lobby.Tell(new LobbyMessages.CreateGame(playerId, nickname));
        return ExpectMsg<JoinResult>(Timeout);
    }

    private JoinResult JoinByCode(IActorRef lobby, string code, string playerId, string nickname, bool asObserver = false)
    {
        lobby.Tell(new LobbyMessages.JoinByCode(code, playerId, nickname, asObserver));
        return ExpectMsg<JoinResult>(Timeout);
    }

    [Fact]
    public void CreatingAGameSeatsTheHostAndIssuesACode()
    {
        var lobby = NewLobby();

        var result = Create(lobby, "p1", "Ada");

        Assert.True(result.Success);
        Assert.Equal(GameRole.Player, result.Role);
        Assert.Equal(0, result.Slot);
        Assert.NotEmpty(result.GameId);
        Assert.Matches("^[A-Z2-9]{3}-[A-Z2-9]{3}$", result.GameCode);
    }

    [Fact]
    public void EachGameGetsADistinctCode()
    {
        var lobby = NewLobby();

        var codes = Enumerable.Range(0, 5)
            .Select(i => Create(lobby, $"host-{i}", $"Host {i}").GameCode)
            .ToList();

        Assert.Equal(codes.Count, codes.Distinct().Count());
    }

    [Fact]
    public void AFriendJoinsWithTheSharedCode()
    {
        var lobby = NewLobby();
        var host = Create(lobby, "p1", "Ada");

        var guest = JoinByCode(lobby, host.GameCode, "p2", "Grace");

        Assert.True(guest.Success);
        Assert.Equal(GameRole.Player, guest.Role);
        Assert.Equal(1, guest.Slot);
        Assert.Equal(host.GameId, guest.GameId);
    }

    [Theory]
    [InlineData("lower")]
    [InlineData("nodash")]
    [InlineData("spaced")]
    public void CodesAreForgivingOfSloppyTyping(string style)
    {
        var lobby = NewLobby();
        var host = Create(lobby, "p1", "Ada");

        var typed = style switch
        {
            "lower" => host.GameCode.ToLowerInvariant(),
            "nodash" => host.GameCode.Replace("-", string.Empty),
            _ => $"  {host.GameCode}  ",
        };

        var guest = JoinByCode(lobby, typed, "p2", "Grace");

        Assert.True(guest.Success);
        Assert.Equal(host.GameId, guest.GameId);
    }

    [Fact]
    public void AnUnknownCodeIsRejected()
    {
        var lobby = NewLobby();

        var result = JoinByCode(lobby, "ZZZ-999", "p2", "Grace");

        Assert.False(result.Success);
        Assert.Contains("No game found", result.Error);
    }

    [Fact]
    public void AnEmptyCodeIsRejected()
    {
        var lobby = NewLobby();

        Assert.False(JoinByCode(lobby, "   ", "p2", "Grace").Success);
    }

    [Fact]
    public void AThirdArrivalBecomesAnObserver()
    {
        var lobby = NewLobby();
        var host = Create(lobby, "p1", "Ada");
        JoinByCode(lobby, host.GameCode, "p2", "Grace");

        var watcher = JoinByCode(lobby, host.GameCode, "p3", "Watcher");

        Assert.True(watcher.Success);
        Assert.Equal(GameRole.Observer, watcher.Role);
        Assert.Null(watcher.Slot);
    }

    [Fact]
    public void SomeoneCanAskToObserveEvenWithASeatFree()
    {
        var lobby = NewLobby();
        var host = Create(lobby, "p1", "Ada");

        var watcher = JoinByCode(lobby, host.GameCode, "p9", "Watcher", asObserver: true);

        Assert.True(watcher.Success);
        Assert.Equal(GameRole.Observer, watcher.Role);
    }

    /// <summary>A shared link must keep working after a server restart drops the in-memory map.</summary>
    [Fact]
    public void AMatchOnlyKnownToTheDirectoryIsRehydrated()
    {
        var lobby = NewLobby();
        var gameId = Guid.NewGuid().ToString("n");
        _directory.Add(gameId, "XKD-472");

        var result = JoinByCode(lobby, "XKD-472", "p1", "Ada");

        Assert.True(result.Success);
        Assert.Equal(gameId, result.GameId);
        Assert.Equal("XKD-472", result.GameCode);
    }

    [Fact]
    public void AmbiguousCharactersAreStrippedRatherThanGuessedAt()
    {
        // O, I, 0 and 1 are excluded from the alphabet, so they cannot appear in a real code.
        Assert.Equal("LD2-34", GameCodes.Normalize("OLD-1234"));
    }

    [Fact]
    public void ResolvingALiveGameReturnsItsActor()
    {
        var lobby = NewLobby();
        var host = Create(lobby, "p1", "Ada");

        lobby.Tell(new LobbyMessages.ResolveGame(host.GameId));
        var reference = ExpectMsg<LobbyMessages.GameRef>(Timeout);

        Assert.NotNull(reference.Actor);
        Assert.Null(reference.Error);
    }

    [Fact]
    public void ResolvingAnUnknownGameReportsAnError()
    {
        var lobby = NewLobby();

        lobby.Tell(new LobbyMessages.ResolveGame("does-not-exist"));
        var reference = ExpectMsg<LobbyMessages.GameRef>(Timeout);

        Assert.Null(reference.Actor);
        Assert.NotNull(reference.Error);
    }

    [Fact]
    public void GeneratedCodesAvoidAmbiguousCharacters()
    {
        var lobby = NewLobby();

        for (var i = 0; i < 10; i++)
        {
            var code = Create(lobby, $"p{i}", "Ada").GameCode;

            Assert.DoesNotContain('O', code);
            Assert.DoesNotContain('I', code);
            Assert.DoesNotContain('0', code);
            Assert.DoesNotContain('1', code);
        }
    }

    [Fact]
    public void NormalizationRestoresTheDashAndDropsNoise()
    {
        Assert.Equal("ABC-234", GameCodes.Normalize("abc234"));
        Assert.Equal("ABC-234", GameCodes.Normalize("  ABC-234 "));
        Assert.Equal(string.Empty, GameCodes.Normalize(null));
        Assert.Equal(string.Empty, GameCodes.Normalize("!!!"));
    }
}
