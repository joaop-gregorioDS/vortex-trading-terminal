using VortexTrading.Application.MarketData;
using VortexTrading.Application.MarketData.Orchestration;
using VortexTrading.Application.MarketData.Replay;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Trading;
using VortexTrading.Infrastructure.MarketData.Replay;
using VortexTrading.Infrastructure.MarketData.Simulation;

namespace VortexTrading.Infrastructure.Tests;

public sealed class HistoricalReplayProviderTests
{
    private static readonly InstrumentId A = new(new Guid("11111111-1111-1111-1111-111111111111"));
    private static readonly InstrumentId B = new(new Guid("22222222-2222-2222-2222-222222222222"));
    private static readonly InstrumentId C = new(new Guid("33333333-3333-3333-3333-333333333333"));
    private static readonly UtcInstant Start = new(new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero).AddTicks(7));

    [Fact]
    public void RejectsNullDataset()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new HistoricalReplayProvider(null));
        Assert.Equal("dataset", exception.ParamName);
    }

    [Fact]
    public void DescriptorHasStableIdentityAndAllThreeCapabilities()
    {
        IMarketDataProvider provider = new HistoricalReplayProvider(CreateFixture().Dataset);

        Assert.Same(provider.Descriptor, provider.Descriptor);
        Assert.Equal("historical-replay", provider.Descriptor.Key);
        Assert.Equal("Historical Replay", provider.Descriptor.DisplayName);
        Assert.Equal(MarketDataCapabilities.Quotes | MarketDataCapabilities.Trades | MarketDataCapabilities.Candles,
            provider.Descriptor.Capabilities);
    }

    [Theory]
    [InlineData(ReplayEventKind.Quote, "null")]
    [InlineData(ReplayEventKind.Quote, "empty")]
    [InlineData(ReplayEventKind.Quote, "null-element")]
    [InlineData(ReplayEventKind.Trade, "null")]
    [InlineData(ReplayEventKind.Trade, "empty")]
    [InlineData(ReplayEventKind.Trade, "null-element")]
    [InlineData(ReplayEventKind.Candle, "null")]
    [InlineData(ReplayEventKind.Candle, "empty")]
    [InlineData(ReplayEventKind.Candle, "null-element")]
    public void RejectsInvalidSubscriptionsAtTheCall(ReplayEventKind kind, string invalidCase)
    {
        var provider = new HistoricalReplayProvider(CreateFixture().Dataset);
        IReadOnlyCollection<InstrumentId> ids = invalidCase switch
        {
            "null" => null!,
            "empty" => [],
            _ => [A, null!]
        };

        var exception = invalidCase == "null"
            ? Assert.Throws<ArgumentNullException>(() => Request(provider, kind, ids, TestContext.Current.CancellationToken))
            : Assert.Throws<ArgumentException>(() => Request(provider, kind, ids, TestContext.Current.CancellationToken));

        Assert.Equal("instrumentIds", exception.ParamName);
    }

    [Fact]
    public void RejectsNullCandleIntervalAtTheCall()
    {
        var provider = new HistoricalReplayProvider(CreateFixture().Dataset);

        var exception = Assert.Throws<ArgumentNullException>(() =>
            provider.StreamCandlesAsync([A], null!, TestContext.Current.CancellationToken));

        Assert.Equal("interval", exception.ParamName);
    }

    [Theory]
    [InlineData(ReplayEventKind.Quote)]
    [InlineData(ReplayEventKind.Trade)]
    [InlineData(ReplayEventKind.Candle)]
    public async Task ProjectsOnlyTheRequestedTypeInDatasetOrderWithOriginalOccurrences(ReplayEventKind kind)
    {
        var fixture = CreateFixture();
        var before = fixture.Dataset.Events.ToArray();
        var provider = new HistoricalReplayProvider(fixture.Dataset);

        var actual = await ReadAllAsync(Request(provider, kind, [A, B], TestContext.Current.CancellationToken));

        AssertSameSequence(Expected(fixture, kind), actual);
        Assert.Equal(before, fixture.Dataset.Events);
        Assert.Equal(before.Length, fixture.Dataset.Count);
        if (kind == ReplayEventKind.Quote)
        {
            Assert.Same(fixture.Quotes[0].ReceivedTime, Assert.IsType<Quote>(actual[0]).ReceivedTime);
            Assert.NotEqual(fixture.Quotes[0].EventTime, fixture.Quotes[0].ReceivedTime);
        }
        if (kind == ReplayEventKind.Trade)
        {
            Assert.Same(fixture.Trades[0].ReceivedTime, Assert.IsType<Trade>(actual[0]).ReceivedTime);
            Assert.NotEqual(fixture.Trades[0].EventTime, fixture.Trades[0].ReceivedTime);
        }
    }

    [Theory]
    [InlineData(ReplayEventKind.Quote, true)]
    [InlineData(ReplayEventKind.Quote, false)]
    [InlineData(ReplayEventKind.Trade, true)]
    [InlineData(ReplayEventKind.Trade, false)]
    [InlineData(ReplayEventKind.Candle, true)]
    [InlineData(ReplayEventKind.Candle, false)]
    public async Task FiltersToExactlyTheRequestedInstrument(ReplayEventKind kind, bool requestA)
    {
        var fixture = CreateFixture();
        var provider = new HistoricalReplayProvider(fixture.Dataset);
        var expected = (kind, requestA) switch
        {
            (ReplayEventKind.Quote, true) => new object[] { fixture.Quotes[2], fixture.Quotes[4] },
            (ReplayEventKind.Quote, false) => [fixture.Quotes[0], fixture.Quotes[1], fixture.Quotes[3]],
            (ReplayEventKind.Trade, true) => [fixture.Trades[0], fixture.Trades[1], fixture.Trades[3]],
            (ReplayEventKind.Trade, false) => [fixture.Trades[2], fixture.Trades[4]],
            (ReplayEventKind.Candle, true) => [fixture.Candles[2]],
            _ => [fixture.Candles[0], fixture.Candles[1], fixture.Candles[3]]
        };

        var actual = await ReadAllAsync(Request(provider, kind, [requestA ? A : B], TestContext.Current.CancellationToken));

        AssertSameSequence(expected, actual);
    }

    [Theory]
    [InlineData(ReplayEventKind.Quote, false)]
    [InlineData(ReplayEventKind.Trade, false)]
    [InlineData(ReplayEventKind.Candle, false)]
    [InlineData(ReplayEventKind.Quote, true)]
    [InlineData(ReplayEventKind.Trade, true)]
    [InlineData(ReplayEventKind.Candle, true)]
    public async Task MissingInstrumentsOrEventTypesCompleteNormallyWithAnEmptyStream(ReplayEventKind kind, bool missingType)
    {
        var fixture = CreateFixture();
        var dataset = missingType
            ? new HistoricalReplayDataset([kind == ReplayEventKind.Quote
                ? ReplayEvent.FromTrade(fixture.Trades[0]) : ReplayEvent.FromQuote(fixture.Quotes[0])])
            : fixture.Dataset;
        var provider = new HistoricalReplayProvider(dataset);

        var actual = await ReadAllAsync(Request(provider, kind, missingType ? [A, B] : [C], TestContext.Current.CancellationToken));

        Assert.Empty(actual);
    }

    [Fact]
    public async Task CandleIntervalUsesValueEqualityAndDoesNotAggregateOrReconstruct()
    {
        var fixture = CreateFixture();
        var provider = new HistoricalReplayProvider(fixture.Dataset);
        var interval = new CandleInterval(CandleInterval.FiveMinutes.Duration);
        Assert.NotSame(interval, fixture.FiveMinuteCandle.Interval);

        var actual = await ReadAllAsync(provider.StreamCandlesAsync([A, B], interval, TestContext.Current.CancellationToken));

        Assert.Same(fixture.FiveMinuteCandle, Assert.Single(actual));
        Assert.Same(fixture.FiveMinuteCandle.Interval, Assert.IsType<Candle>(actual[0]).Interval);
    }

    [Fact]
    public async Task ValidCandleIntervalWithoutMatchesCompletesNormally()
    {
        var provider = new HistoricalReplayProvider(CreateFixture().Dataset);

        var actual = await ReadAllAsync(provider.StreamCandlesAsync([A, B], CandleInterval.OneHour, TestContext.Current.CancellationToken));

        Assert.Empty(actual);
    }

    [Theory]
    [InlineData(ReplayEventKind.Quote)]
    [InlineData(ReplayEventKind.Trade)]
    [InlineData(ReplayEventKind.Candle)]
    public async Task DuplicateSubscriptionIdsDoNotMultiplyHistoricalOccurrences(ReplayEventKind kind)
    {
        var fixture = CreateFixture();
        var provider = new HistoricalReplayProvider(fixture.Dataset);
        var baseline = await ReadAllAsync(Request(provider, kind, [A, B], TestContext.Current.CancellationToken));

        var actual = await ReadAllAsync(Request(provider, kind, [B, new(A.Value), A, B], TestContext.Current.CancellationToken));

        AssertSameSequence(Expected(fixture, kind), baseline);
        AssertSameSequence(baseline, actual);
    }

    [Theory]
    [InlineData(ReplayEventKind.Quote)]
    [InlineData(ReplayEventKind.Trade)]
    [InlineData(ReplayEventKind.Candle)]
    public async Task CallerMutationBeforeAndDuringEnumerationDoesNotChangeTheSnapshot(ReplayEventKind kind)
    {
        var fixture = CreateFixture();
        var provider = new HistoricalReplayProvider(fixture.Dataset);
        var original = new List<InstrumentId> { B, A, A };
        var stream = Request(provider, kind, original, TestContext.Current.CancellationToken);
        Assert.Equal(new[] { B, A, A }, original);
        original.Clear();
        original.Add(C);
        var actual = new List<object>();
        await using var enumerator = stream.GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await enumerator.MoveNextAsync());
        actual.Add(enumerator.Current);
        original.Clear();
        original.Add(A);
        while (await enumerator.MoveNextAsync()) actual.Add(enumerator.Current);

        AssertSameSequence(Expected(fixture, kind), actual);
    }

    [Theory]
    [InlineData(ReplayEventKind.Quote)]
    [InlineData(ReplayEventKind.Trade)]
    [InlineData(ReplayEventKind.Candle)]
    public async Task DatasetOrderWinsOverSubscriptionOrder(ReplayEventKind kind)
    {
        var events = new[]
        {
            CreateEvent(kind, B, At(0)), CreateEvent(kind, A, At(1)),
            CreateEvent(kind, B, At(2)), CreateEvent(kind, A, At(3))
        };
        var provider = new HistoricalReplayProvider(new HistoricalReplayDataset([events[3], events[1], events[2], events[0]]));
        var expected = events.Select(replayEvent => Payload(replayEvent)).ToArray();

        AssertSameSequence(expected, await ReadAllAsync(Request(provider, kind, [A, B], TestContext.Current.CancellationToken)));
        AssertSameSequence(expected, await ReadAllAsync(Request(provider, kind, [B, A], TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task RequestAndEnumeratorCreationAreDeferredEvenWhenAlreadyCanceledAndNothingMatches()
    {
        var provider = new HistoricalReplayProvider(CreateFixture().Dataset);
        using var source = new CancellationTokenSource();
        source.Cancel();

        var stream = provider.StreamQuotesAsync([C], source.Token);
        await using var enumerator = stream.GetAsyncEnumerator(TestContext.Current.CancellationToken);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await enumerator.MoveNextAsync());
    }

    [Theory]
    [InlineData(ReplayEventKind.Quote, false, false)]
    [InlineData(ReplayEventKind.Quote, false, true)]
    [InlineData(ReplayEventKind.Quote, true, false)]
    [InlineData(ReplayEventKind.Quote, true, true)]
    [InlineData(ReplayEventKind.Trade, false, false)]
    [InlineData(ReplayEventKind.Trade, false, true)]
    [InlineData(ReplayEventKind.Trade, true, false)]
    [InlineData(ReplayEventKind.Trade, true, true)]
    [InlineData(ReplayEventKind.Candle, false, false)]
    [InlineData(ReplayEventKind.Candle, false, true)]
    [InlineData(ReplayEventKind.Candle, true, false)]
    [InlineData(ReplayEventKind.Candle, true, true)]
    public async Task HonorsOperationAndEnumerationCancellationBeforeAndDuringConsumption(
        ReplayEventKind kind, bool useEnumerationToken, bool cancelDuringConsumption)
    {
        var provider = new HistoricalReplayProvider(CreateFixture().Dataset);
        using var source = new CancellationTokenSource();
        if (!cancelDuringConsumption) source.Cancel();
        var operationToken = useEnumerationToken ? CancellationToken.None : source.Token;
        var enumerationToken = useEnumerationToken ? source.Token : CancellationToken.None;
        var stream = Request(provider, kind, [A, B], operationToken);
        await using var enumerator = stream.GetAsyncEnumerator(enumerationToken);
        if (cancelDuringConsumption)
        {
            Assert.True(await enumerator.MoveNextAsync());
            source.Cancel();
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await enumerator.MoveNextAsync());
    }

    [Theory]
    [InlineData(ReplayEventKind.Quote, false)]
    [InlineData(ReplayEventKind.Quote, true)]
    [InlineData(ReplayEventKind.Trade, false)]
    [InlineData(ReplayEventKind.Trade, true)]
    [InlineData(ReplayEventKind.Candle, false)]
    [InlineData(ReplayEventKind.Candle, true)]
    public async Task EitherDistinctTokenCanCancelTheCombinedEnumeration(ReplayEventKind kind, bool cancelEnumerationToken)
    {
        var provider = new HistoricalReplayProvider(CreateFixture().Dataset);
        using var operationSource = new CancellationTokenSource();
        using var enumerationSource = new CancellationTokenSource();
        await using var enumerator = Request(provider, kind, [A, B], operationSource.Token)
            .GetAsyncEnumerator(enumerationSource.Token);
        Assert.True(await enumerator.MoveNextAsync());
        if (cancelEnumerationToken) enumerationSource.Cancel();
        else operationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await enumerator.MoveNextAsync());
    }

    [Theory]
    [InlineData(ReplayEventKind.Quote)]
    [InlineData(ReplayEventKind.Trade)]
    [InlineData(ReplayEventKind.Candle)]
    public async Task CancellationAfterTheLastMatchIsObservedBeforeCompletion(ReplayEventKind kind)
    {
        var replayEvent = CreateEvent(kind, A, Start);
        var provider = new HistoricalReplayProvider(new HistoricalReplayDataset([replayEvent]));
        using var source = new CancellationTokenSource();
        await using var enumerator = Request(provider, kind, [A], source.Token)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await enumerator.MoveNextAsync());
        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await enumerator.MoveNextAsync());
    }

    [Theory]
    [InlineData(ReplayEventKind.Quote)]
    [InlineData(ReplayEventKind.Trade)]
    [InlineData(ReplayEventKind.Candle)]
    public async Task RepeatedRequestsAndReenumerationRestartTheSameProjection(ReplayEventKind kind)
    {
        var fixture = CreateFixture();
        var provider = new HistoricalReplayProvider(fixture.Dataset);
        var stream = Request(provider, kind, [A, B], TestContext.Current.CancellationToken);

        AssertSameSequence(Expected(fixture, kind), await ReadAllAsync(stream));
        AssertSameSequence(Expected(fixture, kind), await ReadAllAsync(stream));
        AssertSameSequence(Expected(fixture, kind), await ReadAllAsync(Request(provider, kind, [A, B], TestContext.Current.CancellationToken)));
    }

    [Theory]
    [InlineData(ReplayEventKind.Quote)]
    [InlineData(ReplayEventKind.Trade)]
    [InlineData(ReplayEventKind.Candle)]
    public async Task InterleavedEnumerationsDoNotShareCursorOrCancellation(ReplayEventKind kind)
    {
        var fixture = CreateFixture();
        var provider = new HistoricalReplayProvider(fixture.Dataset);
        using var firstSource = new CancellationTokenSource();
        var stream = Request(provider, kind, [A, B], TestContext.Current.CancellationToken);
        await using var first = stream.GetAsyncEnumerator(firstSource.Token);
        await using var second = stream.GetAsyncEnumerator(TestContext.Current.CancellationToken);
        var expected = Expected(fixture, kind);
        Assert.True(await first.MoveNextAsync());
        Assert.True(await first.MoveNextAsync());
        Assert.True(await second.MoveNextAsync());
        Assert.Same(expected[1], first.Current);
        Assert.Same(expected[0], second.Current);
        firstSource.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await first.MoveNextAsync());
        var actual = new List<object> { second.Current };
        while (await second.MoveNextAsync()) actual.Add(second.Current);

        AssertSameSequence(expected, actual);
        AssertSameSequence(expected, await ReadAllAsync(stream));
    }

    [Theory]
    [InlineData("maximum")]
    [InlineData("smallest")]
    [InlineData("precision")]
    [InlineData("scale")]
    public async Task PreservesExtremeAndHighPrecisionDecimalsAcrossAllPayloadTypes(string valueCase)
    {
        var value = valueCase switch
        {
            "maximum" => decimal.MaxValue,
            "smallest" => 0.0000000000000000000000000001m,
            "precision" => 7.1234567890123456789012345678m,
            _ => 10.0500m
        };
        var events = new[]
        {
            CreateEvent(ReplayEventKind.Quote, A, Start, value),
            CreateEvent(ReplayEventKind.Trade, A, Start, value),
            CreateEvent(ReplayEventKind.Candle, A, Start, value)
        };
        var provider = new HistoricalReplayProvider(new HistoricalReplayDataset(events));
        foreach (var replayEvent in events)
        {
            var actual = Assert.Single(await ReadAllAsync(Request(provider, replayEvent.Kind, [A], TestContext.Current.CancellationToken)));
            Assert.Same(Payload(replayEvent), actual);
            decimal[] values = actual switch
            {
                Quote quote => [quote.BidPrice.Value, quote.AskPrice.Value, quote.BidQuantity.Value, quote.AskQuantity.Value],
                Trade trade => [trade.Price.Value, trade.Quantity.Value],
                Candle candle => [candle.Open.Value, candle.High.Value, candle.Low.Value, candle.Close.Value, candle.Volume.Value],
                _ => throw new InvalidOperationException()
            };
            Assert.All(values, preserved => Assert.Equal(decimal.GetBits(value), decimal.GetBits(preserved)));
        }
    }

    [Theory]
    [InlineData(ReplayEventKind.Quote)]
    [InlineData(ReplayEventKind.Trade)]
    [InlineData(ReplayEventKind.Candle)]
    public async Task TheUnchangedOrchestratorConsumesReplayAndSimulationThroughTheSamePort(ReplayEventKind kind)
    {
        var fixture = CreateFixture();
        IMarketDataProvider replay = new HistoricalReplayProvider(fixture.Dataset);
        IMarketDataProvider simulation = new SimulationMarketDataProvider(
            new SimulationMarketDataOptions(Start, new Price(10m), new Quantity(1m), 2));
        var replayOrchestrator = new MarketDataOrchestrator(replay);
        var simulationOrchestrator = new MarketDataOrchestrator(simulation);
        IAsyncEnumerable<object> Consume(MarketDataOrchestrator orchestrator) => kind switch
        {
            ReplayEventKind.Quote => orchestrator.StreamQuotesAsync([B, A, A], TestContext.Current.CancellationToken),
            ReplayEventKind.Trade => orchestrator.StreamTradesAsync([B, A, A], TestContext.Current.CancellationToken),
            _ => orchestrator.StreamCandlesAsync([B, A, A], CandleInterval.OneMinute, TestContext.Current.CancellationToken)
        };

        AssertSameSequence(Expected(fixture, kind), await ReadAllAsync(Consume(replayOrchestrator)));
        Assert.Equal(4, (await ReadAllAsync(Consume(simulationOrchestrator))).Count);
        Assert.Same(replay.Descriptor, replayOrchestrator.ProviderDescriptor);
        Assert.Same(simulation.Descriptor, simulationOrchestrator.ProviderDescriptor);
    }

    private static IAsyncEnumerable<object> Request(
        IMarketDataProvider provider, ReplayEventKind kind, IReadOnlyCollection<InstrumentId> ids,
        CancellationToken cancellationToken) => kind switch
        {
            ReplayEventKind.Quote => provider.StreamQuotesAsync(ids, cancellationToken),
            ReplayEventKind.Trade => provider.StreamTradesAsync(ids, cancellationToken),
            ReplayEventKind.Candle => provider.StreamCandlesAsync(ids, CandleInterval.OneMinute, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

    private static async Task<List<object>> ReadAllAsync(IAsyncEnumerable<object> stream)
    {
        var result = new List<object>();
        await foreach (var payload in stream.WithCancellation(TestContext.Current.CancellationToken)) result.Add(payload);
        return result;
    }

    private static void AssertSameSequence(IReadOnlyList<object> expected, IReadOnlyList<object> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var index = 0; index < expected.Count; index++) Assert.Same(expected[index], actual[index]);
    }

    private static object[] Expected(Fixture fixture, ReplayEventKind kind) => kind switch
    {
        ReplayEventKind.Quote => fixture.Quotes.Cast<object>().ToArray(),
        ReplayEventKind.Trade => fixture.Trades.Cast<object>().ToArray(),
        _ => fixture.Candles.Cast<object>().ToArray()
    };

    private static object Payload(ReplayEvent replayEvent) => replayEvent.Kind switch
    {
        ReplayEventKind.Quote => replayEvent.Quote!,
        ReplayEventKind.Trade => replayEvent.Trade!,
        _ => replayEvent.Candle!
    };

    private static UtcInstant At(int offset) => new(Start.Value.AddSeconds(offset));

    private static ReplayEvent CreateEvent(ReplayEventKind kind, InstrumentId id, UtcInstant time, decimal value = 10.0500m)
    {
        var price = new Price(value);
        var quantity = new Quantity(value);
        var received = new UtcInstant(time.Value.AddHours(-1));
        return kind switch
        {
            ReplayEventKind.Quote => ReplayEvent.FromQuote(new Quote(id, time, received, price, quantity, price, quantity)),
            ReplayEventKind.Trade => ReplayEvent.FromTrade(new Trade(id, time, received, price, quantity)),
            _ => ReplayEvent.FromCandle(new Candle(id, CandleInterval.OneMinute, time, price, price, price, price, quantity))
        };
    }

    private static Fixture CreateFixture()
    {
        var qB1 = CreateEvent(ReplayEventKind.Quote, B, At(0));
        var tA1 = CreateEvent(ReplayEventKind.Trade, A, At(1));
        var cB1 = CreateEvent(ReplayEventKind.Candle, B, At(2));
        var qA1 = CreateEvent(ReplayEventKind.Quote, A, At(3));
        var price = new Price(11m);
        var fiveMinute = new Candle(A, CandleInterval.FiveMinutes, At(4), price, price, price, price, new Quantity(2m));
        var tB1 = CreateEvent(ReplayEventKind.Trade, B, At(5));
        var qB2 = CreateEvent(ReplayEventKind.Quote, B, At(6));
        var cA1 = CreateEvent(ReplayEventKind.Candle, A, At(7));
        var tA2 = CreateEvent(ReplayEventKind.Trade, A, At(8));
        var qA2 = CreateEvent(ReplayEventKind.Quote, A, At(9));
        var cB2 = CreateEvent(ReplayEventKind.Candle, B, At(10));
        var tB2 = CreateEvent(ReplayEventKind.Trade, B, At(11));
        var dataset = new HistoricalReplayDataset(
        [
            tB2, cB2, qA2, tA2, cA1, qB2, tB1, ReplayEvent.FromCandle(fiveMinute), qA1, cB1, tA1, qB1,
            ReplayEvent.FromQuote(qB1.Quote), ReplayEvent.FromTrade(tA1.Trade), ReplayEvent.FromCandle(cB1.Candle)
        ]);
        return new Fixture(dataset, [qB1.Quote!, qB1.Quote!, qA1.Quote!, qB2.Quote!, qA2.Quote!],
            [tA1.Trade!, tA1.Trade!, tB1.Trade!, tA2.Trade!, tB2.Trade!],
            [cB1.Candle!, cB1.Candle!, cA1.Candle!, cB2.Candle!], fiveMinute);
    }

    private sealed record Fixture(HistoricalReplayDataset Dataset, Quote[] Quotes, Trade[] Trades, Candle[] Candles, Candle FiveMinuteCandle);
}
