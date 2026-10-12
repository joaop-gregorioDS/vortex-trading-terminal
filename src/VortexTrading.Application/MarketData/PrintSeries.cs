using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Application.MarketData;

/// <summary>One price the session actually published.</summary>
public sealed record MarketPrint(UtcInstant EventTime, decimal Price, decimal Volume);

/// <summary>Day statistics derived from prints. Absent values mean the day has no print yet.</summary>
public sealed record DayQuote(
    decimal? Last,
    decimal? Change,
    decimal? ChangeRatio,
    decimal? Low,
    decimal? High,
    decimal Volume,
    UtcInstant? UpdatedAt)
{
    public static DayQuote Empty { get; } = new(null, null, null, null, null, 0m, null);
}

/// <summary>Turns the prints the session published into candles and a day quote. No painted prices.</summary>
public static class PrintSeries
{
    public static IReadOnlyList<Candle> Aggregate(
        InstrumentId instrumentId,
        CandleInterval interval,
        IReadOnlyList<MarketPrint> prints,
        int maxBars)
    {
        ArgumentNullException.ThrowIfNull(instrumentId);
        ArgumentNullException.ThrowIfNull(interval);
        ArgumentNullException.ThrowIfNull(prints);
        if (maxBars <= 0 || prints.Count == 0)
        {
            return [];
        }

        var width = interval.Duration.Ticks;
        var groups = new SortedDictionary<long, List<MarketPrint>>();
        foreach (var print in prints)
        {
            if (print.Price <= 0m || print.Volume <= 0m)
            {
                continue;
            }

            var bucket = print.EventTime.Value.UtcTicks / width * width;
            if (!groups.TryGetValue(bucket, out var list))
            {
                list = [];
                groups.Add(bucket, list);
            }

            list.Add(print);
        }

        var keys = groups.Keys.ToArray();
        if (keys.Length > maxBars)
        {
            keys = keys[^maxBars..];
        }

        var candles = new List<Candle>(keys.Length);
        foreach (var key in keys)
        {
            var bucket = groups[key].OrderBy(print => print.EventTime).ToArray();
            var open = bucket[0].Price;
            var close = bucket[^1].Price;
            var high = bucket.Max(print => print.Price);
            var low = bucket.Min(print => print.Price);
            var volume = bucket.Sum(print => print.Volume);
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

    /// <summary>Summarizes prints whose UTC date is <paramref name="day"/>.</summary>
    public static DayQuote Summarize(IReadOnlyList<MarketPrint> prints, DateOnly day)
    {
        ArgumentNullException.ThrowIfNull(prints);

        var today = prints
            .Where(print => print.Price > 0m && print.Volume > 0m
                && DateOnly.FromDateTime(print.EventTime.Value.UtcDateTime) == day)
            .OrderBy(print => print.EventTime)
            .ToArray();
        if (today.Length == 0)
        {
            return DayQuote.Empty;
        }

        var first = today[0].Price;
        var last = today[^1].Price;
        var change = last - first;
        decimal? ratio = first == 0m ? null : change / first;
        return new DayQuote(
            last,
            change,
            ratio,
            today.Min(print => print.Price),
            today.Max(print => print.Price),
            today.Sum(print => print.Volume),
            today[^1].EventTime);
    }
}
