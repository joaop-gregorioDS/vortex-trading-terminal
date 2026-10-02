using VortexTrading.Application.MarketData;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Trading;
using VortexTrading.Infrastructure.MarketData.Simulation;

namespace VortexTrading.Infrastructure.Tests;

public sealed class SimulationMarketDataProviderTests
{
    private static readonly InstrumentId A = new(new Guid("11111111-1111-1111-1111-111111111111"));
    private static readonly InstrumentId B = new(new Guid("22222222-2222-2222-2222-222222222222"));
    private static readonly InstrumentId C = new(new Guid("33333333-3333-3333-3333-333333333333"));
    private static readonly UtcInstant Start = new(new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero).AddTicks(1234567));

    public static TheoryData<decimal> PreciseValues => new()
    {
        10.0500m,
        0.00100m,
        0.0000000000000000000000000001m,
        7.1234567890123456789012345678m,
        decimal.MaxValue
    };

    [Fact]
    public void DescriptorHasStableIdentityAndAllThreeCanonicalCapabilities()
    {
        var provider = CreateProvider();

        Assert.Equal("simulation", provider.Descriptor.Key);
        Assert.Equal("Simulation", provider.Descriptor.DisplayName);
        Assert.Equal(MarketDataCapabilities.Quotes | MarketDataCapabilities.Trades | MarketDataCapabilities.Candles,
            provider.Descriptor.Capabilities);
        Assert.True(provider.Descriptor.Capabilities.HasFlag(MarketDataCapabilities.Quotes));
        Assert.True(provider.Descriptor.Capabilities.HasFlag(MarketDataCapabilities.Trades));
        Assert.True(provider.Descriptor.Capabilities.HasFlag(MarketDataCapabilities.Candles));
        Assert.Same(provider.Descriptor, provider.Descriptor);
    }

    [Fact]
    public void ProviderRejectsNullOptions()
        => Assert.Throws<ArgumentNullException>(() => new SimulationMarketDataProvider(null));

    [Theory]
    [InlineData("startTime")]
    [InlineData("price")]
    [InlineData("quantity")]
    public void OptionsRejectEachMissingRequiredComponent(string component)
    {
        var error = Assert.Throws<ArgumentNullException>(() => new SimulationMarketDataOptions(
            component == "startTime" ? null : Start,
            component == "price" ? null : new Price(10m),
            component == "quantity" ? null : new Quantity(1m), 3));

        Assert.Equal(component, error.ParamName);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OptionsRejectZeroPriceOrQuantity(bool zeroPrice)
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => new SimulationMarketDataOptions(
            Start, new Price(zeroPrice ? 0m : 10m), new Quantity(zeroPrice ? 1m : 0m), 3));

        Assert.Equal(zeroPrice ? "price" : "quantity", error.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void OptionsRejectNonPositiveEventCount(int count)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new SimulationMarketDataOptions(Start, new Price(10m), new Quantity(1m), count));

    [Fact]
    public void MaximumFiniteEventCountIsValidWithoutAllocatingTheSequence()
    {
        var options = new SimulationMarketDataOptions(new UtcInstant(DateTimeOffset.MinValue), new Price(10m), new Quantity(1m), int.MaxValue);

        Assert.Equal(int.MaxValue, options.EventCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(TimeSpan.TicksPerSecond - 1)]
    public void OptionsRejectUnrepresentableQuoteAndTradeProgression(long ticksBeforeMaximum)
    {
        var start = new UtcInstant(DateTimeOffset.MaxValue.AddTicks(-ticksBeforeMaximum));

        Assert.Throws<ArgumentOutOfRangeException>(() => new SimulationMarketDataOptions(start, new Price(10m), new Quantity(1m), 2));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task QuoteAndTradeTimesAllowTheExactMaximumBoundary(int count)
    {
        var start = new UtcInstant(DateTimeOffset.MaxValue.AddTicks(-(long)(count - 1) * TimeSpan.TicksPerSecond));
        var provider = new SimulationMarketDataProvider(new SimulationMarketDataOptions(start, new Price(10m), new Quantity(1m), count));

        var quotes = await ReadAllAsync(provider.StreamQuotesAsync([A], CancellationToken.None));
        var trades = await ReadAllAsync(provider.StreamTradesAsync([A], CancellationToken.None));

        Assert.Equal(count, quotes.Length);
        Assert.Equal(count, trades.Length);
        Assert.Equal(DateTimeOffset.MaxValue, quotes[^1].EventTime.Value);
        Assert.Equal(DateTimeOffset.MaxValue, trades[^1].ReceivedTime.Value);
    }

    [Theory]
    [InlineData("quotes", "null")]
    [InlineData("quotes", "empty")]
    [InlineData("quotes", "element")]
    [InlineData("trades", "null")]
    [InlineData("trades", "empty")]
    [InlineData("trades", "element")]
    [InlineData("candles", "null")]
    [InlineData("candles", "empty")]
    [InlineData("candles", "element")]
    public void EveryOperationRejectsInvalidSubscriptionsBeforeEnumeration(string shape, string invalid)
    {
        var provider = CreateProvider();
        IReadOnlyCollection<InstrumentId> ids = invalid switch
        {
            "null" => null!,
            "empty" => Array.Empty<InstrumentId>(),
            _ => new InstrumentId[] { A, null! }
        };
        Action subscribe = () =>
        {
            switch (shape)
            {
                case "quotes": provider.StreamQuotesAsync(ids, CancellationToken.None); break;
                case "trades": provider.StreamTradesAsync(ids, CancellationToken.None); break;
                default: provider.StreamCandlesAsync(ids, CandleInterval.OneMinute, CancellationToken.None); break;
            }
        };

        ArgumentException error = invalid == "null"
            ? Assert.Throws<ArgumentNullException>(subscribe)
            : Assert.Throws<ArgumentException>(subscribe);
        Assert.Equal("instrumentIds", error.ParamName);
    }

    [Fact]
    public void CandlesRejectNullIntervalBeforeEnumeration()
        => Assert.Throws<ArgumentNullException>(() => CreateProvider().StreamCandlesAsync([A], null!, CancellationToken.None));

    [Theory]
    [InlineData(1, true)]
    [InlineData(3, false)]
    [InlineData(int.MaxValue, false)]
    public void CandlesRejectUnrepresentableSeriesBeforeAnyEmission(int count, bool startAtMaximum)
    {
        var start = new UtcInstant(startAtMaximum ? DateTimeOffset.MaxValue : DateTimeOffset.MinValue);
        var interval = new CandleInterval(startAtMaximum ? TimeSpan.FromTicks(1) : TimeSpan.MaxValue);
        var provider = new SimulationMarketDataProvider(new SimulationMarketDataOptions(start, new Price(10m), new Quantity(1m), count));

        var error = Assert.Throws<ArgumentOutOfRangeException>(() => provider.StreamCandlesAsync([A], interval, CancellationToken.None));

        Assert.Equal("interval", error.ParamName);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, TimeSpan.TicksPerSecond)]
    public async Task CandleSeriesAllowsCloseTimeExactlyAtMaximum(int count, long intervalTicks)
    {
        var start = new UtcInstant(DateTimeOffset.MaxValue.AddTicks(-count * intervalTicks));
        var interval = new CandleInterval(TimeSpan.FromTicks(intervalTicks));
        var provider = new SimulationMarketDataProvider(new SimulationMarketDataOptions(start, new Price(10m), new Quantity(1m), count));

        var candles = await ReadAllAsync(provider.StreamCandlesAsync([A], interval, CancellationToken.None));

        Assert.Equal(count, candles.Length);
        Assert.Equal(DateTimeOffset.MaxValue, candles[^1].CloseTime.Value);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task QuotesAreFiniteCanonicalDeterministicAndRepeatable(int instrumentCount)
    {
        var provider = CreateProvider();
        InstrumentId[] ids = instrumentCount == 1 ? [A] : [B, A];
        var stream = provider.StreamQuotesAsync(ids, CancellationToken.None);
        var first = await ReadAllAsync(stream);

        Assert.Equal(3 * instrumentCount, first.Length);
        for (var index = 0; index < first.Length; index++)
        {
            var id = index % instrumentCount == 0 ? A : B;
            var time = new UtcInstant(Start.Value.AddSeconds(index / instrumentCount));
            Assert.Equal(new Quote(id, time, time, new Price(10.0500m), new Quantity(0.00100m),
                new Price(10.0500m), new Quantity(0.00100m)), first[index]);
            Assert.True(first[index].BidPrice.Value > 0 && first[index].BidPrice.Value <= first[index].AskPrice.Value);
            Assert.True(first[index].BidQuantity.Value >= 0 && first[index].AskQuantity.Value >= 0);
        }

        Assert.Equal(first, await ReadAllAsync(provider.StreamQuotesAsync(ids, CancellationToken.None)));
        Assert.Equal(first, await ReadAllAsync(stream));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task TradesAreFiniteCanonicalDeterministicAndRepeatable(int instrumentCount)
    {
        var provider = CreateProvider();
        InstrumentId[] ids = instrumentCount == 1 ? [A] : [B, A];
        var stream = provider.StreamTradesAsync(ids, CancellationToken.None);
        var first = await ReadAllAsync(stream);

        Assert.Equal(3 * instrumentCount, first.Length);
        for (var index = 0; index < first.Length; index++)
        {
            var id = index % instrumentCount == 0 ? A : B;
            var time = new UtcInstant(Start.Value.AddSeconds(index / instrumentCount));
            Assert.Equal(new Trade(id, time, time, new Price(10.0500m), new Quantity(0.00100m)), first[index]);
            Assert.True(first[index].Price.Value > 0 && first[index].Quantity.Value > 0);
        }

        Assert.Equal(first, await ReadAllAsync(provider.StreamTradesAsync(ids, CancellationToken.None)));
        Assert.Equal(first, await ReadAllAsync(stream));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(2, 170000003)]
    [InlineData(2, TimeSpan.TicksPerDay)]
    public async Task CandlesAreFiniteFlatRepeatableAndRespectRequestedInterval(int instrumentCount, long intervalTicks)
    {
        var provider = CreateProvider();
        InstrumentId[] ids = instrumentCount == 1 ? [A] : [B, A];
        var interval = new CandleInterval(TimeSpan.FromTicks(intervalTicks));
        var stream = provider.StreamCandlesAsync(ids, interval, CancellationToken.None);
        var first = await ReadAllAsync(stream);

        Assert.Equal(3 * instrumentCount, first.Length);
        for (var index = 0; index < first.Length; index++)
        {
            var id = index % instrumentCount == 0 ? A : B;
            var openTime = new UtcInstant(Start.Value.AddTicks(index / instrumentCount * intervalTicks));
            var price = new Price(10.0500m);
            Assert.Equal(new Candle(id, interval, openTime, price, price, price, price, new Quantity(0.00100m)), first[index]);
            Assert.Same(interval, first[index].Interval);
            Assert.Equal(openTime.Value.AddTicks(intervalTicks), first[index].CloseTime.Value);
            Assert.True(first[index].Open.Value > 0 && first[index].Low.Value <= first[index].Open.Value
                && first[index].High.Value >= first[index].Close.Value && first[index].Volume.Value >= 0);
        }

        Assert.Equal(first, await ReadAllAsync(provider.StreamCandlesAsync(ids, interval, CancellationToken.None)));
        Assert.Equal(first, await ReadAllAsync(stream));
    }

    [Theory]
    [MemberData(nameof(PreciseValues))]
    public async Task EveryShapePreservesDecimalBitsWithoutArithmeticOrQuantization(decimal value)
    {
        var price = new Price(value);
        var quantity = new Quantity(value);
        var options = new SimulationMarketDataOptions(Start, price, quantity, 1);
        var provider = new SimulationMarketDataProvider(options);
        var quote = Assert.Single(await ReadAllAsync(provider.StreamQuotesAsync([A], CancellationToken.None)));
        var trade = Assert.Single(await ReadAllAsync(provider.StreamTradesAsync([A], CancellationToken.None)));
        var candle = Assert.Single(await ReadAllAsync(provider.StreamCandlesAsync([A], CandleInterval.OneMinute, CancellationToken.None)));

        foreach (var actual in new[] { quote.BidPrice.Value, quote.AskPrice.Value, trade.Price.Value,
            candle.Open.Value, candle.High.Value, candle.Low.Value, candle.Close.Value,
            quote.BidQuantity.Value, quote.AskQuantity.Value, trade.Quantity.Value, candle.Volume.Value })
        {
            Assert.Equal(decimal.GetBits(value), decimal.GetBits(actual));
        }

        Assert.Same(Start, options.StartTime);
        Assert.Same(price, options.Price);
        Assert.Same(quantity, options.Quantity);
    }

    [Theory]
    [InlineData("quotes")]
    [InlineData("trades")]
    [InlineData("candles")]
    public async Task SubscriptionIsCopiedSortedAndDuplicatesDoNotChangeOutput(string shape)
    {
        var provider = CreateProvider();
        switch (shape)
        {
            case "quotes": await VerifySubscriptionSnapshotAsync(provider.StreamQuotesAsync, q => q.InstrumentId); break;
            case "trades": await VerifySubscriptionSnapshotAsync(provider.StreamTradesAsync, t => t.InstrumentId); break;
            default: await VerifySubscriptionSnapshotAsync((ids, token) => provider.StreamCandlesAsync(ids, CandleInterval.OneMinute, token), c => c.InstrumentId); break;
        }
    }

    [Theory]
    [InlineData("quotes")]
    [InlineData("trades")]
    [InlineData("candles")]
    public async Task InterleavedEnumeratorsHaveIndependentProgression(string shape)
    {
        var provider = CreateProvider();
        switch (shape)
        {
            case "quotes": await VerifyIndependentProgressionAsync(() => provider.StreamQuotesAsync([B, A], CancellationToken.None)); break;
            case "trades": await VerifyIndependentProgressionAsync(() => provider.StreamTradesAsync([B, A], CancellationToken.None)); break;
            default: await VerifyIndependentProgressionAsync(() => provider.StreamCandlesAsync([B, A], CandleInterval.OneMinute, CancellationToken.None)); break;
        }
    }

    [Theory]
    [InlineData("quotes", true, false)]
    [InlineData("quotes", false, false)]
    [InlineData("quotes", true, true)]
    [InlineData("quotes", false, true)]
    [InlineData("trades", true, false)]
    [InlineData("trades", false, false)]
    [InlineData("trades", true, true)]
    [InlineData("trades", false, true)]
    [InlineData("candles", true, false)]
    [InlineData("candles", false, false)]
    [InlineData("candles", true, true)]
    [InlineData("candles", false, true)]
    public async Task CancellationBeforeOrDuringEnumerationUsesStandardSemantics(string shape, bool before, bool enumerationToken)
    {
        var provider = CreateProvider();
        switch (shape)
        {
            case "quotes": await VerifyCancellationAsync(token => provider.StreamQuotesAsync([A, B], token), before, enumerationToken); break;
            case "trades": await VerifyCancellationAsync(token => provider.StreamTradesAsync([A, B], token), before, enumerationToken); break;
            default: await VerifyCancellationAsync(token => provider.StreamCandlesAsync([A, B], CandleInterval.OneMinute, token), before, enumerationToken); break;
        }
    }

    [Theory]
    [InlineData("quotes", false)]
    [InlineData("quotes", true)]
    [InlineData("trades", false)]
    [InlineData("trades", true)]
    [InlineData("candles", false)]
    [InlineData("candles", true)]
    public async Task BothMethodAndEnumerationTokensAreHonoredWhenDistinct(string shape, bool cancelEnumerationToken)
    {
        var provider = CreateProvider();
        switch (shape)
        {
            case "quotes": await VerifyCombinedCancellationAsync(token => provider.StreamQuotesAsync([A, B], token), cancelEnumerationToken); break;
            case "trades": await VerifyCombinedCancellationAsync(token => provider.StreamTradesAsync([A, B], token), cancelEnumerationToken); break;
            default: await VerifyCombinedCancellationAsync(token => provider.StreamCandlesAsync([A, B], CandleInterval.OneMinute, token), cancelEnumerationToken); break;
        }
    }

    private static SimulationMarketDataProvider CreateProvider()
        => new(new SimulationMarketDataOptions(Start, new Price(10.0500m), new Quantity(0.00100m), 3));

    private static async Task<T[]> ReadAllAsync<T>(IAsyncEnumerable<T> stream)
    {
        var items = new List<T>();
        await foreach (var item in stream) { items.Add(item); }
        return items.ToArray();
    }

    private static async Task VerifySubscriptionSnapshotAsync<T>(
        Func<IReadOnlyCollection<InstrumentId>, CancellationToken, IAsyncEnumerable<T>> subscribe, Func<T, InstrumentId> idSelector)
    {
        InstrumentId[] input = [B, A, InstrumentId.From(A.Value)];
        var original = input.ToArray();
        var stream = subscribe(input, CancellationToken.None);
        Assert.Equal(original, input);
        Array.Fill(input, C);
        var actual = await ReadAllAsync(stream);
        var expected = await ReadAllAsync(subscribe([A, B], CancellationToken.None));

        Assert.Equal(expected, actual);
        Assert.Equal(new[] { A, B, A, B, A, B }, actual.Select(idSelector));
        Assert.All(input, id => Assert.Equal(C, id));
    }

    private static async Task VerifyIndependentProgressionAsync<T>(Func<IAsyncEnumerable<T>> createStream)
    {
        await using var first = createStream().GetAsyncEnumerator();
        await using var second = createStream().GetAsyncEnumerator();
        var firstItems = new List<T>();
        var secondItems = new List<T>();
        Assert.True(await first.MoveNextAsync());
        firstItems.Add(first.Current);
        Assert.True(await first.MoveNextAsync());
        firstItems.Add(first.Current);
        Assert.True(await second.MoveNextAsync());
        secondItems.Add(second.Current);
        Assert.Equal(firstItems[0], second.Current);
        while (await first.MoveNextAsync()) { firstItems.Add(first.Current); }
        while (await second.MoveNextAsync()) { secondItems.Add(second.Current); }

        Assert.Equal(6, firstItems.Count);
        Assert.Equal(firstItems, secondItems);
        Assert.Equal(firstItems.ToArray(), await ReadAllAsync(createStream()));
    }

    private static async Task VerifyCancellationAsync<T>(
        Func<CancellationToken, IAsyncEnumerable<T>> subscribe, bool before, bool enumerationToken)
    {
        using var cancellation = new CancellationTokenSource();
        if (before) { cancellation.Cancel(); }
        var stream = subscribe(enumerationToken ? CancellationToken.None : cancellation.Token);
        await using var enumerator = stream.GetAsyncEnumerator(enumerationToken ? cancellation.Token : CancellationToken.None);
        if (!before) { Assert.True(await enumerator.MoveNextAsync()); }
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => { await enumerator.MoveNextAsync(); });
    }

    private static async Task VerifyCombinedCancellationAsync<T>(
        Func<CancellationToken, IAsyncEnumerable<T>> subscribe, bool cancelEnumerationToken)
    {
        using var methodCancellation = new CancellationTokenSource();
        using var enumerationCancellation = new CancellationTokenSource();
        await using var enumerator = subscribe(methodCancellation.Token).GetAsyncEnumerator(enumerationCancellation.Token);
        Assert.True(await enumerator.MoveNextAsync());
        (cancelEnumerationToken ? enumerationCancellation : methodCancellation).Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => { await enumerator.MoveNextAsync(); });
    }
}
