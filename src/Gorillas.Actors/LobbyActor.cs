using Akka.Actor;
using Akka.Event;
using Gorillas.Contracts;
using Gorillas.Core;
using Gorillas.Core.Ai;
using Gorillas.Core.Model;
using Gorillas.Core.Primitives;

namespace Gorillas.Actors;

/// <summary>
/// Owns the set of live matches and maps human-friendly game codes onto them. Matches it has
/// never seen are rehydrated on demand from the directory, so a server restart does not
/// invalidate a shared code.
/// </summary>
public sealed class LobbyActor : ReceiveActor
{
    private const int CodeAttempts = 8;

    private readonly IGameEventPublisher _publisher;
    private readonly IMatchProjection _projection;
    private readonly IMatchDirectory _directory;
    private readonly GameSettings _defaultSettings;
    private readonly ILoggingAdapter _log = Context.GetLogger();

    private readonly Dictionary<string, IActorRef> _games = [];
    private readonly Dictionary<string, string> _codes = new(StringComparer.OrdinalIgnoreCase);
    private readonly DeterministicRandom _random = new((ulong)Random.Shared.NextInt64(1, long.MaxValue));

    public LobbyActor(
        IGameEventPublisher publisher,
        IMatchProjection projection,
        IMatchDirectory directory,
        GameSettings? defaultSettings = null)
    {
        _publisher = publisher;
        _projection = projection;
        _directory = directory;
        _defaultSettings = defaultSettings ?? GameSettings.Default;

        ReceiveAsync<LobbyMessages.CreateGame>(OnCreateGame);
        ReceiveAsync<LobbyMessages.CreateSoloGame>(OnCreateSoloGame);
        ReceiveAsync<LobbyMessages.JoinByCode>(OnJoinByCode);
        Receive<LobbyMessages.ResolveGame>(OnResolveGame);
        Receive<Terminated>(OnTerminated);
    }

    public static Props PropsFor(
        IGameEventPublisher publisher,
        IMatchProjection projection,
        IMatchDirectory directory,
        GameSettings? defaultSettings = null) =>
        Props.Create(() => new LobbyActor(publisher, projection, directory, defaultSettings));

    private async Task OnCreateGame(LobbyMessages.CreateGame request)
    {
        var sender = Sender;
        var settings = request.Settings ?? _defaultSettings;
        var code = await AllocateCodeAsync();

        if (code is null)
        {
            sender.Tell(JoinResult.Failed("Could not allocate a free game code. Please try again."));
            return;
        }

        var gameId = Guid.NewGuid().ToString("n");
        var game = StartGameActor(gameId, code, settings);

        game.Tell(new GameMessages.Join(request.PlayerId, request.Nickname), sender);
    }

    private async Task OnCreateSoloGame(LobbyMessages.CreateSoloGame request)
    {
        var sender = Sender;
        var settings = request.Settings ?? _defaultSettings;
        var code = await AllocateCodeAsync();

        if (code is null)
        {
            sender.Tell(JoinResult.Failed("Could not allocate a free game code. Please try again."));
            return;
        }

        var gameId = Guid.NewGuid().ToString("n");
        var game = StartGameActor(gameId, code, settings);

        game.Tell(new GameMessages.Join(request.PlayerId, request.Nickname), sender);

        // The computer takes the second seat. It is an ordinary participant from the actor's
        // point of view, so solo games use exactly the same code path as online ones.
        game.Tell(
            new GameMessages.Join(
                $"computer-{gameId}",
                ComputerName(request.Difficulty),
                AsObserver: false,
                IsComputer: true,
                Difficulty: request.Difficulty),
            ActorRefs.NoSender);
    }

    private static string ComputerName(AiDifficulty difficulty) => difficulty switch
    {
        AiDifficulty.Easy => "Bananas (easy)",
        AiDifficulty.Hard => "Kong (hard)",
        _ => "Kong",
    };

    private async Task OnJoinByCode(LobbyMessages.JoinByCode request)
    {
        var sender = Sender;
        var code = GameCodes.Normalize(request.Code);

        if (code.Length == 0)
        {
            sender.Tell(JoinResult.Failed("Enter a game code."));
            return;
        }

        if (!_codes.TryGetValue(code, out var gameId))
        {
            var descriptor = await _directory.FindByCodeAsync(code);
            if (descriptor is null)
            {
                sender.Tell(JoinResult.Failed($"No game found with code '{code}'."));
                return;
            }

            gameId = descriptor.Id;
            StartGameActor(descriptor.Id, descriptor.Code, descriptor.Settings);
        }

        if (!_games.TryGetValue(gameId, out var game))
        {
            sender.Tell(JoinResult.Failed("That game is no longer available."));
            return;
        }

        game.Tell(new GameMessages.Join(request.PlayerId, request.Nickname, request.AsObserver), sender);
    }

    private void OnResolveGame(LobbyMessages.ResolveGame request) =>
        Sender.Tell(_games.TryGetValue(request.GameId, out var game)
            ? new LobbyMessages.GameRef(game, null)
            : new LobbyMessages.GameRef(null, $"Game '{request.GameId}' is not active."));

    private IActorRef StartGameActor(string gameId, string code, GameSettings settings)
    {
        if (_games.TryGetValue(gameId, out var existing))
        {
            return existing;
        }

        var game = Context.ActorOf(
            GameActor.PropsFor(gameId, code, settings, _publisher, _projection),
            $"game-{gameId}");

        Context.Watch(game);
        _games[gameId] = game;
        _codes[code] = gameId;

        _log.Info("Started match {0} with code {1}.", gameId, code);
        return game;
    }

    private async Task<string?> AllocateCodeAsync()
    {
        for (var attempt = 0; attempt < CodeAttempts; attempt++)
        {
            var code = GameCodes.Generate(_random);

            if (_codes.ContainsKey(code) || await _directory.CodeExistsAsync(code))
            {
                continue;
            }

            return code;
        }

        return null;
    }

    private void OnTerminated(Terminated terminated)
    {
        var gameId = _games.FirstOrDefault(pair => pair.Value.Equals(terminated.ActorRef)).Key;
        if (gameId is null)
        {
            return;
        }

        _games.Remove(gameId);

        foreach (var code in _codes.Where(pair => pair.Value == gameId).Select(pair => pair.Key).ToList())
        {
            _codes.Remove(code);
        }
    }
}
