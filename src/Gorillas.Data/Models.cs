using Gorillas.Core;
using Gorillas.Core.Events;
using Gorillas.Core.Model;

namespace Gorillas.Data;

public enum MatchStatus
{
    Open,
    InProgress,
    Completed,
    Abandoned,
}

public sealed record MatchPlayerRecord(int Slot, string PlayerId, string Nickname, bool IsComputer);

public sealed record MatchRecord(
    string Id,
    string Code,
    ulong Seed,
    GameSettings Settings,
    MatchStatus Status,
    int? WinnerSlot,
    long LastSequence,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? CompletedAt,
    IReadOnlyList<MatchPlayerRecord> Players);

public sealed record StoredEvent(long Sequence, GameEvent Event);

public sealed record MatchSummary(
    string Id,
    string Code,
    MatchStatus Status,
    int? WinnerSlot,
    long LastSequence,
    DateTime CreatedAt,
    DateTime? CompletedAt,
    IReadOnlyList<MatchPlayerRecord> Players);

/// <summary>
/// Raised when an append is attempted against a sequence the store has already moved past —
/// two writers raced for the same match.
/// </summary>
public sealed class MatchConcurrencyException(string matchId, long expected, long actual)
    : Exception($"Match '{matchId}' is at sequence {actual}, but the append expected {expected}.")
{
    public string MatchId { get; } = matchId;

    public long ExpectedSequence { get; } = expected;

    public long ActualSequence { get; } = actual;
}
