using System.Collections.Concurrent;
using Gorillas.Core.Model;

namespace Gorillas.Actors.Tests;

public sealed class InMemoryMatchDirectory : IMatchDirectory
{
    private readonly ConcurrentDictionary<string, MatchDescriptor> _byCode = new(StringComparer.OrdinalIgnoreCase);

    public void Add(string id, string code, GameSettings? settings = null) =>
        _byCode[code] = new MatchDescriptor(id, code, settings ?? GameSettings.Default);

    public Task<MatchDescriptor?> FindByCodeAsync(string code, CancellationToken ct = default) =>
        Task.FromResult(_byCode.TryGetValue(code, out var descriptor) ? descriptor : null);

    public Task<bool> CodeExistsAsync(string code, CancellationToken ct = default) =>
        Task.FromResult(_byCode.ContainsKey(code));
}
