using VortexTrading.Application.MarketData.Replay;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Application.Tests;

public sealed class ReplayEventAvailabilityTests
{
    private static readonly InstrumentId A = new(new Guid("11111111-1111-1111-1111-111111111111"));
    private static readonly InstrumentId B = new(new Guid("22222222-2222-2222-2222-222222222222"));
    private static readonly UtcInstant Start = new(new DateTimeOffset(2026, 10, 2, 10, 0, 0, TimeSpan.Zero).AddTicks(7));

    [Fact]
    public void RejectsNullEvent()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => ReplayEventAvailability.GetAvailabilityTime(null));

        Assert.Equal("replayEvent", exception.ParamName);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public void QuoteAvailabilityUsesEventTimeRegardlessOfReceivedTime(int receivedOffsetSeconds)
    {
        var received = new UtcInstant(Start.Value.AddSeconds(receivedOffsetSeconds));
        var quote = new Quote(A, Start, received, new Price(100m), new Quantity(2m), new Price(101m), new Quantity(3m));
        var replayEvent = ReplayEvent.FromQuote(quote);

        var availability = ReplayEventAvailability.GetAvailabilityTime(replayEvent);

        Assert.Same(quote.EventTime, availability);
        Assert.Same(Start, replayEvent.ReplayTime);
        Assert.Same(received, quote.ReceivedTime);
        Assert.Equal(Math.Sign(receivedOffsetSeconds), quote.ReceivedTime.CompareTo(quote.EventTime));
        Assert.Same(quote, replayEvent.Quote);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public void TradeAvailabilityUsesEventTimeRegardlessOfReceivedTime(int receivedOffsetSeconds)
    {
        var received = new UtcInstant(Start.Value.AddSeconds(receivedOffsetSeconds));
        var trade = new Trade(A, Start, received, new Price(108m), new Quantity(4m));
        var replayEvent = ReplayEvent.FromTrade(trade);

        var availability = ReplayEventAvailability.GetAvailabilityTime(replayEvent);

        Assert.Same(trade.EventTime, availability);
        Assert.Same(Start, replayEvent.ReplayTime);
        Assert.Same(received, trade.ReceivedTime);
        Assert.Equal(Math.Sign(receivedOffsetSeconds), trade.ReceivedTime.CompareTo(trade.EventTime));
        Assert.Same(trade, replayEvent.Trade);
    }

    [Theory]
    [InlineData(600000000L)]
    [InlineData(3000000000L)]
    [InlineData(36000000000L)]
    [InlineData(17L)]
    public void CompleteCandleBecomesAvailableAtItsExactCloseForStandardAndCustomIntervals(long intervalTicks)
    {
        var interval = new CandleInterval(TimeSpan.FromTicks(intervalTicks));
        var candle = CreateCandle(Start, interval);
        var replayEvent = ReplayEvent.FromCandle(candle);
        var expectedClose = new UtcInstant(Start.Value.AddTicks(intervalTicks));

        var availability = ReplayEventAvailability.GetAvailabilityTime(replayEvent);

        Assert.Same(candle.OpenTime, replayEvent.ReplayTime);
        Assert.Equal(candle.CloseTime, availability);
        Assert.Equal(expectedClose, availability);
        Assert.True(availability.CompareTo(replayEvent.ReplayTime) > 0);
        Assert.NotEqual(new UtcInstant(expectedClose.Value.AddTicks(-1)), availability);
        Assert.NotEqual(new UtcInstant(expectedClose.Value.AddTicks(1)), availability);
        Assert.Same(candle, replayEvent.Candle);
        Assert.Same(interval, candle.Interval);
    }

    [Theory]
    [InlineData(ReplayEventKind.Quote)]
    [InlineData(ReplayEventKind.Trade)]
    [InlineData(ReplayEventKind.Candle)]
    public void CalculationPreservesEnvelopePayloadReferencesAndFinancialValues(ReplayEventKind kind)
    {
        var replayEvent = Create(kind);
        var quote = replayEvent.Quote;
        var trade = replayEvent.Trade;
        var candle = replayEvent.Candle;
        var occurrence = replayEvent.ReplayTime;
        var received = quote?.ReceivedTime ?? trade?.ReceivedTime;
        var interval = candle?.Interval;
        var closeTime = candle?.CloseTime;
        var id = replayEvent.InstrumentId;

        ReplayEventAvailability.GetAvailabilityTime(replayEvent);

        Assert.Equal(kind, replayEvent.Kind);
        Assert.Same(quote, replayEvent.Quote);
        Assert.Same(trade, replayEvent.Trade);
        Assert.Same(candle, replayEvent.Candle);
        Assert.Same(occurrence, replayEvent.ReplayTime);
        Assert.Same(received, replayEvent.Quote?.ReceivedTime ?? replayEvent.Trade?.ReceivedTime);
        Assert.Same(interval, replayEvent.Candle?.Interval);
        Assert.Equal(closeTime, replayEvent.Candle?.CloseTime);
        Assert.Same(id, replayEvent.InstrumentId);
        switch (kind)
        {
            case ReplayEventKind.Quote:
                AssertDecimal(10.0500m, quote!.BidPrice.Value);
                AssertDecimal(10.0500m, quote.AskPrice.Value);
                AssertDecimal(0.00100m, quote.BidQuantity.Value);
                AssertDecimal(0.00100m, quote.AskQuantity.Value);
                break;
            case ReplayEventKind.Trade:
                AssertDecimal(10.0500m, trade!.Price.Value);
                AssertDecimal(0.00100m, trade.Quantity.Value);
                break;
            case ReplayEventKind.Candle:
                AssertDecimal(100m, candle!.Open.Value);
                AssertDecimal(110m, candle.High.Value);
                AssertDecimal(95m, candle.Low.Value);
                AssertDecimal(108m, candle.Close.Value);
                AssertDecimal(5000m, candle.Volume.Value);
                break;
        }
    }

    [Theory]
    [InlineData(ReplayEventKind.Quote)]
    [InlineData(ReplayEventKind.Trade)]
    [InlineData(ReplayEventKind.Candle)]
    public void RepeatedCallsProduceTheSameInstantRegardlessOfOtherCalls(ReplayEventKind kind)
    {
        var replayEvent = Create(kind);
        var first = ReplayEventAvailability.GetAvailabilityTime(replayEvent);

        foreach (var otherKind in new[] { ReplayEventKind.Candle, ReplayEventKind.Quote, ReplayEventKind.Trade })
        {
            ReplayEventAvailability.GetAvailabilityTime(Create(otherKind));
            var repeated = ReplayEventAvailability.GetAvailabilityTime(replayEvent);
            Assert.Equal(first, repeated);
            Assert.Equal(first.Value.UtcTicks, repeated.Value.UtcTicks);
        }
    }

    [Fact]
    public void OccurrenceOrderCanDifferFromAvailabilityChronologyWithoutChangingTheDataset()
    {
        var open = new UtcInstant(new DateTimeOffset(2026, 10, 2, 10, 0, 0, TimeSpan.Zero));
        var tradeTime = new UtcInstant(open.Value.AddMinutes(3));
        var candle = ReplayEvent.FromCandle(CreateCandle(open, CandleInterval.FiveMinutes));
        var trade = ReplayEvent.FromTrade(new Trade(B, tradeTime, tradeTime, new Price(108m), new Quantity(4m)));
        var dataset = new HistoricalReplayDataset([trade, candle]);
        var events = dataset.Events;

        var candleAvailability = ReplayEventAvailability.GetAvailabilityTime(candle);
        var tradeAvailability = ReplayEventAvailability.GetAvailabilityTime(trade);

        Assert.Same(candle, dataset.Events[0]);
        Assert.Same(trade, dataset.Events[1]);
        Assert.True(candle.ReplayTime.CompareTo(trade.ReplayTime) < 0);
        Assert.True(tradeAvailability.CompareTo(candleAvailability) < 0);
        Assert.Equal(tradeTime, tradeAvailability);
        Assert.Equal(new UtcInstant(open.Value.AddMinutes(5)), candleAvailability);
        Assert.Same(open, candle.ReplayTime);
        Assert.Same(events, dataset.Events);
        Assert.Equal(2, dataset.Count);
        Assert.Same(open, dataset.StartTime);
        Assert.Same(tradeTime, dataset.EndTime);
        Assert.True(candleAvailability.CompareTo(dataset.EndTime) > 0);
    }

    [Theory]
    [InlineData(ReplayEventKind.Quote, false)]
    [InlineData(ReplayEventKind.Quote, true)]
    [InlineData(ReplayEventKind.Trade, false)]
    [InlineData(ReplayEventKind.Trade, true)]
    public void QuoteAndTradeAvailabilityAcceptsTheFullInstantRangeWithoutArithmetic(ReplayEventKind kind, bool maximum)
    {
        var time = new UtcInstant(maximum ? DateTimeOffset.MaxValue : DateTimeOffset.MinValue);
        var received = Start;
        var price = new Price(10m);
        var quantity = new Quantity(1m);
        var replayEvent = kind == ReplayEventKind.Quote
            ? ReplayEvent.FromQuote(new Quote(A, time, received, price, quantity, price, quantity))
            : ReplayEvent.FromTrade(new Trade(A, time, received, price, quantity));

        var availability = ReplayEventAvailability.GetAvailabilityTime(replayEvent);

        Assert.Same(time, availability);
        Assert.Same(time, replayEvent.ReplayTime);
        Assert.Same(received, replayEvent.Quote?.ReceivedTime ?? replayEvent.Trade?.ReceivedTime);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CandleAvailabilityUsesCanonicalCloseAtTemporalBoundaries(bool nearMaximum)
    {
        var interval = new CandleInterval(TimeSpan.FromTicks(1));
        var open = new UtcInstant(nearMaximum ? DateTimeOffset.MaxValue.AddTicks(-1) : DateTimeOffset.MinValue);
        var candle = CreateCandle(open, interval);
        var replayEvent = ReplayEvent.FromCandle(candle);
        var expected = nearMaximum ? DateTimeOffset.MaxValue : DateTimeOffset.MinValue.AddTicks(1);

        var availability = ReplayEventAvailability.GetAvailabilityTime(replayEvent);

        Assert.Equal(expected, availability.Value);
        Assert.Equal(candle.CloseTime, availability);
        Assert.Same(open, replayEvent.ReplayTime);
        Assert.True(availability.CompareTo(replayEvent.ReplayTime) > 0);
    }

    private static ReplayEvent Create(ReplayEventKind kind)
    {
        var received = new UtcInstant(Start.Value.AddSeconds(-1));
        var price = new Price(10.0500m);
        var quantity = new Quantity(0.00100m);
        return kind switch
        {
            ReplayEventKind.Quote => ReplayEvent.FromQuote(new Quote(A, Start, received, price, quantity, price, quantity)),
            ReplayEventKind.Trade => ReplayEvent.FromTrade(new Trade(A, Start, received, price, quantity)),
            _ => ReplayEvent.FromCandle(CreateCandle(Start, CandleInterval.OneMinute))
        };
    }

    private static Candle CreateCandle(UtcInstant openTime, CandleInterval interval) => new(
        A, interval, openTime, new Price(100m), new Price(110m), new Price(95m), new Price(108m), new Quantity(5000m));

    private static void AssertDecimal(decimal expected, decimal actual) => Assert.Equal(decimal.GetBits(expected), decimal.GetBits(actual));
}
