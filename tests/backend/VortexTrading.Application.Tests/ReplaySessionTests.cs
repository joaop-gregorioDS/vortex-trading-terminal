using VortexTrading.Application.MarketData.Replay;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Application.Tests;

public sealed class ReplaySessionTests
{
    private static readonly InstrumentId A = new(new Guid("11111111-1111-1111-1111-111111111111"));
    private static readonly InstrumentId B = new(new Guid("22222222-2222-2222-2222-222222222222"));
    private static readonly UtcInstant Start = new(new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero).AddTicks(7));

    [Fact]
    public void RejectsNullDataset()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new ReplaySession(null));

        Assert.Equal("dataset", exception.ParamName);
    }

    [Fact]
    public void StartsAtDatasetStartWithoutConsumingAnyOccurrence()
    {
        var dataset = CreateDataset();
        var session = new ReplaySession(dataset);

        Assert.Equal(0, session.Position);
        Assert.False(session.IsCompleted);
        Assert.Same(dataset.StartTime, session.CurrentTime);
    }

    [Fact]
    public void FirstCallReturnsTheOriginalFirstEventAfterUpdatingProgressAndTime()
    {
        var dataset = CreateDataset();
        var session = new ReplaySession(dataset);

        Assert.True(session.TryMoveNext(out var replayEvent));

        Assert.Same(dataset.Events[0], replayEvent);
        Assert.Equal(1, session.Position);
        Assert.Equal(replayEvent.ReplayTime, session.CurrentTime);
        Assert.False(session.IsCompleted);
    }

    [Fact]
    public void DeliversTheWholeHeterogeneousDatasetWithCoherentTimeAtEveryStep()
    {
        var dataset = CreateDataset();
        var session = new ReplaySession(dataset);
        var expectedKinds = new[]
        {
            ReplayEventKind.Quote, ReplayEventKind.Trade, ReplayEventKind.Candle,
            ReplayEventKind.Quote, ReplayEventKind.Trade
        };

        for (var index = 0; index < dataset.Count; index++)
        {
            Assert.True(session.TryMoveNext(out var replayEvent));
            Assert.Same(dataset.Events[index], replayEvent);
            Assert.Equal(expectedKinds[index], replayEvent.Kind);
            Assert.Equal(replayEvent.ReplayTime, session.CurrentTime);
            Assert.Equal(index + 1, session.Position);
            Assert.Equal(index + 1 == dataset.Count, session.IsCompleted);
        }

        Assert.False(session.TryMoveNext(out var afterEnd));
        Assert.Null(afterEnd);
        Assert.Equal(dataset.Count, session.Position);
        Assert.True(session.IsCompleted);
        Assert.Equal(dataset.EndTime, session.CurrentTime);
    }

    [Fact]
    public void SameTimestampOccurrencesAdvancePositionWithoutAdvancingTime()
    {
        var quote = Create(ReplayEventKind.Quote, A, Start);
        var trade = Create(ReplayEventKind.Trade, A, new UtcInstant(Start.Value));
        var candle = Create(ReplayEventKind.Candle, A, new UtcInstant(Start.Value));
        var dataset = new HistoricalReplayDataset([candle, trade, quote]);
        var session = new ReplaySession(dataset);

        foreach (var expected in new[] { quote, trade, candle })
        {
            var previousTime = session.CurrentTime;
            var previousPosition = session.Position;
            Assert.True(session.TryMoveNext(out var replayEvent));
            Assert.Same(expected, replayEvent);
            Assert.Same(previousTime, session.CurrentTime);
            Assert.Equal(replayEvent.ReplayTime, session.CurrentTime);
            Assert.Equal(previousPosition + 1, session.Position);
        }

        Assert.True(session.IsCompleted);
    }

    [Theory]
    [InlineData(ReplayEventKind.Quote)]
    [InlineData(ReplayEventKind.Trade)]
    [InlineData(ReplayEventKind.Candle)]
    public void DeliversRepeatedEnvelopeAndPayloadOccurrencesWithoutDeduplication(ReplayEventKind kind)
    {
        var first = Create(kind, A, Start);
        var second = kind switch
        {
            ReplayEventKind.Quote => ReplayEvent.FromQuote(first.Quote),
            ReplayEventKind.Trade => ReplayEvent.FromTrade(first.Trade),
            _ => ReplayEvent.FromCandle(first.Candle)
        };
        var session = new ReplaySession(new HistoricalReplayDataset([first, second, first]));

        foreach (var expected in new[] { first, second, first })
        {
            Assert.True(session.TryMoveNext(out var replayEvent));
            Assert.Same(expected, replayEvent);
            Assert.Same(Payload(first), Payload(replayEvent));
        }

        Assert.Equal(3, session.Position);
        Assert.True(session.IsCompleted);
    }

    [Fact]
    public void PreservesStructurallyEqualButDistinctPayloadsAsSeparateOccurrences()
    {
        var first = Create(ReplayEventKind.Quote, A, Start);
        var second = Create(ReplayEventKind.Quote, A, Start);
        Assert.Equal(first.Quote, second.Quote);
        Assert.NotSame(first.Quote, second.Quote);
        var session = new ReplaySession(new HistoricalReplayDataset([first, second]));

        Assert.True(session.TryMoveNext(out var firstDelivered));
        Assert.Same(first, firstDelivered);
        Assert.True(session.TryMoveNext(out var secondDelivered));
        Assert.Same(second, secondDelivered);
        Assert.Equal(2, session.Position);
    }

    [Theory]
    [InlineData(ReplayEventKind.Quote)]
    [InlineData(ReplayEventKind.Trade)]
    [InlineData(ReplayEventKind.Candle)]
    public void CompletedSingleEventSessionsRemainStableOnAdditionalCalls(ReplayEventKind kind)
    {
        var only = Create(kind, A, Start);
        var dataset = new HistoricalReplayDataset([only]);
        var session = new ReplaySession(dataset);
        Assert.False(session.IsCompleted);
        Assert.True(session.TryMoveNext(out var delivered));
        Assert.Same(only, delivered);
        Assert.True(session.IsCompleted);
        var finalTime = session.CurrentTime;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            Assert.False(session.TryMoveNext(out var replayEvent));
            Assert.Null(replayEvent);
            Assert.Equal(1, session.Position);
            Assert.Same(finalTime, session.CurrentTime);
            Assert.Equal(dataset.EndTime, session.CurrentTime);
            Assert.True(session.IsCompleted);
        }
    }

    [Fact]
    public void SessionsOverTheSameDatasetHaveIndependentInterleavedProgress()
    {
        var dataset = CreateDataset();
        var first = new ReplaySession(dataset);
        var second = new ReplaySession(dataset);
        for (var index = 0; index < 3; index++) Assert.True(first.TryMoveNext(out _));

        Assert.Equal(3, first.Position);
        Assert.Equal(dataset.Events[2].ReplayTime, first.CurrentTime);
        Assert.Equal(0, second.Position);
        Assert.Same(dataset.StartTime, second.CurrentTime);
        Assert.False(second.IsCompleted);
        Assert.True(second.TryMoveNext(out var secondEvent));
        Assert.Same(dataset.Events[0], secondEvent);
        Assert.Equal(3, first.Position);
        Assert.Equal(dataset.Events[2].ReplayTime, first.CurrentTime);
        Assert.True(first.TryMoveNext(out var firstEvent));
        Assert.Same(dataset.Events[3], firstEvent);
        Assert.Equal(1, second.Position);
        Assert.Equal(dataset.StartTime, second.CurrentTime);
    }

    [Fact]
    public void NewSessionRestartsAtTheBeginningAfterAnotherSessionCompletes()
    {
        var dataset = CreateDataset();
        var completed = new ReplaySession(dataset);
        while (completed.TryMoveNext(out _)) { }

        var restarted = new ReplaySession(dataset);

        Assert.Equal(0, restarted.Position);
        Assert.False(restarted.IsCompleted);
        Assert.Same(dataset.StartTime, restarted.CurrentTime);
        Assert.True(restarted.TryMoveNext(out var first));
        Assert.Same(dataset.Events[0], first);
        Assert.Equal(dataset.Count, completed.Position);
        Assert.Equal(dataset.EndTime, completed.CurrentTime);
        Assert.True(completed.IsCompleted);
    }

    [Fact]
    public void ConsumptionDoesNotModifyDatasetEventsOrMetadata()
    {
        var dataset = CreateDataset();
        var events = dataset.Events;
        var snapshot = events.ToArray();
        var count = dataset.Count;
        var start = dataset.StartTime;
        var end = dataset.EndTime;
        var session = new ReplaySession(dataset);
        while (session.TryMoveNext(out _)) { }

        Assert.Same(events, dataset.Events);
        Assert.Equal(count, dataset.Count);
        Assert.Same(start, dataset.StartTime);
        Assert.Same(end, dataset.EndTime);
        for (var index = 0; index < count; index++) Assert.Same(snapshot[index], dataset.Events[index]);
    }

    [Fact]
    public void PreservesNontrivialCanonicalTimeInstrumentKindAndOrdinalOrder()
    {
        var laterQuoteA = Create(ReplayEventKind.Quote, A, At(1));
        var candleB = Create(ReplayEventKind.Candle, B, Start);
        var tradeA = Create(ReplayEventKind.Trade, A, Start);
        var quoteB = Create(ReplayEventKind.Quote, B, Start);
        var firstQuoteA = Create(ReplayEventKind.Quote, A, Start, 20m);
        var candleA = Create(ReplayEventKind.Candle, A, Start);
        var secondQuoteA = Create(ReplayEventKind.Quote, A, Start, 10m);
        var earlierCandleB = Create(ReplayEventKind.Candle, B, At(-1));
        var dataset = new HistoricalReplayDataset(
            [laterQuoteA, candleB, tradeA, quoteB, firstQuoteA, candleA, secondQuoteA, earlierCandleB]);
        var expected = new[] { earlierCandleB, firstQuoteA, secondQuoteA, tradeA, candleA, quoteB, candleB, laterQuoteA };
        var session = new ReplaySession(dataset);

        for (var index = 0; index < expected.Length; index++)
        {
            Assert.True(session.TryMoveNext(out var replayEvent));
            Assert.Same(expected[index], replayEvent);
            Assert.Same(dataset.Events[index], replayEvent);
            Assert.Equal(replayEvent.ReplayTime, session.CurrentTime);
        }

        Assert.True(session.IsCompleted);
    }

    [Fact]
    public void AdvancesAcrossExtremeInstantsAndAdjacentTicksWithoutTimeArithmetic()
    {
        var minimum = new UtcInstant(DateTimeOffset.MinValue);
        var afterMinimum = new UtcInstant(DateTimeOffset.MinValue.AddTicks(1));
        var beforeMaximum = new UtcInstant(DateTimeOffset.MaxValue.AddTicks(-1));
        var maximum = new UtcInstant(DateTimeOffset.MaxValue);
        var expected = new[]
        {
            Create(ReplayEventKind.Trade, A, minimum), Create(ReplayEventKind.Trade, B, afterMinimum),
            Create(ReplayEventKind.Quote, A, beforeMaximum), Create(ReplayEventKind.Quote, B, maximum)
        };
        var dataset = new HistoricalReplayDataset([expected[3], expected[1], expected[2], expected[0]]);
        var session = new ReplaySession(dataset);

        foreach (var next in expected)
        {
            Assert.True(session.TryMoveNext(out var replayEvent));
            Assert.Same(next, replayEvent);
            Assert.Equal(next.ReplayTime, session.CurrentTime);
        }

        Assert.Equal(4, session.Position);
        Assert.True(session.IsCompleted);
        Assert.Same(maximum, session.CurrentTime);
    }

    [Theory]
    [InlineData(ReplayEventKind.Quote)]
    [InlineData(ReplayEventKind.Trade)]
    [InlineData(ReplayEventKind.Candle)]
    public void PreservesOriginalPayloadsTimestampsAndFinancialValues(ReplayEventKind kind)
    {
        var original = Create(kind, A, Start);
        var payload = Payload(original);
        var replayTime = original.ReplayTime;
        var received = original.Quote?.ReceivedTime ?? original.Trade?.ReceivedTime;
        var session = new ReplaySession(new HistoricalReplayDataset([original]));

        Assert.True(session.TryMoveNext(out var delivered));

        Assert.Same(original, delivered);
        Assert.Same(payload, Payload(delivered));
        Assert.Same(replayTime, delivered.ReplayTime);
        Assert.Same(received, delivered.Quote?.ReceivedTime ?? delivered.Trade?.ReceivedTime);
        if (received is not null) Assert.NotEqual(replayTime, received);
        decimal[] values = payload switch
        {
            Quote quote => [quote.BidPrice.Value, quote.AskPrice.Value, quote.BidQuantity.Value, quote.AskQuantity.Value],
            Trade trade => [trade.Price.Value, trade.Quantity.Value],
            Candle candle => [candle.Open.Value, candle.High.Value, candle.Low.Value, candle.Close.Value, candle.Volume.Value],
            _ => throw new InvalidOperationException()
        };
        Assert.All(values, value => Assert.Equal(decimal.GetBits(10.0500m), decimal.GetBits(value)));
        if (delivered.Candle is not null)
        {
            Assert.True(delivered.Candle.CloseTime.CompareTo(session.CurrentTime) > 0);
            Assert.Equal(delivered.Candle.OpenTime, session.CurrentTime);
        }
    }

    [Fact]
    public void ObservingStateDoesNotConsumeEventsOrAnticipateTheNextTimestamp()
    {
        var dataset = CreateDataset();
        var session = new ReplaySession(dataset);
        Assert.True(session.TryMoveNext(out var first));
        Assert.True(session.TryMoveNext(out var second));
        Assert.True(second.ReplayTime.CompareTo(first.ReplayTime) > 0);

        for (var observation = 0; observation < 3; observation++)
        {
            Assert.Equal(2, session.Position);
            Assert.False(session.IsCompleted);
            Assert.Equal(second.ReplayTime, session.CurrentTime);
            Assert.True(dataset.Events[2].ReplayTime.CompareTo(session.CurrentTime) > 0);
        }

        Assert.True(session.TryMoveNext(out var third));
        Assert.Same(dataset.Events[2], third);
        Assert.Equal(third.ReplayTime, session.CurrentTime);
        Assert.Equal(3, session.Position);
    }

    private static HistoricalReplayDataset CreateDataset() => new(
    [
        Create(ReplayEventKind.Trade, B, At(4)), Create(ReplayEventKind.Quote, A, At(3)),
        Create(ReplayEventKind.Candle, B, At(2)), Create(ReplayEventKind.Trade, A, At(1)),
        Create(ReplayEventKind.Quote, B, Start)
    ]);

    private static UtcInstant At(int ticks) => new(Start.Value.AddTicks(ticks));

    private static ReplayEvent Create(ReplayEventKind kind, InstrumentId id, UtcInstant time, decimal value = 10.0500m)
    {
        var price = new Price(value);
        var quantity = new Quantity(value);
        // Fixed and distinct from EventTime, including for temporal boundary fixtures.
        var received = new UtcInstant(Start.Value.AddHours(-1));
        return kind switch
        {
            ReplayEventKind.Quote => ReplayEvent.FromQuote(new Quote(id, time, received, price, quantity, price, quantity)),
            ReplayEventKind.Trade => ReplayEvent.FromTrade(new Trade(id, time, received, price, quantity)),
            _ => ReplayEvent.FromCandle(new Candle(id, CandleInterval.OneMinute, time, price, price, price, price, quantity))
        };
    }

    private static object Payload(ReplayEvent replayEvent) => replayEvent.Kind switch
    {
        ReplayEventKind.Quote => replayEvent.Quote!,
        ReplayEventKind.Trade => replayEvent.Trade!,
        _ => replayEvent.Candle!
    };
}
