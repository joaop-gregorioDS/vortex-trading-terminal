using VortexTrading.Application.MarketData;
using VortexTrading.Application.MarketData.Orchestration;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Application.Tests;

public sealed class MarketDataOrchestratorTests
{
    private const MarketDataCapabilities AllCapabilities = MarketDataCapabilities.Quotes
        | MarketDataCapabilities.Trades | MarketDataCapabilities.Candles;
    private static readonly InstrumentId A = new(new Guid("11111111-1111-1111-1111-111111111111"));
    private static readonly InstrumentId B = new(new Guid("22222222-2222-2222-2222-222222222222"));
    private static readonly InstrumentId C = new(new Guid("33333333-3333-3333-3333-333333333333"));
    private static readonly CandleInterval Interval = new(TimeSpan.FromTicks(170000003));

    [Fact]
    public void RejectsNullProvider()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new MarketDataOrchestrator(null));
        Assert.Equal("provider", exception.ParamName);
    }

    [Fact]
    public void DelegatesTheOriginalDescriptor()
    {
        var provider = new RecordingMarketDataProvider(AllCapabilities);
        var orchestrator = new MarketDataOrchestrator(provider);

        Assert.Same(provider.Descriptor, orchestrator.ProviderDescriptor);
        Assert.Equal(0, provider.TotalCalls);
    }

    [Theory]
    [InlineData(MarketDataCapabilities.Quotes, "null")]
    [InlineData(MarketDataCapabilities.Quotes, "empty")]
    [InlineData(MarketDataCapabilities.Quotes, "null-element")]
    [InlineData(MarketDataCapabilities.Trades, "null")]
    [InlineData(MarketDataCapabilities.Trades, "empty")]
    [InlineData(MarketDataCapabilities.Trades, "null-element")]
    [InlineData(MarketDataCapabilities.Candles, "null")]
    [InlineData(MarketDataCapabilities.Candles, "empty")]
    [InlineData(MarketDataCapabilities.Candles, "null-element")]
    public void RejectsInvalidSubscriptionsBeforeCallingTheProvider(MarketDataCapabilities operation, string invalidCase)
    {
        var provider = new RecordingMarketDataProvider(AllCapabilities);
        var orchestrator = new MarketDataOrchestrator(provider);
        IReadOnlyCollection<InstrumentId> ids = invalidCase switch
        {
            "null" => null!,
            "empty" => [],
            _ => [A, null!]
        };

        var exception = invalidCase == "null"
            ? Assert.Throws<ArgumentNullException>(() => Request(orchestrator, operation, ids, TestContext.Current.CancellationToken))
            : Assert.Throws<ArgumentException>(() => Request(orchestrator, operation, ids, TestContext.Current.CancellationToken));

        Assert.Equal("instrumentIds", exception.ParamName);
        Assert.Equal(0, provider.TotalCalls);
        Assert.Equal(0, provider.EnumerationStarts);
    }

    [Fact]
    public void RejectsNullCandleIntervalBeforeCallingTheProvider()
    {
        var provider = new RecordingMarketDataProvider(AllCapabilities);
        var orchestrator = new MarketDataOrchestrator(provider);

        var exception = Assert.Throws<ArgumentNullException>(() =>
            orchestrator.StreamCandlesAsync([A], null!, TestContext.Current.CancellationToken));

        Assert.Equal("interval", exception.ParamName);
        Assert.Equal(0, provider.TotalCalls);
    }

    [Theory]
    [InlineData(MarketDataCapabilities.Quotes)]
    [InlineData(MarketDataCapabilities.Trades)]
    [InlineData(MarketDataCapabilities.Candles)]
    public async Task NormalizesAndSnapshotsSubscriptionsWithoutMutatingTheCaller(MarketDataCapabilities operation)
    {
        var provider = CreateProvider();
        var orchestrator = new MarketDataOrchestrator(provider);
        var original = new List<InstrumentId> { B, A, new(A.Value) };
        var stream = Request(orchestrator, operation, original, TestContext.Current.CancellationToken);

        Assert.Equal(new[] { B, A, A }, original);
        Assert.NotSame(original, provider.Subscription);
        Assert.Equal(new[] { A, B }, provider.Subscription);
        var snapshot = Assert.IsAssignableFrom<IList<InstrumentId>>(provider.Subscription);
        Assert.True(snapshot.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => snapshot[0] = C);

        original.Clear();
        original.Add(C);
        await using var enumerator = stream.GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await enumerator.MoveNextAsync());
        original.Add(B);
        Assert.Equal(new[] { A, B }, provider.Subscription);
        while (await enumerator.MoveNextAsync())
        {
        }
        Assert.Equal(new[] { A, B }, provider.Subscription);

        Request(orchestrator, operation, new[] { A, B }, TestContext.Current.CancellationToken);
        Assert.Equal(new[] { A, B }, provider.Subscription);
    }

    [Theory]
    [InlineData(MarketDataCapabilities.Quotes, MarketDataCapabilities.Trades)]
    [InlineData(MarketDataCapabilities.Quotes, MarketDataCapabilities.Candles)]
    [InlineData(MarketDataCapabilities.Quotes, MarketDataCapabilities.Trades | MarketDataCapabilities.Candles)]
    [InlineData(MarketDataCapabilities.Trades, MarketDataCapabilities.Quotes)]
    [InlineData(MarketDataCapabilities.Trades, MarketDataCapabilities.Candles)]
    [InlineData(MarketDataCapabilities.Trades, MarketDataCapabilities.Quotes | MarketDataCapabilities.Candles)]
    [InlineData(MarketDataCapabilities.Candles, MarketDataCapabilities.Quotes)]
    [InlineData(MarketDataCapabilities.Candles, MarketDataCapabilities.Trades)]
    [InlineData(MarketDataCapabilities.Candles, MarketDataCapabilities.Quotes | MarketDataCapabilities.Trades)]
    public void RejectsMissingCapabilityWithoutInvokingAnyProviderOperation(
        MarketDataCapabilities operation, MarketDataCapabilities advertised)
    {
        var provider = new RecordingMarketDataProvider(advertised);
        var orchestrator = new MarketDataOrchestrator(provider);

        Assert.Throws<NotSupportedException>(() => Request(orchestrator, operation, [A], TestContext.Current.CancellationToken));
        Assert.Equal(0, provider.TotalCalls);
        Assert.Equal(0, provider.EnumerationStarts);
    }

    [Theory]
    [InlineData(MarketDataCapabilities.Quotes, MarketDataCapabilities.Quotes)]
    [InlineData(MarketDataCapabilities.Trades, MarketDataCapabilities.Trades)]
    [InlineData(MarketDataCapabilities.Candles, MarketDataCapabilities.Candles)]
    [InlineData(MarketDataCapabilities.Quotes, MarketDataCapabilities.Quotes | MarketDataCapabilities.Trades)]
    [InlineData(MarketDataCapabilities.Trades, MarketDataCapabilities.Quotes | MarketDataCapabilities.Trades)]
    [InlineData(MarketDataCapabilities.Candles, MarketDataCapabilities.Quotes | MarketDataCapabilities.Candles)]
    [InlineData(MarketDataCapabilities.Quotes, AllCapabilities)]
    [InlineData(MarketDataCapabilities.Trades, AllCapabilities)]
    [InlineData(MarketDataCapabilities.Candles, AllCapabilities)]
    public void AcceptsTheRequiredFlagAndCallsOnlyTheCorrespondingOperation(
        MarketDataCapabilities operation, MarketDataCapabilities advertised)
    {
        var provider = new RecordingMarketDataProvider(advertised);
        var orchestrator = new MarketDataOrchestrator(provider);

        Request(orchestrator, operation, [A], TestContext.Current.CancellationToken);

        Assert.Equal(operation == MarketDataCapabilities.Quotes ? 1 : 0, provider.QuoteCalls);
        Assert.Equal(operation == MarketDataCapabilities.Trades ? 1 : 0, provider.TradeCalls);
        Assert.Equal(operation == MarketDataCapabilities.Candles ? 1 : 0, provider.CandleCalls);
    }

    [Theory]
    [InlineData(MarketDataCapabilities.Quotes)]
    [InlineData(MarketDataCapabilities.Trades)]
    [InlineData(MarketDataCapabilities.Candles)]
    public async Task ForwardsEveryOriginalEventInProviderOrder(MarketDataCapabilities operation)
    {
        var provider = CreateProvider();
        var orchestrator = new MarketDataOrchestrator(provider);
        var stream = Request(orchestrator, operation, [A, B], TestContext.Current.CancellationToken);
        var expected = operation switch
        {
            MarketDataCapabilities.Quotes => provider.Quotes.Cast<object>().ToArray(),
            MarketDataCapabilities.Trades => provider.Trades.Cast<object>().ToArray(),
            _ => provider.Candles.Cast<object>().ToArray()
        };

        Assert.Same(provider.ReturnedStream, stream);
        var actual = await ReadAllAsync(stream);
        Assert.Equal(expected.Length, actual.Count);
        for (var index = 0; index < expected.Length; index++)
        {
            Assert.Same(expected[index], actual[index]);
        }

        if (operation == MarketDataCapabilities.Candles)
        {
            Assert.Same(Interval, provider.RequestedInterval);
        }
    }

    [Fact]
    public async Task DoesNotEnumerateOrBufferProviderEventsWhenRequestingAStream()
    {
        var provider = CreateProvider();
        var stream = new MarketDataOrchestrator(provider).StreamQuotesAsync([A, B], TestContext.Current.CancellationToken);

        Assert.Equal(1, provider.QuoteCalls);
        Assert.Equal(0, provider.EnumerationStarts);
        Assert.Equal(0, provider.EmittedEvents);
        await using (var enumerator = stream.GetAsyncEnumerator(TestContext.Current.CancellationToken))
        {
            Assert.Equal(0, provider.EnumerationStarts);
            Assert.True(await enumerator.MoveNextAsync());
            Assert.Equal(1, provider.EnumerationStarts);
            Assert.Equal(1, provider.EmittedEvents);
            Assert.Same(provider.Quotes[0], enumerator.Current);
        }

        Assert.Equal(1, provider.EmittedEvents);
        Assert.Equal(1, provider.DisposedEnumerations);
    }

    [Theory]
    [InlineData(MarketDataCapabilities.Quotes, false, false)]
    [InlineData(MarketDataCapabilities.Quotes, false, true)]
    [InlineData(MarketDataCapabilities.Quotes, true, false)]
    [InlineData(MarketDataCapabilities.Quotes, true, true)]
    [InlineData(MarketDataCapabilities.Trades, false, false)]
    [InlineData(MarketDataCapabilities.Trades, false, true)]
    [InlineData(MarketDataCapabilities.Trades, true, false)]
    [InlineData(MarketDataCapabilities.Trades, true, true)]
    [InlineData(MarketDataCapabilities.Candles, false, false)]
    [InlineData(MarketDataCapabilities.Candles, false, true)]
    [InlineData(MarketDataCapabilities.Candles, true, false)]
    [InlineData(MarketDataCapabilities.Candles, true, true)]
    public async Task PreservesCancellationBeforeAndDuringEnumeration(
        MarketDataCapabilities operation, bool useEnumerationToken, bool cancelDuringEnumeration)
    {
        var provider = CreateProvider();
        var orchestrator = new MarketDataOrchestrator(provider);
        using var source = new CancellationTokenSource();
        var operationToken = useEnumerationToken ? CancellationToken.None : source.Token;
        var enumerationToken = useEnumerationToken ? source.Token : CancellationToken.None;
        if (!cancelDuringEnumeration) source.Cancel();
        var stream = Request(orchestrator, operation, [A, B], operationToken);

        Assert.Equal(operationToken, provider.OperationToken);
        await using (var enumerator = stream.GetAsyncEnumerator(enumerationToken))
        {
            if (cancelDuringEnumeration)
            {
                Assert.True(await enumerator.MoveNextAsync());
                source.Cancel();
            }

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await enumerator.MoveNextAsync());
            Assert.Equal(cancelDuringEnumeration ? 1 : 0, provider.EmittedEvents);
        }

        Assert.Equal(1, provider.DisposedEnumerations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreservesBothDistinctCancellationTokens(bool cancelEnumerationToken)
    {
        var provider = CreateProvider();
        using var operationSource = new CancellationTokenSource();
        using var enumerationSource = new CancellationTokenSource();
        var stream = new MarketDataOrchestrator(provider).StreamQuotesAsync([A, B], operationSource.Token);
        await using var enumerator = stream.GetAsyncEnumerator(enumerationSource.Token);
        Assert.True(await enumerator.MoveNextAsync());

        if (cancelEnumerationToken) enumerationSource.Cancel();
        else operationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await enumerator.MoveNextAsync());
        Assert.Equal(operationSource.Token, provider.OperationToken);
        Assert.Equal(1, provider.EmittedEvents);
    }

    [Theory]
    [InlineData(MarketDataCapabilities.Quotes, false)]
    [InlineData(MarketDataCapabilities.Trades, false)]
    [InlineData(MarketDataCapabilities.Candles, false)]
    [InlineData(MarketDataCapabilities.Quotes, true)]
    [InlineData(MarketDataCapabilities.Trades, true)]
    [InlineData(MarketDataCapabilities.Candles, true)]
    public async Task PropagatesTheOriginalProviderFailure(MarketDataCapabilities operation, bool failDuringStream)
    {
        var failure = new InvalidOperationException("Test provider failure.");
        var provider = CreateProvider(failDuringStream ? null : failure, failDuringStream ? failure : null);
        var orchestrator = new MarketDataOrchestrator(provider);

        if (!failDuringStream)
        {
            var actual = Assert.Throws<InvalidOperationException>(() =>
                Request(orchestrator, operation, [A, B], TestContext.Current.CancellationToken));
            Assert.Same(failure, actual);
            Assert.Equal(0, provider.EnumerationStarts);
            return;
        }

        await using var enumerator = Request(orchestrator, operation, [A, B], TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await enumerator.MoveNextAsync());
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await enumerator.MoveNextAsync());
        Assert.Same(failure, exception);
        Assert.Equal(1, provider.EmittedEvents);
        Assert.Equal(1, provider.DisposedEnumerations);
    }

    [Fact]
    public async Task IndependentRequestsAndReenumerationDoNotShareSubscriptionOrProgress()
    {
        var provider = CreateProvider();
        var orchestrator = new MarketDataOrchestrator(provider);
        var firstStream = orchestrator.StreamQuotesAsync([B, A], TestContext.Current.CancellationToken);
        var firstSubscription = provider.Subscription;
        var secondStream = orchestrator.StreamQuotesAsync([C], TestContext.Current.CancellationToken);

        Assert.Equal(new[] { A, B }, firstSubscription);
        Assert.Equal(new[] { C }, provider.Subscription);
        Assert.NotSame(firstSubscription, provider.Subscription);
        await using var first = firstStream.GetAsyncEnumerator(TestContext.Current.CancellationToken);
        await using var second = secondStream.GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await first.MoveNextAsync());
        Assert.True(await first.MoveNextAsync());
        Assert.True(await second.MoveNextAsync());
        Assert.Same(provider.Quotes[1], first.Current);
        Assert.Same(provider.Quotes[0], second.Current);
        var repeated = await ReadAllAsync(firstStream);
        Assert.Equal(provider.Quotes.Count, repeated.Count);
        Assert.Same(provider.Quotes[0], repeated[0]);
    }

    private static IAsyncEnumerable<object> Request(
        MarketDataOrchestrator orchestrator, MarketDataCapabilities operation,
        IReadOnlyCollection<InstrumentId> ids, CancellationToken cancellationToken) => operation switch
        {
            MarketDataCapabilities.Quotes => orchestrator.StreamQuotesAsync(ids, cancellationToken),
            MarketDataCapabilities.Trades => orchestrator.StreamTradesAsync(ids, cancellationToken),
            MarketDataCapabilities.Candles => orchestrator.StreamCandlesAsync(ids, Interval, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };

    private static async Task<List<object>> ReadAllAsync(IAsyncEnumerable<object> stream)
    {
        var observations = new List<object>();
        await foreach (var observation in stream.WithCancellation(TestContext.Current.CancellationToken))
        {
            observations.Add(observation);
        }
        return observations;
    }

    private static RecordingMarketDataProvider CreateProvider(Exception? callException = null, Exception? enumerationException = null)
    {
        var early = new UtcInstant(new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero));
        var late = new UtcInstant(early.Value.AddHours(1));
        var price = new Price(10.0500m);
        var quantity = new Quantity(0.00100m);
        var quote = new Quote(B, late, early, price, quantity, new Price(10.0600m), new Quantity(2m));
        var trade = new Trade(B, late, early, price, quantity);
        var candle = new Candle(B, Interval, late, price, new Price(12m), new Price(9m), new Price(11m), quantity);

        return new RecordingMarketDataProvider(AllCapabilities)
        {
            Quotes = [quote, new Quote(A, early, late, price, quantity, price, quantity), quote],
            Trades = [trade, new Trade(A, early, late, new Price(7.1234567890123456789012345678m), quantity), trade],
            Candles = [candle, new Candle(A, CandleInterval.OneMinute, early, price, price, price, price, quantity), candle],
            CallException = callException,
            EnumerationException = enumerationException
        };
    }
}
