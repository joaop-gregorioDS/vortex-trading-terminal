using VortexTrading.Application.MarketData;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;

namespace VortexTrading.Application.Tests;

public sealed class PrintSeriesTests
{
    private static readonly InstrumentId Petr = new(new Guid("11111111-1111-1111-1111-111111111111"));
    private static readonly InstrumentId Vale = new(new Guid("22222222-2222-2222-2222-222222222222"));

    [Fact]
    public void TwoPrintsInTheSameMinuteBecomeOneCandle()
    {
        var open = At(2026, 10, 2, 14, 0, 10);
        var prints = new MarketPrint[]
        {
            new(open, 10m, 5m),
            new(At(2026, 10, 2, 14, 0, 40), 12m, 7m)
        };

        var candles = PrintSeries.Aggregate(Petr, CandleInterval.OneMinute, prints, 10);

        var candle = Assert.Single(candles);
        Assert.Equal(10m, candle.Open.Value);
        Assert.Equal(12m, candle.High.Value);
        Assert.Equal(10m, candle.Low.Value);
        Assert.Equal(12m, candle.Close.Value);
        Assert.Equal(12m, candle.Volume.Value);
        Assert.Equal(At(2026, 10, 2, 14, 0, 0), candle.OpenTime);
    }

    [Fact]
    public void AQuoteUsesOnlyPrintsFromTheSimulatedDay()
    {
        var prints = new MarketPrint[]
        {
            new(At(2026, 10, 1, 18, 0, 0), 100m, 1m),
            new(At(2026, 10, 2, 14, 0, 0), 80m, 2m),
            new(At(2026, 10, 2, 14, 1, 0), 90m, 3m)
        };

        var quote = PrintSeries.Summarize(prints, new DateOnly(2026, 10, 2));

        Assert.Equal(90m, quote.Last);
        Assert.Equal(10m, quote.Change);
        Assert.Equal(0.125m, quote.ChangeRatio);
        Assert.Equal(80m, quote.Low);
        Assert.Equal(90m, quote.High);
        Assert.Equal(5m, quote.Volume);
    }

    [Fact]
    public void AggregatingOneInstrumentDoesNotReadAnother()
    {
        var prints = new MarketPrint[] { new(At(2026, 10, 2, 14, 0, 0), 71m, 4m) };

        var petr = PrintSeries.Aggregate(Petr, CandleInterval.OneMinute, [], 10);
        var vale = PrintSeries.Aggregate(Vale, CandleInterval.OneMinute, prints, 10);

        Assert.Empty(petr);
        Assert.Equal(71m, Assert.Single(vale).Close.Value);
    }

    private static UtcInstant At(int year, int month, int day, int hour, int minute, int second) =>
        new(new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.Zero));
}
