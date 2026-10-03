using VortexTrading.Application.MarketData.Replay;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Application.Tests;

public sealed class CausalReplaySessionTests
{
    private static readonly InstrumentId A = new(new Guid("11111111-1111-1111-1111-111111111111"));
    private static readonly InstrumentId B = new(new Guid("22222222-2222-2222-2222-222222222222"));
    private static readonly InstrumentId C = new(new Guid("33333333-3333-3333-3333-333333333333"));
    private static readonly UtcInstant Start = new(new DateTimeOffset(2026, 10, 2, 10, 0, 0, TimeSpan.Zero));

    [Fact]
    public void RejectsNullDataset()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new CausalReplaySession(null));

        Assert.Equal("dataset", exception.ParamName);
    }

    [Fact]
    public void StartsAtMinimumAvailabilityWithoutConsumingAnOccurrence()
    {
        var session = new CausalReplaySession(CreateDataset());

        Assert.Equal(0, session.Position);
        Assert.False(session.IsCompleted);
        Assert.Equal(At(3), session.CurrentTime);
        Assert.Equal(At(5), session.EndTime);
    }

    [Fact]
    public void InitialTimeCanDifferFromDatasetOccurrenceStart()
    {
        var dataset = CreateDataset();
        var session = new CausalReplaySession(dataset);

        Assert.Equal(Start, dataset.StartTime);
        Assert.Equal(At(3), session.CurrentTime);
        Assert.True(session.CurrentTime.CompareTo(dataset.StartTime) > 0);
    }

    [Fact]
    public void CausalHorizonCanExtendBeyondDatasetOccurrenceEnd()
    {
        var dataset = CreateDataset();
        var session = new CausalReplaySession(dataset);

        Assert.Equal(At(4), dataset.EndTime);
        Assert.Equal(At(5), session.EndTime);
        Assert.True(session.EndTime.CompareTo(dataset.EndTime) > 0);
        while (session.TryMoveNext(out _)) { }
        Assert.Equal(At(5), session.CurrentTime);
        Assert.True(session.IsCompleted);
    }

    [Fact]
    public void ReordersCandleTradeQuoteByAvailabilityAndConsumesEveryOriginalOccurrence()
    {
        var dataset = CreateDataset();
        Assert.Equal(new[] { ReplayEventKind.Candle, ReplayEventKind.Trade, ReplayEventKind.Quote },
            dataset.Events.Select(replayEvent => replayEvent.Kind).ToArray());
        var expected = new[] { dataset.Events[1], dataset.Events[2], dataset.Events[0] };
        var times = new[] { At(3), At(4), At(5) };
        var session = new CausalReplaySession(dataset);

        for (var index = 0; index < expected.Length; index++)
        {
            var previous = session.CurrentTime;
            Assert.True(session.TryMoveNext(out var delivered));
            Assert.Same(expected[index], delivered);
            Assert.Equal(times[index], session.CurrentTime);
            Assert.True(session.CurrentTime.CompareTo(previous) >= 0);
            Assert.Equal(index + 1, session.Position);
            Assert.Equal(index + 1 == dataset.Count, session.IsCompleted);
        }

        Assert.Equal(dataset.Count, session.Position);
        Assert.Equal(session.EndTime, session.CurrentTime);
        Assert.False(session.TryMoveNext(out var afterEnd));
        Assert.Null(afterEnd);
    }

    [Fact]
    public void AvailabilityTiesFollowCanonicalDatasetOrdinalsRatherThanInputOrder()
    {
        var dataset = CreateTiedDataset();
        var session = new CausalReplaySession(dataset);

        Assert.Equal(new[] { ReplayEventKind.Candle, ReplayEventKind.Quote, ReplayEventKind.Trade },
            dataset.Events.Select(replayEvent => replayEvent.Kind).ToArray());
        foreach (var expected in dataset.Events)
        {
            Assert.True(session.TryMoveNext(out var delivered));
            Assert.Same(expected, delivered);
            Assert.Equal(At(5), session.CurrentTime);
        }
    }

    [Fact]
    public void KindPriorityDoesNotOverrideTheCanonicalOrdinalAtEqualAvailability()
    {
        var dataset = CreateTiedDataset();
        var enumOrder = dataset.Events.OrderBy(replayEvent => replayEvent.Kind).ToArray();
        Assert.NotSame(dataset.Events[0], enumOrder[0]);
        var session = new CausalReplaySession(dataset);

        Assert.True(session.TryMoveNext(out var first));
        Assert.Same(dataset.Events[0], first);
        Assert.Equal(ReplayEventKind.Candle, first.Kind);
        Assert.True(session.TryMoveNext(out var second));
        Assert.Same(dataset.Events[1], second);
        Assert.Equal(ReplayEventKind.Quote, second.Kind);
    }

    [Fact]
    public void InstrumentPriorityDoesNotOverrideTheCanonicalOrdinalAtEqualAvailability()
    {
        var dataset = CreateTiedDataset();
        Assert.Equal(new[] { C, A, B }, dataset.Events.Select(replayEvent => replayEvent.InstrumentId).ToArray());
        var session = new CausalReplaySession(dataset);

        foreach (var expectedId in new[] { C, A, B })
        {
            Assert.True(session.TryMoveNext(out var delivered));
            Assert.Equal(expectedId, delivered.InstrumentId);
            Assert.Equal(At(5), session.CurrentTime);
        }
    }

    [Fact]
    public void EqualAvailabilityAdvancesPositionOncePerOccurrenceWithoutAdvancingTime()
    {
        var session = new CausalReplaySession(CreateTiedDataset());
        var initial = session.CurrentTime;

        for (var position = 1; position <= 3; position++)
        {
            Assert.True(session.TryMoveNext(out _));
            Assert.Same(initial, session.CurrentTime);
            Assert.Equal(At(5), session.CurrentTime);
            Assert.Equal(position, session.Position);
            Assert.Equal(position == 3, session.IsCompleted);
        }
    }

    [Theory]
    [InlineData(ReplayEventKind.Quote)]
    [InlineData(ReplayEventKind.Trade)]
    [InlineData(ReplayEventKind.Candle)]
    public void PreservesRepeatedEventInstancesAndDifferentEnvelopesSharingOnePayload(ReplayEventKind kind)
    {
        var first = Create(kind, A, Start);
        var second = kind switch
        {
            ReplayEventKind.Quote => ReplayEvent.FromQuote(first.Quote),
            ReplayEventKind.Trade => ReplayEvent.FromTrade(first.Trade),
            _ => ReplayEvent.FromCandle(first.Candle)
        };
        var session = new CausalReplaySession(new HistoricalReplayDataset([first, second, first]));

        foreach (var expected in new[] { first, second, first })
        {
            Assert.True(session.TryMoveNext(out var delivered));
            Assert.Same(expected, delivered);
            Assert.Same(Payload(first), Payload(delivered));
        }

        Assert.Equal(3, session.Position);
        Assert.True(session.IsCompleted);
    }

    [Theory]
    [InlineData(ReplayEventKind.Quote)]
    [InlineData(ReplayEventKind.Trade)]
    [InlineData(ReplayEventKind.Candle)]
    public void PreservesStructurallyEqualDistinctPayloadsAsSeparateOccurrences(ReplayEventKind kind)
    {
        var first = Create(kind, A, Start);
        var second = Create(kind, A, Start);
        Assert.Equal(Payload(first), Payload(second));
        Assert.NotSame(Payload(first), Payload(second));
        var session = new CausalReplaySession(new HistoricalReplayDataset([first, second]));

        Assert.True(session.TryMoveNext(out var deliveredFirst));
        Assert.Same(first, deliveredFirst);
        Assert.True(session.TryMoveNext(out var deliveredSecond));
        Assert.Same(second, deliveredSecond);
        Assert.Equal(2, session.Position);
        Assert.True(session.IsCompleted);
    }

    [Theory]
    [InlineData(ReplayEventKind.Quote)]
    [InlineData(ReplayEventKind.Trade)]
    [InlineData(ReplayEventKind.Candle)]
    public void PreservesPayloadIdentityTimestampsAndExactFinancialValues(ReplayEventKind kind)
    {
        var original = Create(kind, A, Start);
        var payload = Payload(original);
        var received = original.Quote?.ReceivedTime ?? original.Trade?.ReceivedTime;
        var availability = kind == ReplayEventKind.Candle ? At(5) : Start;
        var session = new CausalReplaySession(new HistoricalReplayDataset([original]));

        Assert.True(session.TryMoveNext(out var delivered));

        Assert.Same(original, delivered);
        Assert.Same(payload, Payload(delivered));
        Assert.Same(Start, delivered.ReplayTime);
        Assert.Same(received, delivered.Quote?.ReceivedTime ?? delivered.Trade?.ReceivedTime);
        Assert.Equal(availability, session.CurrentTime);
        if (received is not null) Assert.NotEqual(received, session.CurrentTime);
        decimal[] values = payload switch
        {
            Quote quote => [quote.BidPrice.Value, quote.AskPrice.Value, quote.BidQuantity.Value, quote.AskQuantity.Value],
            Trade trade => [trade.Price.Value, trade.Quantity.Value],
            Candle candle => [candle.Open.Value, candle.High.Value, candle.Low.Value, candle.Close.Value, candle.Volume.Value],
            _ => throw new InvalidOperationException()
        };
        Assert.All(values, value => Assert.Equal(decimal.GetBits(10.0500m), decimal.GetBits(value)));
    }

    [Theory]
    [InlineData(ReplayEventKind.Quote)]
    [InlineData(ReplayEventKind.Trade)]
    [InlineData(ReplayEventKind.Candle)]
    public void CompletedSingleOccurrenceSessionsRemainStableOnRepeatedCalls(ReplayEventKind kind)
    {
        var only = Create(kind, A, Start);
        var session = new CausalReplaySession(new HistoricalReplayDataset([only]));
        Assert.Equal(session.EndTime, session.CurrentTime);
        Assert.Equal(0, session.Position);
        Assert.False(session.IsCompleted);
        Assert.True(session.TryMoveNext(out var delivered));
        Assert.Same(only, delivered);
        var current = session.CurrentTime;
        var end = session.EndTime;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            Assert.False(session.TryMoveNext(out var afterEnd));
            Assert.Null(afterEnd);
            Assert.Equal(1, session.Position);
            Assert.Same(current, session.CurrentTime);
            Assert.Same(end, session.EndTime);
            Assert.Equal(end, session.CurrentTime);
            Assert.True(session.IsCompleted);
        }
    }

    [Fact]
    public void SessionsOverOneDatasetHaveIndependentInterleavedProgress()
    {
        var dataset = CreateDataset();
        var first = new CausalReplaySession(dataset);
        var second = new CausalReplaySession(dataset);
        Assert.True(first.TryMoveNext(out _));
        Assert.True(first.TryMoveNext(out _));

        Assert.Equal(2, first.Position);
        Assert.Equal(At(4), first.CurrentTime);
        Assert.Equal(0, second.Position);
        Assert.Equal(At(3), second.CurrentTime);
        Assert.False(second.IsCompleted);
        Assert.True(second.TryMoveNext(out var secondEvent));
        Assert.Same(dataset.Events[1], secondEvent);
        Assert.Equal(2, first.Position);
        Assert.Equal(At(4), first.CurrentTime);
        Assert.True(first.TryMoveNext(out var firstEvent));
        Assert.Same(dataset.Events[0], firstEvent);
        Assert.True(first.IsCompleted);
        Assert.Equal(1, second.Position);
        Assert.Equal(At(3), second.CurrentTime);
        Assert.False(second.IsCompleted);
    }

    [Fact]
    public void NewSessionRestartsDeterministicallyAfterAnotherSessionCompletes()
    {
        var dataset = CreateDataset();
        var first = new CausalReplaySession(dataset);
        var firstEvents = new List<ReplayEvent>();
        var firstTimes = new List<UtcInstant>();
        while (first.TryMoveNext(out var delivered))
        {
            firstEvents.Add(delivered);
            firstTimes.Add(first.CurrentTime);
        }

        var restarted = new CausalReplaySession(dataset);
        Assert.Equal(0, restarted.Position);
        Assert.False(restarted.IsCompleted);
        Assert.Equal(At(3), restarted.CurrentTime);
        for (var index = 0; index < firstEvents.Count; index++)
        {
            Assert.True(restarted.TryMoveNext(out var delivered));
            Assert.Same(firstEvents[index], delivered);
            Assert.Equal(firstTimes[index], restarted.CurrentTime);
        }

        Assert.True(restarted.IsCompleted);
        Assert.Equal(first.EndTime, restarted.EndTime);
        Assert.Equal(dataset.Count, first.Position);
        Assert.Equal(first.EndTime, first.CurrentTime);
    }

    [Fact]
    public void ConstructionAndConsumptionLeaveDatasetEventsAndMetadataUnchanged()
    {
        var dataset = CreateDataset();
        var events = dataset.Events;
        var snapshot = events.ToArray();
        var count = dataset.Count;
        var start = dataset.StartTime;
        var end = dataset.EndTime;
        var session = new CausalReplaySession(dataset);
        while (session.TryMoveNext(out _)) { }

        Assert.Same(events, dataset.Events);
        Assert.Equal(count, dataset.Count);
        Assert.Same(start, dataset.StartTime);
        Assert.Same(end, dataset.EndTime);
        for (var index = 0; index < count; index++) Assert.Same(snapshot[index], dataset.Events[index]);
    }

    [Fact]
    public void ReadingStateDoesNotDeliverOrAdvanceToTheNextAvailability()
    {
        var dataset = CreateDataset();
        var session = new CausalReplaySession(dataset);
        Assert.True(session.TryMoveNext(out var first));
        Assert.Same(dataset.Events[1], first);

        for (var observation = 0; observation < 3; observation++)
        {
            Assert.Equal(1, session.Position);
            Assert.Equal(At(3), session.CurrentTime);
            Assert.Equal(At(5), session.EndTime);
            Assert.False(session.IsCompleted);
        }

        Assert.True(session.TryMoveNext(out var second));
        Assert.Same(dataset.Events[2], second);
        Assert.Equal(At(4), session.CurrentTime);
    }

    [Fact]
    public void PreservesAdjacentTicksAndFullTemporalRangeIncludingCandleCloseAtMaximum()
    {
        var minimum = new UtcInstant(DateTimeOffset.MinValue);
        var afterMinimum = new UtcInstant(DateTimeOffset.MinValue.AddTicks(1));
        var beforeMaximum = new UtcInstant(DateTimeOffset.MaxValue.AddTicks(-1));
        var maximum = new UtcInstant(DateTimeOffset.MaxValue);
        var quote = Create(ReplayEventKind.Quote, A, minimum);
        var trade = Create(ReplayEventKind.Trade, B, afterMinimum);
        var candle = Create(ReplayEventKind.Candle, C, beforeMaximum, new CandleInterval(TimeSpan.FromTicks(1)));
        var lastQuote = Create(ReplayEventKind.Quote, A, maximum);
        var dataset = new HistoricalReplayDataset([lastQuote, candle, trade, quote]);
        var session = new CausalReplaySession(dataset);
        var expected = new[] { quote, trade, candle, lastQuote };
        var times = new[] { minimum, afterMinimum, maximum, maximum };

        for (var index = 0; index < expected.Length; index++)
        {
            Assert.True(session.TryMoveNext(out var delivered));
            Assert.Same(expected[index], delivered);
            Assert.Equal(times[index], session.CurrentTime);
        }

        Assert.Equal(maximum, session.EndTime);
        Assert.Equal(4, session.Position);
        Assert.True(session.IsCompleted);
    }

    private static HistoricalReplayDataset CreateDataset() => new(
    [
        Create(ReplayEventKind.Quote, A, At(4)),
        Create(ReplayEventKind.Candle, C, Start),
        Create(ReplayEventKind.Trade, B, At(3))
    ]);

    private static HistoricalReplayDataset CreateTiedDataset() => new(
    [
        Create(ReplayEventKind.Trade, B, At(5)),
        Create(ReplayEventKind.Quote, A, At(5)),
        Create(ReplayEventKind.Candle, C, Start)
    ]);

    private static UtcInstant At(int minutes) => new(Start.Value.AddMinutes(minutes));

    private static ReplayEvent Create(ReplayEventKind kind, InstrumentId id, UtcInstant time, CandleInterval? interval = null)
    {
        var price = new Price(10.0500m);
        var quantity = new Quantity(10.0500m);
        var received = new UtcInstant(Start.Value.AddHours(-1));
        return kind switch
        {
            ReplayEventKind.Quote => ReplayEvent.FromQuote(new Quote(id, time, received, price, quantity, price, quantity)),
            ReplayEventKind.Trade => ReplayEvent.FromTrade(new Trade(id, time, received, price, quantity)),
            _ => ReplayEvent.FromCandle(new Candle(id, interval ?? CandleInterval.FiveMinutes,
                time, price, price, price, price, quantity))
        };
    }

    private static object Payload(ReplayEvent replayEvent) => replayEvent.Kind switch
    {
        ReplayEventKind.Quote => replayEvent.Quote!,
        ReplayEventKind.Trade => replayEvent.Trade!,
        _ => replayEvent.Candle!
    };
}
