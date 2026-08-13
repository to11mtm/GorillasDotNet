using System.Text.Json;
using System.Text.Json.Serialization;
using Gorillas.Core.Events;

namespace Gorillas.Data.Serialization;

/// <summary>
/// Canonical JSON encoding for the event log. Shared by persistence and the SignalR transport
/// so a stored event and a wire event are byte-identical.
/// </summary>
public static class GameEventSerializer
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = false,
    };

    public static string Serialize(GameEvent @event) => JsonSerializer.Serialize(@event, Options);

    public static GameEvent Deserialize(string payload) =>
        JsonSerializer.Deserialize<GameEvent>(payload, Options)
        ?? throw new InvalidOperationException("Event payload deserialized to null.");

    /// <summary>Stable name for the event, stored alongside the payload so logs stay queryable.</summary>
    public static string TypeNameOf(GameEvent @event) => @event.GetType().Name;
}
