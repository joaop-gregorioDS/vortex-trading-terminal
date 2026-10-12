using VortexTrading.Application.MarketData;
using VortexTrading.Domain.Common.Time;

namespace VortexTrading.Infrastructure.MarketData.Yahoo;

/// <summary>Latest 1-minute close from the Yahoo client. It does not touch the paper session.</summary>
public sealed class YahooChartCloseSource(YahooFinanceChartClient yahoo) : IChartCloseSource
{
    public async Task<ChartSeriesClose?> LatestMinuteAsync(string ticker, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ticker);
        if (!CandleAggregator.TryGetProfile(ticker, out var profile) || profile is null
            || !YahooSeriesCatalog.TryForProfile(profile, out var series))
        {
            return null;
        }

        if (!YahooInterval.TryParse("1m", out var window))
        {
            return null;
        }

        var history = await yahoo.LoadAsync(
            series.Symbol, window, series.TickSize, series.Decimals, 2, cancellationToken, series.Scale);
        if (history.Bars.Count == 0)
        {
            return null;
        }

        var bar = history.Bars[^1];
        return new ChartSeriesClose(bar.Close, new UtcInstant(bar.OpenTime), bar.Volume);
    }
}
