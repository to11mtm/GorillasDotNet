using Gorillas.Contracts;
using Gorillas.Data;

namespace Gorillas.Server.Realtime;

/// <summary>
/// Serves match history from the read-model projection. Reading here rather than from the Akka
/// journal keeps the browse query a plain indexed SQL lookup and never disturbs a live match.
/// </summary>
public sealed class SqlReplayCatalog(IMatchStore store) : IReplayCatalog
{
    public async Task<IReadOnlyList<ReplaySummary>> ListAsync(int limit = 50, CancellationToken ct = default)
    {
        var matches = await store.ListAsync(limit: limit, ct: ct);

        return
        [
            .. matches.Select(match => new ReplaySummary(
                match.Id,
                match.Code,
                match.Status.ToString(),
                match.WinnerSlot,
                match.LastSequence,
                match.CreatedAt,
                match.CompletedAt,
                [.. match.Players.OrderBy(p => p.Slot).Select(p => p.Nickname)]))
        ];
    }

    public async Task<IReadOnlyList<EventEnvelope>> LoadAsync(string gameId, CancellationToken ct = default)
    {
        var stored = await store.LoadEventsAsync(gameId, ct: ct);
        return [.. stored.Select(e => new EventEnvelope(e.Sequence, e.Event))];
    }
}
