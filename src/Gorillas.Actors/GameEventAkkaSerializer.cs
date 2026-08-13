using System.Text;
using Akka.Serialization;
using Gorillas.Core.Events;
using Gorillas.Data.Serialization;

namespace Gorillas.Actors;

/// <summary>
/// Persists journal entries using the same System.Text.Json encoding as the read model, so a
/// stored journal payload is human-readable JSON and shares one tested format with the
/// transport. Avoids relying on Akka's default reflection-based JSON for polymorphic records.
/// </summary>
public sealed class GameEventAkkaSerializer(Akka.Actor.ExtendedActorSystem system) : SerializerWithStringManifest(system)
{
    public const int SerializerId = 9171;

    private const string EventManifest = "gorillas-event";

    public override int Identifier => SerializerId;

    public override string Manifest(object o) => EventManifest;

    public override byte[] ToBinary(object obj) => obj is GameEvent @event
        ? Encoding.UTF8.GetBytes(GameEventSerializer.Serialize(@event))
        : throw new ArgumentException($"Cannot serialize '{obj.GetType().FullName}'.", nameof(obj));

    public override object FromBinary(byte[] bytes, string manifest) => manifest switch
    {
        EventManifest => GameEventSerializer.Deserialize(Encoding.UTF8.GetString(bytes)),
        _ => throw new ArgumentException($"Unknown manifest '{manifest}'.", nameof(manifest)),
    };
}
