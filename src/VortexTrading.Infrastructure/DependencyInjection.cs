using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using VortexTrading.Application.MarketData;
using VortexTrading.Application.Trading.Execution;
using VortexTrading.Infrastructure.MarketData.Yahoo;
using VortexTrading.Infrastructure.Trading.Session;
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

        // One options registration, owned by the factory. Registering AddDbContext as well would add a
        // second, scoped DbContextOptions that the singleton factory cannot consume, and the host would
        // refuse to start.
        services.AddDbContextFactory<VortexTradingDbContext>(options =>
            options.UseNpgsql(connectionString, postgres => postgres.SetPostgresVersion(18, 0)));

        // The request-scoped context is derived from the factory so both share one options instance.
        // Ordinary consumers keep taking a scoped VortexTradingDbContext, while the execution scope,
        // which is a singleton and outlives any request, takes the factory instead.
        services.AddScoped(provider =>
            provider.GetRequiredService<IDbContextFactory<VortexTradingDbContext>>().CreateDbContext());

        services.AddSingleton<IOrderStore, PostgresOrderStore>();
        services.AddSingleton<IMinuteBarStore, PostgresMinuteBarStore>();
        services.AddScoped<IChartCloseSource, YahooChartCloseSource>();
        services.AddHttpClient<YahooFinanceChartClient>(client =>
        {
            client.BaseAddress = new Uri("https://query1.finance.yahoo.com/");
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("VortexTrading/1.0");
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        });

        services.AddHealthChecks()
            .AddDbContextCheck<VortexTradingDbContext>("postgresql", tags: ["ready"]);

        return services;
    }
}
