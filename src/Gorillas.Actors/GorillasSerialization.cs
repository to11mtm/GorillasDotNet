using Akka.Configuration;

namespace Gorillas.Actors;

public static class GorillasSerialization
{
    /// <summary>
    /// Binds every <c>GameEvent</c> to the JSON serializer shared with the read model, instead
    /// of Akka's default reflection-based JSON. Journal rows stay readable and the encoding is
    /// the one already covered by the persistence tests.
    /// </summary>
    public static Config Config { get; } = ConfigurationFactory.ParseString($$"""
        akka.actor {
          serializers {
            gorillas-event = "{{typeof(GameEventAkkaSerializer).FullName}}, Gorillas.Actors"
          }
          serialization-bindings {
            "Gorillas.Core.Events.GameEvent, Gorillas.Core" = gorillas-event
          }
        }
        """);
}
