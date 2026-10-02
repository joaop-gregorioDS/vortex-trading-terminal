using VortexTrading.Application.MarketData.Replay;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Application.Tests;

public sealed class HistoricalReplayDatasetTests
{
    private static readonly InstrumentId A = new(new Guid("11111111-1111-1111-1111-111111111111"));
    private static readonly InstrumentId B = new(new Guid("22222222-2222-2222-2222-222222222222"));
    private static readonly UtcInstant Time = new(new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero).AddTicks(7));

    [Fact]
    public void RejectsNullCollection()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new HistoricalReplayDataset(null));
        Assert.Equal("events", exception.ParamName);
    }

    [Fact]
    public void RejectsEmptyDataset()
    {
        var exception = Assert.Throws<ArgumentException>(() => new HistoricalReplayDataset([]));
        Assert.Equal("events", exception.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void RejectsNullEventAtAnyPosition(int position)
    {
        var events = new[] { Create(ReplayEventKind.Quote, A, Time), Create(ReplayEventKind.Trade, A, Time), Create(ReplayEventKind.Candle, A, Time) };
        events[position] = null!;

        var exception = Assert.Throws<ArgumentException>(() => new HistoricalReplayDataset(events));
        Assert.Equal("events", exception.ParamName);
    }

    [Fact]
    public void SnapshotsWithoutMutatingTheCallerAndPreservesMetadataAfterCallerChanges()
    {
        var late = Create(ReplayEventKind.Trade, B, new UtcInstant(Time.Value.AddTicks(1)));
        var early = Create(ReplayEventKind.Quote, A, Time);
        var original = new List<ReplayEvent> { late, early };
        var dataset = new HistoricalReplayDataset(original);

        Assert.Equal(new[] { late, early }, original);
        Assert.NotSame(original, dataset.Events);
        original.Clear();
        original.Add(Create(ReplayEventKind.Quote, B, new UtcInstant(Time.Value.AddDays(-1))));

        Assert.Equal(2, dataset.Count);
        Assert.Equal(new[] { early, late }, dataset.Events);
        Assert.Same(early.ReplayTime, dataset.StartTime);
        Assert.Same(late.ReplayTime, dataset.EndTime);
        Assert.Same(early, dataset.Events[0]);
        Assert.Same(late, dataset.Events[1]);
    }

    [Fact]
    public void CanonicalOrderUsesTimeThenInstrumentThenKindThenOriginalOrdinal()
    {
        var laterQuoteA = Create(ReplayEventKind.Quote, A, new UtcInstant(Time.Value.AddTicks(1)));
        var candleB = Create(ReplayEventKind.Candle, B, Time);
        var tradeA = Create(ReplayEventKind.Trade, A, Time);
        var quoteB = Create(ReplayEventKind.Quote, B, Time);
        var firstQuoteA = Create(ReplayEventKind.Quote, A, Time, 20m);
        var candleA = Create(ReplayEventKind.Candle, A, Time);
        var secondQuoteA = Create(ReplayEventKind.Quote, A, Time, 10m);
        var earlierCandleB = Create(ReplayEventKind.Candle, B, new UtcInstant(Time.Value.AddTicks(-1)));
        var input = new[] { laterQuoteA, candleB, tradeA, quoteB, firstQuoteA, candleA, secondQuoteA, earlierCandleB };

        var dataset = new HistoricalReplayDataset(input);

        Assert.Equal(new[] { earlierCandleB, firstQuoteA, secondQuoteA, tradeA, candleA, quoteB, candleB, laterQuoteA }, dataset.Events);
        Assert.Equal(8, dataset.Count);
        Assert.Same(earlierCandleB.ReplayTime, dataset.StartTime);
        Assert.Same(laterQuoteA.ReplayTime, dataset.EndTime);
        Assert.Equal(new[] { laterQuoteA, candleB, tradeA, quoteB, firstQuoteA, candleA, secondQuoteA, earlierCandleB }, input);
    }

    [Fact]
    public void IdenticalInputProducesIdenticalCanonicalOrderAcrossDatasets()
    {
        var input = new[]
        {
            Create(ReplayEventKind.Candle, B, Time),
            Create(ReplayEventKind.Quote, A, Time, 20m),
            Create(ReplayEventKind.Trade, B, new UtcInstant(Time.Value.AddTicks(-1))),
            Create(ReplayEventKind.Quote, A, Time, 10m)
        };
        var first = new HistoricalReplayDataset(input);
        var second = new HistoricalReplayDataset(input);

        Assert.NotSame(first.Events, second.Events);
        Assert.Equal(first.Events, second.Events);
        for (var index = 0; index < first.Count; index++) Assert.Same(first.Events[index], second.Events[index]);
    }

    [Fact]
    public void FinalTieUsesInputOrdinalRatherThanFinancialValuesOrReceivedTime()
    {
        var price = new Price(20m);
        var first = ReplayEvent.FromTrade(new Trade(A, Time, new UtcInstant(Time.Value.AddHours(1)), price, new Quantity(2m)));
        var second = ReplayEvent.FromTrade(new Trade(A, Time, new UtcInstant(Time.Value.AddHours(-1)), new Price(10m), new Quantity(1m)));

        Assert.Equal(new[] { first, second }, new HistoricalReplayDataset([first, second]).Events);
        Assert.Equal(new[] { second, first }, new HistoricalReplayDataset([second, first]).Events);
    }

    [Theory]
    [InlineData(ReplayEventKind.Quote)]
    [InlineData(ReplayEventKind.Trade)]
    [InlineData(ReplayEventKind.Candle)]
    public void RetainsRepeatedPayloadAndEnvelopeOccurrences(ReplayEventKind kind)
    {
        var first = Create(kind, A, Time);
        var second = kind switch
        {
            ReplayEventKind.Quote => ReplayEvent.FromQuote(first.Quote),
            ReplayEventKind.Trade => ReplayEvent.FromTrade(first.Trade),
            _ => ReplayEvent.FromCandle(first.Candle)
        };
        var dataset = new HistoricalReplayDataset([first, second, first]);

        Assert.Equal(3, dataset.Count);
        Assert.Same(first, dataset.Events[0]);
        Assert.Same(second, dataset.Events[1]);
        Assert.Same(first, dataset.Events[2]);
        Assert.Same(first.Quote, second.Quote);
        Assert.Same(first.Trade, second.Trade);
        Assert.Same(first.Candle, second.Candle);
    }

    [Fact]
    public void RetainsStructurallyEqualButDistinctPayloadInstances()
    {
        var first = Create(ReplayEventKind.Quote, A, Time);
        var second = Create(ReplayEventKind.Quote, A, Time);
        Assert.NotSame(first.Quote, second.Quote);
        Assert.Equal(first.Quote, second.Quote);

        var dataset = new HistoricalReplayDataset([first, second]);

        Assert.Equal(2, dataset.Count);
        Assert.Same(first.Quote, dataset.Events[0].Quote);
        Assert.Same(second.Quote, dataset.Events[1].Quote);
    }

    [Fact]
    public void ExposedEventsCannotBeReplacedAddedRemovedOrCleared()
    {
        var replayEvent = Create(ReplayEventKind.Quote, A, Time);
        var dataset = new HistoricalReplayDataset([replayEvent]);
        var events = Assert.IsAssignableFrom<IList<ReplayEvent>>(dataset.Events);

        Assert.True(events.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => events[0] = Create(ReplayEventKind.Trade, B, Time));
        Assert.Throws<NotSupportedException>(() => events.Add(replayEvent));
        Assert.Throws<NotSupportedException>(() => events.RemoveAt(0));
        Assert.Throws<NotSupportedException>(() => events.Clear());
        Assert.Equal(1, dataset.Count);
        Assert.Same(replayEvent, dataset.Events[0]);
        Assert.Same(Time, dataset.StartTime);
        Assert.Same(Time, dataset.EndTime);
    }

    [Theory]
    [InlineData(ReplayEventKind.Quote)]
    [InlineData(ReplayEventKind.Trade)]
    [InlineData(ReplayEventKind.Candle)]
    public void SingleEventHasEqualStartAndEndReplayTimes(ReplayEventKind kind)
    {
        var replayEvent = Create(kind, A, Time);
        var dataset = new HistoricalReplayDataset([replayEvent]);

        Assert.Equal(1, dataset.Count);
        Assert.Same(replayEvent.ReplayTime, dataset.StartTime);
        Assert.Same(replayEvent.ReplayTime, dataset.EndTime);
        if (kind == ReplayEventKind.Candle) Assert.NotEqual(replayEvent.Candle!.CloseTime, dataset.EndTime);
    }

    [Fact]
    public void PreservesTickPrecisionAndAcceptsTheFullInstantRange()
    {
        var minimum = Create(ReplayEventKind.Trade, B, new UtcInstant(DateTimeOffset.MinValue));
        var maximum = Create(ReplayEventKind.Quote, A, new UtcInstant(DateTimeOffset.MaxValue));
        var oneTickLater = Create(ReplayEventKind.Trade, A, new UtcInstant(Time.Value.AddTicks(1)));
        var atTime = Create(ReplayEventKind.Quote, B, Time);

        var dataset = new HistoricalReplayDataset([maximum, oneTickLater, atTime, minimum]);

        Assert.Equal(new[] { minimum, atTime, oneTickLater, maximum }, dataset.Events);
        Assert.Equal(DateTimeOffset.MinValue, dataset.StartTime.Value);
        Assert.Equal(DateTimeOffset.MaxValue, dataset.EndTime.Value);
        Assert.Equal(1, oneTickLater.ReplayTime.Value.UtcTicks - atTime.ReplayTime.Value.UtcTicks);
    }

    private static ReplayEvent Create(ReplayEventKind kind, InstrumentId id, UtcInstant time, decimal value = 10.0500m)
    {
        var price = new Price(value);
        var quantity = new Quantity(0.00100m);
        return kind switch
        {
            ReplayEventKind.Quote => ReplayEvent.FromQuote(new Quote(id, time, time, price, quantity, price, quantity)),
            ReplayEventKind.Trade => ReplayEvent.FromTrade(new Trade(id, time, time, price, quantity)),
            ReplayEventKind.Candle => ReplayEvent.FromCandle(new Candle(id, CandleInterval.OneMinute, time, price, price, price, price, quantity)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }
}
