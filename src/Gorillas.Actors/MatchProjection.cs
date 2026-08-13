using Gorillas.Core.Events;
using Gorillas.Core.Model;
using Gorillas.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Gorillas.Actors;

/// <summary>
/// Write side (the Akka journal) is authoritative; this is the query-side projection the
/// lobby and match browser read. If the two ever diverge, the journal wins and the projection
/// can be rebuilt by replaying it.
/// </summary>
public interface IMatchProjection
{
    Task RecordEventsAsync(string gameId, long fromSequence, IReadOnlyList<GameEvent> events, CancellationToken ct = default);
}

public sealed class SqlMatchProjection(IServiceScopeFactory scopeFactory) : IMatchProjection
{
    public async Task RecordEventsAsync(
        string gameId, long fromSequence, IReadOnlyList<GameEvent> events, CancellationToken ct = default)
    {
        if (events.Count == 0)
        {
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IMatchStore>();

        // The index row is derived from the opening event, so the projection needs no
        // out-of-band creation step.
        if (events[0] is GameCreated created && await store.FindByIdAsync(gameId, ct) is null)
        {
            await store.CreateAsync(created.GameId, created.GameCode, created.Seed, created.Settings, ct);
        }

        try
        {
            await store.AppendEventsAsync(gameId, fromSequence, events, ct);
        }
        catch (MatchConcurrencyException)
        {
            // These events are already projected (a retry, or an actor re-emitting after
            // recovery). The journal is the source of truth, so this is safe to ignore.
        }
    }
}

/// <summary>Used when no database is configured, e.g. in unit tests.</summary>
public sealed class NullMatchProjection : IMatchProjection
{
    public Task RecordEventsAsync(string gameId, long fromSequence, IReadOnlyList<GameEvent> events, CancellationToken ct = default) =>
        Task.CompletedTask;
}

public sealed record MatchDescriptor(string Id, string Code, GameSettings Settings);

/// <summary>Lets the lobby resolve a game code to a match without owning a database connection.</summary>
public interface IMatchDirectory
{
    Task<MatchDescriptor?> FindByCodeAsync(string code, CancellationToken ct = default);

    Task<bool> CodeExistsAsync(string code, CancellationToken ct = default);
}

public sealed class SqlMatchDirectory(IServiceScopeFactory scopeFactory) : IMatchDirectory
{
    public async Task<MatchDescriptor?> FindByCodeAsync(string code, CancellationToken ct = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IMatchStore>();

        var match = await store.FindByCodeAsync(code, ct);
        return match is null ? null : new MatchDescriptor(match.Id, match.Code, match.Settings);
    }

    public async Task<bool> CodeExistsAsync(string code, CancellationToken ct = default) =>
        await FindByCodeAsync(code, ct) is not null;
}

public sealed class NullMatchDirectory : IMatchDirectory
{
    public Task<MatchDescriptor?> FindByCodeAsync(string code, CancellationToken ct = default) =>
        Task.FromResult<MatchDescriptor?>(null);

    public Task<bool> CodeExistsAsync(string code, CancellationToken ct = default) => Task.FromResult(false);
}
