using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Application.MarketData;

/// <summary>One Yahoo minute bar kept on the session tape. Prices are already on the instrument tick.</summary>
public sealed record SessionBar(
    UtcInstant OpenTime,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume);

/// <summary>Rolls session bars up to the chart interval and to the day quote. No painted prices.</summary>
public static class SessionBarSeries
{
    public static IReadOnlyList<Candle> Aggregate(
        InstrumentId instrumentId,
        CandleInterval interval,
        IReadOnlyList<SessionBar> bars,
        int maxBars)
    {
        ArgumentNullException.ThrowIfNull(instrumentId);
        ArgumentNullException.ThrowIfNull(interval);
        ArgumentNullException.ThrowIfNull(bars);
        if (maxBars <= 0 || bars.Count == 0)
        {
            return [];
        }

        var groups = new SortedDictionary<long, List<SessionBar>>();
        foreach (var bar in bars)
        {
            if (bar.Open <= 0m || bar.High <= 0m || bar.Low <= 0m || bar.Close <= 0m)
            {
                continue;
            }

            var bucket = FormationOpen(bar.OpenTime.Value, interval).UtcTicks;
            if (!groups.TryGetValue(bucket, out var list))
            {
                list = [];
                groups.Add(bucket, list);
            }

            list.Add(bar);
        }

        var keys = groups.Keys.ToArray();
        if (keys.Length > maxBars)
        {
            keys = keys[^maxBars..];
        }

        var candles = new List<Candle>(keys.Length);
        foreach (var key in keys)
        {
            var bucket = groups[key].OrderBy(bar => bar.OpenTime).ToArray();
            var open = bucket[0].Open;
            var close = bucket[^1].Close;
            var high = bucket.Max(bar => bar.High);
            var low = bucket.Min(bar => bar.Low);
            if (high < open)
            {
                high = open;
            }

            if (high < close)
            {
                high = close;
            }

            if (low <= 0m || low > open)
            {
                low = open;
            }

            if (low > close)
            {
                low = close;
            }

            if (high < low)
            {
                continue;
            }

            var volume = bucket.Sum(bar => bar.Volume < 0m ? 0m : bar.Volume);
            candles.Add(new Candle(
                instrumentId,
                interval,
                new UtcInstant(new DateTimeOffset(key, TimeSpan.Zero)),
                new Price(open),
                new Price(high),
                new Price(low),
                new Price(close),
                new Quantity(volume)));
        }

        return candles;
    }

    /// <summary>
    /// Open of the candle that contains <paramref name="open"/>. Intraday buckets follow the clock,
    /// and Brasília is a whole number of those steps behind UTC, so a 15-minute candle opens at
    /// :00, :15, :30 or :45 in São Paulo. A daily candle opens at the cash-session open of that day.
    /// </summary>
    public static DateTimeOffset FormationOpen(DateTimeOffset open, CandleInterval interval)
    {
        ArgumentNullException.ThrowIfNull(interval);
        if (interval.Duration >= TimeSpan.FromDays(1))
        {
            return B3Session.CashWindow(B3Session.SaoPauloDate(open)).Start;
        }

        var width = interval.Duration.Ticks;
        var ticks = open.UtcTicks / width * width;
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    /// <summary>Day change from the first open to the last close. Absent values mean the day has no bar.</summary>
    public static DayQuote Summarize(IReadOnlyList<SessionBar> bars, DateOnly day)
    {
        ArgumentNullException.ThrowIfNull(bars);

        var today = bars
            .Where(bar => bar.Open > 0m && bar.Close > 0m
                && DateOnly.FromDateTime(bar.OpenTime.Value.UtcDateTime) == day)
            .OrderBy(bar => bar.OpenTime)
            .ToArray();
        if (today.Length == 0)
        {
            return DayQuote.Empty;
        }

        var first = today[0].Open;
        var last = today[^1].Close;
        var change = last - first;
        decimal? ratio = first == 0m ? null : change / first;
        var low = today.Min(bar => bar.Low > 0m ? bar.Low : bar.Close);
        var high = today.Max(bar => bar.High > 0m ? bar.High : bar.Close);
        return new DayQuote(
            last,
            change,
            ratio,
            low,
            high,
            today.Sum(bar => bar.Volume < 0m ? 0m : bar.Volume),
            today[^1].OpenTime);
    }
}
