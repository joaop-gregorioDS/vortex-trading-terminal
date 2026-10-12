using VortexTrading.Domain.Common.Time;

namespace VortexTrading.Application.MarketData;

/// <summary>The latest 1-minute close of the chart series for one catalog ticker.</summary>
/// <param name="Close">Close already rounded to the series tick.</param>
/// <param name="OpenTime">When that candle opened. An order submitted against it must not fill on it.</param>
/// <param name="Volume">Reported bar volume. Zero means the feed did not publish a size.</param>
public sealed record ChartSeriesClose(decimal Close, UtcInstant OpenTime, decimal Volume);

/// <summary>Reads the chart series. It does not publish anything into the simulated session.</summary>
public interface IChartCloseSource
{
    Task<ChartSeriesClose?> LatestMinuteAsync(string ticker, CancellationToken cancellationToken);
}
