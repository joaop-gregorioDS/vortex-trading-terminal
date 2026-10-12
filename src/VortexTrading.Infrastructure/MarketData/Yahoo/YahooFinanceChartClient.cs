using VortexTrading.Domain.Markets;

namespace VortexTrading.Infrastructure.MarketData.Yahoo;

/// <summary>Which Yahoo chart window matches one of our candle intervals.</summary>
public readonly record struct YahooInterval(string Token, string QueryInterval, string Range, CandleInterval CandleInterval)
{
    public static bool TryParse(string? token, out YahooInterval interval)
    {
        switch (token?.Trim().ToLowerInvariant())
        {
            case "1d":
                interval = new("1d", "1d", "1y", CandleInterval.OneDay);
                return true;
            case "1h":
            case "60m":
                interval = new("1h", "60m", "5d", CandleInterval.OneHour);
                return true;
            case "15m":
                interval = new("15m", "15m", "5d", CandleInterval.FifteenMinutes);
                return true;
            case "5m":
                interval = new("5m", "5m", "5d", CandleInterval.FiveMinutes);
                return true;
            case "1m":
                interval = new("1m", "1m", "1d", CandleInterval.OneMinute);
                return true;
            default:
                interval = default;
                return false;
        }
    }
}

/// <summary>
/// Reads delayed candles from the public Yahoo chart endpoint.
/// Nothing returned here is published into the paper session.
/// </summary>
public sealed class YahooFinanceChartClient(HttpClient http)
{
    public const int MaximumBars = 500;

    public async Task<YahooHistorySeries> LoadAsync(
        string yahooSymbol,
        YahooInterval interval,
        decimal tickSize,
        int decimals,
        int maxBars,
        CancellationToken cancellationToken,
        decimal scale = 1m)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(yahooSymbol);
        if (maxBars is < 1 or > MaximumBars)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBars));
        }

        var path =
            $"v8/finance/chart/{Uri.EscapeDataString(yahooSymbol)}?interval={interval.QueryInterval}&range={interval.Range}";
        using var response = await http.GetAsync(path, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new YahooChartException("Yahoo Finance did not return this symbol.");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return YahooChartParser.Parse(json, yahooSymbol, interval.Token, tickSize, decimals, maxBars, scale);
    }
}
