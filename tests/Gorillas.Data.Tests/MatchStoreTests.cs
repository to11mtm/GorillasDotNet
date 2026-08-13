using Gorillas.Core;
using Gorillas.Core.Events;
using Gorillas.Core.Model;
using Gorillas.Data.Serialization;
using LinqToDB.Data;

namespace Gorillas.Data.Tests;

public class MatchStoreTests
{
    [Fact]
    public async Task CreatedMatchRoundTripsById()
    {
        await using var db = await TempDatabase.CreateAsync();

        var created = await db.Store.CreateAsync("m1", "BAN-7Q3", 1234567890123UL, GameSettings.Default);
        var loaded = await db.Store.FindByIdAsync("m1");

        Assert.NotNull(loaded);
        Assert.Equal(created.Id, loaded.Id);
        Assert.Equal("BAN-7Q3", loaded.Code);
        Assert.Equal(1234567890123UL, loaded.Seed);
        Assert.Equal(GameSettings.Default, loaded.Settings);
        Assert.Equal(MatchStatus.Open, loaded.Status);
        Assert.Equal(0, loaded.LastSequence);
    }

    [Fact]
    public async Task SeedsAboveLongMaxSurviveTheRoundTrip()
    {
        await using var db = await TempDatabase.CreateAsync();

        await db.Store.CreateAsync("m1", "BAN-001", ulong.MaxValue, GameSettings.Default);
        var loaded = await db.Store.FindByIdAsync("m1");

        Assert.Equal(ulong.MaxValue, loaded!.Seed);
    }

    [Fact]
    public async Task CustomSettingsRoundTrip()
    {
        await using var db = await TempDatabase.CreateAsync();
        var settings = GameSettings.Default with { RoundsToWin = 5, Gravity = 22.5, MaxWind = 3 };

        await db.Store.CreateAsync("m1", "BAN-002", 1, settings);
        var loaded = await db.Store.FindByIdAsync("m1");

        Assert.Equal(settings, loaded!.Settings);
    }

    [Fact]
    public async Task MatchesAreFoundByCodeCaseInsensitively()
    {
        await using var db = await TempDatabase.CreateAsync();
        await db.Store.CreateAsync("m1", "BAN-7Q3", 1, GameSettings.Default);

        Assert.NotNull(await db.Store.FindByCodeAsync("ban-7q3"));
        Assert.Null(await db.Store.FindByCodeAsync("BAN-XXX"));
    }

    [Fact]
    public async Task MissingMatchesReturnNull()
    {
        await using var db = await TempDatabase.CreateAsync();

        Assert.Null(await db.Store.FindByIdAsync("nope"));
        Assert.Null(await db.Store.FindByCodeAsync("NO-PE1"));
    }

    [Fact]
    public async Task AppendedEventsReloadAsEquivalentEvents()
    {
        await using var db = await TempDatabase.CreateAsync();
        var (_, log) = MatchFactory.PlayMatch();

        await db.Store.CreateAsync("m1", "BAN-7Q3", 1, GameSettings.Default);
        var sequence = await db.Store.AppendEventsAsync("m1", 0, log);

        var stored = await db.Store.LoadEventsAsync("m1");

        Assert.Equal(log.Count, sequence);
        Assert.Equal(log.Count, stored.Count);
        Assert.Equal(log, [.. stored.Select(s => s.Event)]);
        Assert.Equal(Enumerable.Range(1, log.Count).Select(i => (long)i), stored.Select(s => s.Sequence));
    }

