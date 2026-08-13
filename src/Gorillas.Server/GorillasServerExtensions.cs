using Akka.Hosting;
using Akka.Persistence.Sql.Hosting;
using Gorillas.Actors;
using Gorillas.Contracts;
using Gorillas.Core.Model;
using Gorillas.Server.Realtime;
using LinqToDB;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gorillas.Server;

public static class GorillasServerExtensions
{
    /// <summary>
    /// Wires the actor system, its SQL journal and the SignalR bridge. The journal shares the
    /// game database file, so a deployment is still a single SQLite file to back up.
    /// </summary>
    public static IServiceCollection AddGorillasRealtime(
        this IServiceCollection services,
        string connectionString,
        GameSettings? defaultSettings = null)
    {
        services.AddSignalR();
        services.AddSingleton<ConnectionRegistry>();
        services.AddScoped<IReplayCatalog, SqlReplayCatalog>();
        services.TryAddSingleton<IGameEventPublisher, SignalRGameEventPublisher>();
        services.TryAddSingleton<IMatchProjection, SqlMatchProjection>();
        services.TryAddSingleton<IMatchDirectory, SqlMatchDirectory>();

        services.AddAkka("gorillas", (builder, provider) =>
        {
            builder
                .AddHocon(GorillasSerialization.Config, HoconAddMode.Prepend)
                .WithSqlPersistence(connectionString, ProviderName.SQLiteMS)
                .WithActors((system, registry) =>
                {
                    var lobby = system.ActorOf(
                        LobbyActor.PropsFor(
                            provider.GetRequiredService<IGameEventPublisher>(),
                            provider.GetRequiredService<IMatchProjection>(),
                            provider.GetRequiredService<IMatchDirectory>(),
                            defaultSettings),
                        "lobby");

                    registry.Register<LobbyActor>(lobby);
                });
        });

        return services;
    }
}
