using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using VortexTrading.Infrastructure.Persistence;

namespace VortexTrading.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("PostgreSQL");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:PostgreSQL must be configured and must not be empty or whitespace.");
        }

        try
        {
            _ = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException(
                "ConnectionStrings:PostgreSQL must be a valid PostgreSQL connection string.");
        }

        services.AddDbContext<VortexTradingDbContext>(options =>
            options.UseNpgsql(connectionString, postgres => postgres.SetPostgresVersion(18, 0)));

        services.AddHealthChecks()
            .AddDbContextCheck<VortexTradingDbContext>("postgresql", tags: ["ready"]);

        return services;
    }
}
