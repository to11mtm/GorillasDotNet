using System.Collections.Concurrent;
using Gorillas.Contracts;

namespace Gorillas.Actors.Tests;

/// <summary>Records everything the actors broadcast so tests can assert on the wire traffic.</summary>
public sealed class RecordingPublisher : IGameEventPublisher
{
    private readonly ConcurrentQueue<EventBatch> _batches = new();
    private readonly ConcurrentQueue<PresenceUpdate> _presence = new();

    public IReadOnlyList<EventBatch> Batches => [.. _batches];

    public IReadOnlyList<PresenceUpdate> Presence => [.. _presence];

    public IReadOnlyList<EventEnvelope> AllEvents => [.. _batches.SelectMany(b => b.Events).OrderBy(e => e.Sequence)];

    public Task PublishEventsAsync(EventBatch batch)
    {
        _batches.Enqueue(batch);
        return Task.CompletedTask;
    }

    public Task PublishPresenceAsync(PresenceUpdate presence)
    {
        _presence.Enqueue(presence);
        return Task.CompletedTask;
    }
}
