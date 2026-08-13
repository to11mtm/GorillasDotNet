namespace Gorillas.Contracts;

public sealed record ReplaySummary(
    string GameId,
    string GameCode,
    string Status,
    int? WinnerSlot,
    long EventCount,
    DateTime CreatedAt,
    DateTime? CompletedAt,
    IReadOnlyList<string> PlayerNames);

/// <summary>
/// Read-only access to finished and in-progress match logs. Lets the replay UI live in the
/// component library without taking a dependency on the database layer.
/// </summary>
public interface IReplayCatalog
{
    Task<IReadOnlyList<ReplaySummary>> ListAsync(int limit = 50, CancellationToken ct = default);

    Task<IReadOnlyList<EventEnvelope>> LoadAsync(string gameId, CancellationToken ct = default);
}
