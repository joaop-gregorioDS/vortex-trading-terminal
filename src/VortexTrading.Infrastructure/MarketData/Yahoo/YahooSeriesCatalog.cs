using VortexTrading.Application.MarketData;

namespace VortexTrading.Infrastructure.MarketData.Yahoo;

/// <summary>How one catalog ticker is read from the delayed Yahoo chart.</summary>
/// <remarks>
/// Mini index and mini dollar are not listed, so those charts use the Ibovespa and the commercial
/// dollar in mini points. The tick here is the chart series tick, which is finer than the simulated
/// contract tick.
/// </remarks>
public readonly record struct YahooSeriesSpec(
    string Symbol,
    decimal TickSize,
    int Decimals,
    decimal Scale,
    string SeriesName);

public static class YahooSeriesCatalog
{
    public static bool TryForProfile(AssetMarketProfile profile, out YahooSeriesSpec series)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (profile.Ticker.Equals("WING26", StringComparison.OrdinalIgnoreCase))
        {
            series = new YahooSeriesSpec("^BVSP", 1m, 0, 1m, "Ibovespa");
            return true;
        }

        if (profile.Ticker.Equals("WDOU26", StringComparison.OrdinalIgnoreCase))
        {
            series = new YahooSeriesSpec("BRL=X", 0.1m, 1, 1000m, "Dólar comercial");
            return true;
        }

        if (profile.PointValue == 1m)
        {
            series = new YahooSeriesSpec(profile.Ticker + ".SA", profile.TickSize, profile.Decimals, 1m, profile.Ticker);
            return true;
        }

        series = default;
        return false;
    }
}
