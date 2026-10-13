using VortexTrading.Application.MarketData;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;

namespace VortexTrading.Application.Tests;

public sealed class SessionBarSeriesTests
{
    [Fact]
    public void FiveMinuteCandlesUseTheRealOpenAndClose()
    {
        var instrument = CandleAggregator.GetProfile("PETR4").InstrumentId;
        var first = Bar(new DateTimeOffset(2026, 10, 13, 16, 0, 0, TimeSpan.Zero), 10m, 12m, 9m, 11m, 4m);
        var second = Bar(new DateTimeOffset(2026, 10, 13, 16, 4, 0, TimeSpan.Zero), 11m, 13m, 10.5m, 12.5m, 6m);

        var candles = SessionBarSeries.Aggregate(instrument, CandleInterval.FiveMinutes, [second, first], 10);

        var candle = Assert.Single(candles);
        Assert.Equal(10m, candle.Open.Value);
        Assert.Equal(13m, candle.High.Value);
        Assert.Equal(9m, candle.Low.Value);
        Assert.Equal(12.5m, candle.Close.Value);
        Assert.Equal(10m, candle.Volume.Value);
    }

    [Fact]
    public void FifteenMinuteCandlesOpenOnTheFormationClock()
    {
        var instrument = CandleAggregator.GetProfile("PETR4").InstrumentId;
        var open = new DateTimeOffset(2026, 10, 13, 13, 0, 0, TimeSpan.Zero);
        var inside = Bar(open.AddMinutes(14), 11m, 12m, 10.5m, 12m, 2m);
        var first = Bar(open, 10m, 11m, 9m, 10.5m, 3m);
        var next = Bar(open.AddMinutes(15), 12m, 13m, 12m, 12.5m, 1m);

        var candles = SessionBarSeries.Aggregate(
            instrument, CandleInterval.FifteenMinutes, [next, inside, first], 10);

        Assert.Equal(2, candles.Count);
        Assert.Equal(open, candles[0].OpenTime.Value);
        Assert.Equal(10m, candles[0].Open.Value);
        Assert.Equal(12m, candles[0].Close.Value);
        Assert.Equal(12m, candles[0].High.Value);
        Assert.Equal(9m, candles[0].Low.Value);
        Assert.Equal(open.AddMinutes(15), candles[1].OpenTime.Value);
        Assert.Equal(12.5m, candles[1].Close.Value);
    }

    [Fact]
    public void TheDayQuoteMovesFromTheFirstOpenToTheLastClose()
    {
        var day = new DateOnly(2026, 10, 13);
        var open = Bar(new DateTimeOffset(2026, 10, 13, 13, 0, 0, TimeSpan.Zero), 10m, 11m, 9m, 10.5m, 2m);
        var close = Bar(new DateTimeOffset(2026, 10, 13, 19, 0, 0, TimeSpan.Zero), 10.5m, 12m, 10m, 11m, 3m);

        var quote = SessionBarSeries.Summarize([close, open], day);

        Assert.Equal(11m, quote.Last);
        Assert.Equal(1m, quote.Change);
        Assert.Equal(0.1m, quote.ChangeRatio);
        Assert.Equal(9m, quote.Low);
        Assert.Equal(12m, quote.High);
        Assert.Equal(5m, quote.Volume);
    }

    private static SessionBar Bar(
        DateTimeOffset open, decimal openPrice, decimal high, decimal low, decimal close, decimal volume) =>
        new(new UtcInstant(open), openPrice, high, low, close, volume);
}
