using System.Globalization;
using System.Text.Json;
using VortexTrading.Application.MarketData;

namespace VortexTrading.Infrastructure.MarketData.Yahoo;

/// <summary>One Yahoo bar after the JSON number has been brought back onto the instrument tick.</summary>
public sealed record YahooHistoryBar(
    DateTimeOffset OpenTime,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume);

/// <summary>Candles read from one Yahoo chart payload. This is not the simulated session.</summary>
public sealed record YahooHistorySeries(
    string Symbol,
    string Currency,
    string Interval,
    IReadOnlyList<YahooHistoryBar> Bars);

/// <summary>The public chart endpoint refused the symbol or returned a payload this adapter cannot use.</summary>
public sealed class YahooChartException : Exception
{
    public YahooChartException(string message) : base(message)
    {
    }
}

/// <summary>
/// Reads the Yahoo chart document. JSON numbers arrive as binary literals such as 53.779998779296875,
/// so each price is rounded to the catalog tick. That recovers the quote; it does not invent a print.
/// </summary>
public static class YahooChartParser
{
    public static YahooHistorySeries Parse(
        string json,
        string expectedSymbol,
        string intervalToken,
        decimal tickSize,
        int decimals,
        int maxBars,
        decimal scale = 1m)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSymbol);
        if (maxBars < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBars));
        }

        if (scale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scale));
        }

        using var document = JsonDocument.Parse(json);
        var chart = document.RootElement.GetProperty("chart");
        if (chart.TryGetProperty("error", out var error) && error.ValueKind is not JsonValueKind.Null)
        {
            throw new YahooChartException("Yahoo Finance rejected the chart request.");
        }

        if (!chart.TryGetProperty("result", out var results)
            || results.ValueKind != JsonValueKind.Array
            || results.GetArrayLength() == 0)
        {
            throw new YahooChartException("Yahoo Finance returned no chart.");
        }

        var result = results[0];
        var meta = result.GetProperty("meta");
        var symbol = meta.GetProperty("symbol").GetString();
        if (!string.Equals(symbol, expectedSymbol, StringComparison.OrdinalIgnoreCase))
        {
            throw new YahooChartException("Yahoo Finance returned a different symbol.");
        }

        var currency = meta.TryGetProperty("currency", out var currencyElement)
            && currencyElement.ValueKind == JsonValueKind.String
            ? currencyElement.GetString() ?? ""
            : "";

        if (!result.TryGetProperty("timestamp", out var timestamps) || timestamps.ValueKind != JsonValueKind.Array)
        {
            throw new YahooChartException("Yahoo Finance returned no candles.");
        }

        var quote = result.GetProperty("indicators").GetProperty("quote");
        if (quote.ValueKind != JsonValueKind.Array || quote.GetArrayLength() == 0)
        {
            throw new YahooChartException("Yahoo Finance returned no candles.");
        }

        var bars = quote[0];
        var opens = bars.GetProperty("open");
        var highs = bars.GetProperty("high");
        var lows = bars.GetProperty("low");
        var closes = bars.GetProperty("close");
        var volumes = bars.GetProperty("volume");
        var count = Math.Min(timestamps.GetArrayLength(), Math.Min(opens.GetArrayLength(),
            Math.Min(highs.GetArrayLength(), Math.Min(lows.GetArrayLength(),
                Math.Min(closes.GetArrayLength(), volumes.GetArrayLength())))));

        var parsed = new List<YahooHistoryBar>(count);
        for (var index = 0; index < count; index++)
        {
            if (!TryDecimal(opens[index], out var rawOpen)
                || !TryDecimal(highs[index], out var rawHigh)
                || !TryDecimal(lows[index], out var rawLow)
                || !TryDecimal(closes[index], out var rawClose)
                || timestamps[index].ValueKind != JsonValueKind.Number
                || !timestamps[index].TryGetInt64(out var unix) || unix <= 0)
            {
                continue;
            }

            var open = CandleAggregator.RoundToTick(rawOpen * scale, tickSize, decimals);
            var high = CandleAggregator.RoundToTick(rawHigh * scale, tickSize, decimals);
            var low = CandleAggregator.RoundToTick(rawLow * scale, tickSize, decimals);
            var close = CandleAggregator.RoundToTick(rawClose * scale, tickSize, decimals);
            if (open <= 0 || close <= 0)
            {
                continue;
            }

            // Rounding can leave the extreme one tick outside the body. The body prices stay as rounded.
            if (high < open) high = open;
            if (high < close) high = close;
            if (low <= 0 || low > open) low = open;
            if (low > close) low = close;
            if (high < low)
            {
                continue;
            }

            var volume = TryDecimal(volumes[index], out var rawVolume) && rawVolume > 0
                ? decimal.Round(rawVolume, 0, MidpointRounding.AwayFromZero)
                : 0m;
            parsed.Add(new YahooHistoryBar(DateTimeOffset.FromUnixTimeSeconds(unix), open, high, low, close, volume));
        }

        parsed.Sort(static (left, right) => left.OpenTime.CompareTo(right.OpenTime));
        var ordered = new List<YahooHistoryBar>(parsed.Count);
        foreach (var bar in parsed)
        {
            if (ordered.Count > 0 && ordered[^1].OpenTime >= bar.OpenTime)
            {
                continue;
            }

            ordered.Add(bar);
        }

        if (ordered.Count == 0)
        {
            throw new YahooChartException("Yahoo Finance returned no candles.");
        }

        if (ordered.Count > maxBars)
        {
            ordered.RemoveRange(0, ordered.Count - maxBars);
        }

        return new YahooHistorySeries(symbol ?? expectedSymbol, currency, intervalToken, ordered);
    }

    private static bool TryDecimal(JsonElement element, out decimal value)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out value))
        {
            return true;
        }

        value = 0m;
        return false;
    }

    public static string FormatInstant(DateTimeOffset instant) =>
        instant.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
