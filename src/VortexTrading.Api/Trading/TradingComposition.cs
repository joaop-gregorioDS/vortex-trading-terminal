using Microsoft.Extensions.DependencyInjection.Extensions;
using VortexTrading.Application.Risk;
using VortexTrading.Infrastructure.MarketData.Yahoo;
using VortexTrading.Infrastructure.Trading.Session;

namespace VortexTrading.Api.Trading;

/// <summary>Composition for the paper trading session and its Yahoo pregão feed.</summary>
public static class TradingComposition
{
    /// <summary>
    /// Registers the single paper trading session and the feed that follows the free Yahoo quotes.
    /// </summary>
    /// <remarks>
    /// Both are singletons on purpose. The session owns a provider whose report channel has exactly one
    /// logical consumer and a coordinator that is not thread-safe, so a per-request instance would
    /// create separate order books and separate report consumers for the same account. The feed is a
    /// hosted service so the pregão keeps updating whether or not anyone is calling.
    /// </remarks>
    public static IServiceCollection AddPaperTradingSession(this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<RiskOptions>(configuration.GetSection(RiskOptions.SectionName));
        services.TryAddSingleton<PaperTradingSession>();
        services.Configure<MarketFeedOptions>(configuration.GetSection(MarketFeedOptions.SectionName));
        services.AddSingleton<IYahooSessionSource, YahooSessionSource>();
        services.AddHostedService<YahooMarketFeed>();
        services.AddHostedService<ChartClosePoller>();

        return services;
    }
}