    [Fact]
    public async Task EveryEventTypeSurvivesSerialization()
    {
        var (_, log) = MatchFactory.PlayMatch(seed: 777, throws: 8);
        var (_, forfeited) = MatchFactory.PlayToCompletion();
        var all = log.Concat(forfeited).ToList();

        // Guard against a new event type being added without a JsonDerivedType discriminator.
        Assert.Contains(all, e => e is GameCreated);
        Assert.Contains(all, e => e is PlayerJoined);
        Assert.Contains(all, e => e is RoundStarted);
        Assert.Contains(all, e => e is BananaThrown);
        Assert.Contains(all, e => e is BananaImpacted);
        Assert.Contains(all, e => e is MatchEnded);

        foreach (var @event in all)
        {
            var restored = GameEventSerializer.Deserialize(GameEventSerializer.Serialize(@event));
            Assert.Equal(@event, restored);
        }
    }

    [Fact]
    public async Task ReplayingTheStoredLogRebuildsTheLiveState()
    {
        await using var db = await TempDatabase.CreateAsync();
        var (expected, log) = MatchFactory.PlayMatch();

        await db.Store.CreateAsync("m1", "BAN-7Q3", 1, GameSettings.Default);
        await db.Store.AppendEventsAsync("m1", 0, log);

        var replayed = await db.Store.LoadStateAsync("m1");

        Assert.Equal(expected, replayed);
    }

    [Fact]
    public async Task AppendsAccumulateAcrossCalls()
    {
        await using var db = await TempDatabase.CreateAsync();
        var (_, log) = MatchFactory.PlayMatch();

        await db.Store.CreateAsync("m1", "BAN-7Q3", 1, GameSettings.Default);
        var first = await db.Store.AppendEventsAsync("m1", 0, log.Take(3).ToList());
        var second = await db.Store.AppendEventsAsync("m1", first, log.Skip(3).ToList());

        Assert.Equal(3, first);
        Assert.Equal(log.Count, second);

        var match = await db.Store.FindByIdAsync("m1");
        Assert.Equal(log.Count, match!.LastSequence);
    }

    [Fact]
    public async Task AppendingAnEmptyBatchIsANoOp()
    {
        await using var db = await TempDatabase.CreateAsync();
        await db.Store.CreateAsync("m1", "BAN-7Q3", 1, GameSettings.Default);

        var sequence = await db.Store.AppendEventsAsync("m1", 0, []);

        Assert.Equal(0, sequence);
        Assert.Empty(await db.Store.LoadEventsAsync("m1"));
    }

    [Fact]
    public async Task AppendingAtAStaleSequenceIsRejected()
    {
        await using var db = await TempDatabase.CreateAsync();
        var (_, log) = MatchFactory.PlayMatch();

        await db.Store.CreateAsync("m1", "BAN-7Q3", 1, GameSettings.Default);
        await db.Store.AppendEventsAsync("m1", 0, log);

        var error = await Assert.ThrowsAsync<MatchConcurrencyException>(
            () => db.Store.AppendEventsAsync("m1", 0, log));

        Assert.Equal(0, error.ExpectedSequence);
        Assert.Equal(log.Count, error.ActualSequence);
    }

    [Fact]
    public async Task ARejectedAppendLeavesNoPartialWrite()
    {
        await using var db = await TempDatabase.CreateAsync();
        var (_, log) = MatchFactory.PlayMatch();

        await db.Store.CreateAsync("m1", "BAN-7Q3", 1, GameSettings.Default);
        await db.Store.AppendEventsAsync("m1", 0, log);

        await Assert.ThrowsAsync<MatchConcurrencyException>(() => db.Store.AppendEventsAsync("m1", 0, log));

        var stored = await db.Store.LoadEventsAsync("m1");
        Assert.Equal(log.Count, stored.Count);
    }

