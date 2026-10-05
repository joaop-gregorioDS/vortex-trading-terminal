using Microsoft.Extensions.DependencyInjection;
using VortexTrading.Application.Charts;
using VortexTrading.Application.MarketData;
using VortexTrading.Application.MarketData.Replay;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Trading;
using VortexTrading.Infrastructure.MarketData.Replay;

namespace VortexTrading.Infrastructure.MarketData.Development;

public static class DevelopmentCandleDataset
{
    public const string SourceId = "development-history-v1";
    public const int BarCount = 200;
    public static InstrumentId InstrumentId { get; } = new(new Guid("11111111-1111-1111-1111-111111111111"));
    public static CandleInterval Interval { get; } = CandleInterval.OneMinute;
    public static UtcInstant StartTime { get; } = new(new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero));

    public static HistoricalReplayDataset Create()
    {
        var events = new ReplayEvent[BarCount];
        decimal previousClose = 100m;
        for (var index = 0; index < events.Length; index++)
        {
            var close = 100m + (index % 23 - 11) * 0.32m + index * 0.015m + index % 5 * 0.075m;
            var high = Math.Max(previousClose, close) + 0.35m + index % 4 * 0.05m;
            var low = Math.Min(previousClose, close) - 0.30m - index % 3 * 0.04m;
            var volume = 1000m + index % 17 * 37m + index * 0.5m;
            var candle = new Candle(InstrumentId, Interval, new UtcInstant(StartTime.Value.AddMinutes(index)),
                new Price(previousClose), new Price(high), new Price(low), new Price(close), new Quantity(volume));
            events[index] = ReplayEvent.FromCandle(candle);
            previousClose = close;
        }
        return new HistoricalReplayDataset(events);
    }

    public static IServiceCollection AddDevelopmentHistoricalCharts(this IServiceCollection services)
    {
        services.AddSingleton(new HistoricalChartSourceContext(InstrumentId, Interval, SourceId, true));
        services.AddSingleton<IMarketDataProvider>(_ => new HistoricalReplayProvider(Create()));
        return services;
    }
}
