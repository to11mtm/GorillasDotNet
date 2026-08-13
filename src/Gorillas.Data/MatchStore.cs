using System.Text.Json;
using Gorillas.Core;
using Gorillas.Core.Events;
using Gorillas.Core.Model;
using Gorillas.Data.Entities;
using Gorillas.Data.Serialization;
using LinqToDB;
using LinqToDB.Data;

namespace Gorillas.Data;

public interface IMatchStore
{
    Task<MatchRecord> CreateAsync(string id, string code, ulong seed, GameSettings settings, CancellationToken ct = default);

    Task<MatchRecord?> FindByIdAsync(string id, CancellationToken ct = default);

    Task<MatchRecord?> FindByCodeAsync(string code, CancellationToken ct = default);

    Task<long> AppendEventsAsync(string matchId, long expectedSequence, IReadOnlyList<GameEvent> events, CancellationToken ct = default);

    Task<IReadOnlyList<StoredEvent>> LoadEventsAsync(string matchId, long afterSequence = 0, CancellationToken ct = default);

    Task<GameState> LoadStateAsync(string matchId, CancellationToken ct = default);

    Task<IReadOnlyList<MatchSummary>> ListAsync(MatchStatus? status = null, int limit = 50, CancellationToken ct = default);
}

/// <summary>
/// Append-only event log plus a small denormalised match index. The index exists purely so the
/// lobby and replay browser can answer questions without folding every log.
/// </summary>
public sealed class MatchStore(GorillasDataConnection db) : IMatchStore
{
    public async Task<MatchRecord> CreateAsync(
        string id, string code, ulong seed, GameSettings settings, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var row = new MatchRow
        {
            Id = id,
            Code = code,
            Seed = unchecked((long)seed),
            SettingsJson = JsonSerializer.Serialize(settings, GameEventSerializer.Options),
            Status = MatchStatus.Open.ToString(),
            LastSequence = 0,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await db.InsertAsync(row, token: ct);
        return ToRecord(row, []);
    }

    public async Task<MatchRecord?> FindByIdAsync(string id, CancellationToken ct = default)
    {
        var row = await db.Matches.FirstOrDefaultAsync(m => m.Id == id, ct);
        return row is null ? null : ToRecord(row, await LoadPlayersAsync(row.Id, ct));
    }

    public async Task<MatchRecord?> FindByCodeAsync(string code, CancellationToken ct = default)
    {
        var normalized = code.ToUpperInvariant();
        var row = await db.Matches.FirstOrDefaultAsync(m => m.Code == normalized, ct);
        return row is null ? null : ToRecord(row, await LoadPlayersAsync(row.Id, ct));
    }

    public async Task<long> AppendEventsAsync(
        string matchId, long expectedSequence, IReadOnlyList<GameEvent> events, CancellationToken ct = default)
    {
        if (events.Count == 0)
        {
            return expectedSequence;
        }

        await using var transaction = await db.BeginTransactionAsync(ct);

        var current = await db.Matches
            .Where(m => m.Id == matchId)
            .Select(m => (long?)m.LastSequence)
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException($"Match '{matchId}' does not exist.");

        if (current != expectedSequence)
        {
            throw new MatchConcurrencyException(matchId, expectedSequence, current);
        }

        var now = DateTime.UtcNow;
        var sequence = current;
        var rows = new List<MatchEventRow>(events.Count);

        foreach (var @event in events)
        {
            sequence++;
            rows.Add(new MatchEventRow
            {
                MatchId = matchId,
                Sequence = sequence,
                Type = GameEventSerializer.TypeNameOf(@event),
                PayloadJson = GameEventSerializer.Serialize(@event),
                CreatedAt = now,
            });
        }

        await db.BulkCopyAsync(rows, cancellationToken: ct);
        await ApplyIndexUpdatesAsync(matchId, events, sequence, now, ct);
        await transaction.CommitAsync(ct);

        return sequence;
    }

    public async Task<IReadOnlyList<StoredEvent>> LoadEventsAsync(
        string matchId, long afterSequence = 0, CancellationToken ct = default)
    {
        var rows = await db.MatchEvents
            .Where(e => e.MatchId == matchId && e.Sequence > afterSequence)
            .OrderBy(e => e.Sequence)
            .ToListAsync(ct);

        return [.. rows.Select(r => new StoredEvent(r.Sequence, GameEventSerializer.Deserialize(r.PayloadJson)))];
    }

    public async Task<GameState> LoadStateAsync(string matchId, CancellationToken ct = default)
    {
        var events = await LoadEventsAsync(matchId, 0, ct);
        return GameState.Replay(events.Select(e => e.Event));
    }

    public async Task<IReadOnlyList<MatchSummary>> ListAsync(
        MatchStatus? status = null, int limit = 50, CancellationToken ct = default)
    {
        var query = db.Matches.AsQueryable();

        if (status is { } wanted)
        {
            var name = wanted.ToString();
            query = query.Where(m => m.Status == name);
        }

        var rows = await query
            .OrderByDescending(m => m.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);

        if (rows.Count == 0)
        {
            return [];
        }

        var ids = rows.Select(r => r.Id).ToList();
        var players = await db.MatchPlayers
            .Where(p => ids.Contains(p.MatchId))
            .ToListAsync(ct);

        return
        [
            .. rows.Select(row => new MatchSummary(
                row.Id,
                row.Code,
                Enum.Parse<MatchStatus>(row.Status),
                row.WinnerSlot,
                row.LastSequence,
                row.CreatedAt,
                row.CompletedAt,
                [.. players.Where(p => p.MatchId == row.Id).OrderBy(p => p.Slot).Select(ToRecord)]))
        ];
    }

    /// <summary>Keeps the query-side index in step with the log inside the same transaction.</summary>
    private async Task ApplyIndexUpdatesAsync(
        string matchId, IReadOnlyList<GameEvent> events, long sequence, DateTime now, CancellationToken ct)
    {
        foreach (var joined in events.OfType<PlayerJoined>())
        {
            await db.InsertOrReplaceAsync(
                new MatchPlayerRow
                {
                    MatchId = matchId,
                    Slot = joined.Slot,
                    PlayerId = joined.PlayerId,
                    Nickname = joined.Nickname,
                    IsComputer = joined.IsComputer,
                    JoinedAt = now,
                },
                token: ct);
        }

        var ended = events.OfType<MatchEnded>().LastOrDefault();

        var update = db.Matches
            .Where(m => m.Id == matchId)
            .AsUpdatable()
            .Set(m => m.LastSequence, sequence)
            .Set(m => m.UpdatedAt, now);

        if (ended is not null)
        {
            update = update
                .Set(m => m.Status, MatchStatus.Completed.ToString())
                .Set(m => m.WinnerSlot, (int?)ended.WinnerSlot)
                .Set(m => m.CompletedAt, (DateTime?)now);
        }
        else if (events.Any(e => e is RoundStarted))
        {
            update = update.Set(m => m.Status, MatchStatus.InProgress.ToString());
        }

        await update.UpdateAsync(ct);
    }

    private async Task<IReadOnlyList<MatchPlayerRecord>> LoadPlayersAsync(string matchId, CancellationToken ct)
    {
        var rows = await db.MatchPlayers
            .Where(p => p.MatchId == matchId)
            .OrderBy(p => p.Slot)
            .ToListAsync(ct);

        return [.. rows.Select(ToRecord)];
    }

    private static MatchPlayerRecord ToRecord(MatchPlayerRow row) =>
        new(row.Slot, row.PlayerId, row.Nickname, row.IsComputer);

    private static MatchRecord ToRecord(MatchRow row, IReadOnlyList<MatchPlayerRecord> players) =>
        new(
            row.Id,
            row.Code,
            unchecked((ulong)row.Seed),
            JsonSerializer.Deserialize<GameSettings>(row.SettingsJson, GameEventSerializer.Options) ?? GameSettings.Default,
            Enum.Parse<MatchStatus>(row.Status),
            row.WinnerSlot,
            row.LastSequence,
            row.CreatedAt,
            row.UpdatedAt,
            row.CompletedAt,
            players);
}
