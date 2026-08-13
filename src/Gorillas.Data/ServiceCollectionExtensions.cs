using LinqToDB;
using LinqToDB.AspNet;
using LinqToDB.AspNet.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace Gorillas.Data;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGorillasData(this IServiceCollection services, string connectionString)
    {
        services.AddLinqToDBContext<GorillasDataConnection>((provider, options) =>
            options
                .UseSQLite(connectionString)
                .UseDefaultLogging(provider));

        services.AddScoped<IMatchStore, MatchStore>();

        return services;
    }
}