    [Fact]
    public async Task AppendingToAnUnknownMatchFails()
    {
        await using var db = await TempDatabase.CreateAsync();
        var (_, log) = MatchFactory.PlayMatch();

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Store.AppendEventsAsync("ghost", 0, log));
    }

    [Fact]
    public async Task LoadingFromASequenceReturnsOnlyTheTail()
    {
        await using var db = await TempDatabase.CreateAsync();
        var (_, log) = MatchFactory.PlayMatch();

        await db.Store.CreateAsync("m1", "BAN-7Q3", 1, GameSettings.Default);
        await db.Store.AppendEventsAsync("m1", 0, log);

        var tail = await db.Store.LoadEventsAsync("m1", afterSequence: 3);

        Assert.Equal(log.Count - 3, tail.Count);
        Assert.Equal(4, tail[0].Sequence);
        Assert.Equal(log[3], tail[0].Event);
    }

    [Fact]
    public async Task ResyncingFromTheLatestSequenceReturnsNothing()
    {
        await using var db = await TempDatabase.CreateAsync();
        var (_, log) = MatchFactory.PlayMatch();

        await db.Store.CreateAsync("m1", "BAN-7Q3", 1, GameSettings.Default);
        var latest = await db.Store.AppendEventsAsync("m1", 0, log);

        Assert.Empty(await db.Store.LoadEventsAsync("m1", latest));
    }

    /// <summary>A reconnecting client folds only the delta and must land on the same state.</summary>
    [Fact]
    public async Task CatchingUpFromAnyPointReconstructsTheSameState()
    {
        await using var db = await TempDatabase.CreateAsync();
        var (expected, log) = MatchFactory.PlayMatch();

        await db.Store.CreateAsync("m1", "BAN-7Q3", 1, GameSettings.Default);
        await db.Store.AppendEventsAsync("m1", 0, log);

        for (var cursor = 0; cursor <= log.Count; cursor++)
        {
            var head = GameState.Replay(log.Take(cursor));
            var delta = await db.Store.LoadEventsAsync("m1", cursor);

            var caughtUp = delta.Aggregate(head, (state, stored) => state.Apply(stored.Event));

            Assert.Equal(expected, caughtUp);
        }
    }

    [Fact]
    public async Task PlayersAreIndexedWhenTheyJoin()
    {
        await using var db = await TempDatabase.CreateAsync();
        var (_, log) = MatchFactory.PlayMatch();

        await db.Store.CreateAsync("m1", "BAN-7Q3", 1, GameSettings.Default);
        await db.Store.AppendEventsAsync("m1", 0, log);

        var match = await db.Store.FindByIdAsync("m1");

        Assert.Equal(2, match!.Players.Count);
        Assert.Equal("Ada", match.Players[0].Nickname);
        Assert.Equal("Grace", match.Players[1].Nickname);
        Assert.Equal([0, 1], match.Players.Select(p => p.Slot));
    }

    [Fact]
    public async Task StatusMovesToInProgressWhenTheFirstRoundStarts()
    {
        await using var db = await TempDatabase.CreateAsync();
        var (_, log) = MatchFactory.PlayMatch();

        await db.Store.CreateAsync("m1", "BAN-7Q3", 1, GameSettings.Default);
        await db.Store.AppendEventsAsync("m1", 0, log);

        var match = await db.Store.FindByIdAsync("m1");

        Assert.Equal(MatchStatus.InProgress, match!.Status);
        Assert.Null(match.CompletedAt);
        Assert.Null(match.WinnerSlot);
    }

    [Fact]
    public async Task CompletingAMatchRecordsTheWinner()
    {
        await using var db = await TempDatabase.CreateAsync();
        var (_, log) = MatchFactory.PlayToCompletion();

        await db.Store.CreateAsync("m1", "BAN-7Q3", 1, GameSettings.Default);
        await db.Store.AppendEventsAsync("m1", 0, log);

        var match = await db.Store.FindByIdAsync("m1");

        Assert.Equal(MatchStatus.Completed, match!.Status);
        Assert.Equal(0, match.WinnerSlot);
        Assert.NotNull(match.CompletedAt);
    }

    [Fact]
    public async Task ListFiltersByStatus()
    {
        await using var db = await TempDatabase.CreateAsync();

        await db.Store.CreateAsync("open-1", "BAN-001", 1, GameSettings.Default);

        var (_, played) = MatchFactory.PlayMatch();
        await db.Store.CreateAsync("live-1", "BAN-002", 1, GameSettings.Default);
        await db.Store.AppendEventsAsync("live-1", 0, played);

        var (_, finished) = MatchFactory.PlayToCompletion();
        await db.Store.CreateAsync("done-1", "BAN-003", 1, GameSettings.Default);
        await db.Store.AppendEventsAsync("done-1", 0, finished);

        Assert.Equal(3, (await db.Store.ListAsync()).Count);
        Assert.Equal("open-1", Assert.Single(await db.Store.ListAsync(MatchStatus.Open)).Id);
        Assert.Equal("live-1", Assert.Single(await db.Store.ListAsync(MatchStatus.InProgress)).Id);

        var completed = Assert.Single(await db.Store.ListAsync(MatchStatus.Completed));
        Assert.Equal("done-1", completed.Id);
        Assert.Equal(2, completed.Players.Count);
    }

    [Fact]
    public async Task DuplicateGameCodesAreRejected()
    {
        await using var db = await TempDatabase.CreateAsync();
        await db.Store.CreateAsync("m1", "BAN-7Q3", 1, GameSettings.Default);

        await Assert.ThrowsAnyAsync<Exception>(
            () => db.Store.CreateAsync("m2", "BAN-7Q3", 1, GameSettings.Default));
    }

    [Fact]
    public async Task ASecondWriterCannotAppendOverTheFirst()
    {
        await using var db = await TempDatabase.CreateAsync();
        var (_, log) = MatchFactory.PlayMatch();

        await db.Store.CreateAsync("m1", "BAN-7Q3", 1, GameSettings.Default);
        await db.Store.AppendEventsAsync("m1", 0, log.Take(3).ToList());

        using var other = db.OpenSecondConnection();
        var rival = new MatchStore(other);

        await Assert.ThrowsAsync<MatchConcurrencyException>(
            () => rival.AppendEventsAsync("m1", 0, log.Skip(3).ToList()));

        Assert.Equal(3, (await rival.LoadEventsAsync("m1")).Count);
    }

    [Fact]
    public async Task DataIsVisibleAcrossConnections()
    {
        await using var db = await TempDatabase.CreateAsync();
        var (expected, log) = MatchFactory.PlayMatch();

        await db.Store.CreateAsync("m1", "BAN-7Q3", 1, GameSettings.Default);
        await db.Store.AppendEventsAsync("m1", 0, log);

        using var other = db.OpenSecondConnection();
        var reader = new MatchStore(other);

        Assert.Equal(expected, await reader.LoadStateAsync("m1"));
    }

    [Fact]
    public async Task SchemaCreationIsIdempotent()
    {
        await using var db = await TempDatabase.CreateAsync();

        await SchemaInitializer.EnsureCreatedAsync(db.Connection);
        await SchemaInitializer.EnsureCreatedAsync(db.Connection);

        await db.Store.CreateAsync("m1", "BAN-7Q3", 1, GameSettings.Default);
        Assert.NotNull(await db.Store.FindByIdAsync("m1"));
    }

    [Fact]
    public async Task SchemaCreatesTheExpectedTablesAndIndexes()
    {
        await using var db = await TempDatabase.CreateAsync();

        var objects = await db.Connection
            .QueryToListAsync<string>("SELECT name FROM sqlite_master WHERE name NOT LIKE 'sqlite_%'");

        Assert.Contains("matches", objects);
        Assert.Contains("match_players", objects);
        Assert.Contains("match_events", objects);
        Assert.Contains("ux_matches_code", objects);
        Assert.Contains("ix_matches_status", objects);
        Assert.Contains("ix_match_players_player", objects);
    }

    [Fact]
    public async Task WriteAheadLoggingIsEnabled()
    {
        await using var db = await TempDatabase.CreateAsync();

        var mode = await db.Connection.QueryToListAsync<string>("PRAGMA journal_mode");

        Assert.Equal("wal", mode[0], ignoreCase: true);
    }
}
