using Microsoft.Extensions.DependencyInjection.Extensions;
using VortexTrading.Infrastructure.Trading.Session;

namespace VortexTrading.Api.Trading;

/// <summary>Composition for the simulated trading session.</summary>
public static class TradingComposition
{
    /// <summary>
    /// Registers the single paper trading session and its simulated market feed.
    /// </summary>
    /// <remarks>
    /// Both are singletons on purpose. The session owns a provider whose report channel has exactly one
    /// logical consumer and a coordinator that is not thread-safe, so a per-request instance would
    /// create separate order books and separate report consumers for the same account. The feed is a
    /// hosted service so the simulation keeps producing prices whether or not anyone is calling.
    /// <para>
    /// The feed is only registered in Development for now. A production feed needs a decision about
    /// authorized data and about how a shared deployment handles concurrent users, which the current
    /// single-account simulation deliberately does not pretend to solve.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddPaperTradingSession(this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingleton<PaperTradingSession>();
        services.Configure<MarketFeedOptions>(configuration.GetSection(MarketFeedOptions.SectionName));
        services.AddHostedService<SimulatedMarketFeed>();

        return services;
    }
}
