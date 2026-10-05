using System.Runtime.CompilerServices;
using VortexTrading.Application.Charts;
using VortexTrading.Application.IndicatorConsumption;
using VortexTrading.Application.MarketData;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Application.Tests;

public sealed class HistoricalChartQueryTests
{
    private static readonly InstrumentId A = new(new Guid("11111111-1111-1111-1111-111111111111"));
    private static readonly HistoricalChartSourceContext Source = new(A, CandleInterval.OneMinute, "test-history", true);
    private static readonly IndicatorSpecification[] Specifications = [new SmaSpecification(20), new MacdSpecification(12, 26, 9)];

    [Fact]
    public async Task TwoHundredBarsHaveOriginalCandlesAndExactBoundaryOutputs()
    {
        var candles = Candles(200);
        var provider = new Provider(candles);
        var snapshot = await new HistoricalChartQuery(provider, Source).ExecuteAsync(Request(200), CancellationToken.None);
        Assert.Same(Source, snapshot.Context);
        Assert.Equal(200, snapshot.Candles.Count);
        Assert.Equal(200, provider.Delivered);
        for (var index = 0; index < candles.Length; index++) Assert.Same(candles[index], snapshot.Candles[index]);
        Assert.Equal(new[] { "Value" }, snapshot.Indicators[0].Result.Series.Select(series => series.Name));
        Assert.Equal(new[] { "MACD", "Signal", "Histogram" }, snapshot.Indicators[1].Result.Series.Select(series => series.Name));
        Assert.Equal(new[] { 181, 175, 167, 167 }, snapshot.Indicators.SelectMany(i => i.Result.Series).Select(s => s.Series.Count));
        foreach (var indicator in snapshot.Indicators)
        {
            var expected = new IndicatorBatchCalculator().Calculate(indicator.Specification, snapshot.Candles);
            for (var series = 0; series < expected.Count; series++)
            {
                Assert.Equal(expected.Series[series].Name, indicator.Result.Series[series].Name);
                Assert.Equal(expected.Series[series].Series.Points, indicator.Result.Series[series].Series.Points);
            }
        }
        Assert.Throws<NotSupportedException>(() => ((IList<Candle>)snapshot.Candles).Clear());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(19)]
    [InlineData(20)]
    [InlineData(26)]
    [InlineData(34)]
    [InlineData(199)]
    public async Task LimitsChooseTheFirstNFromTheAnchorAndNeverCalculateHiddenHistory(int count)
    {
        var provider = new Provider(Candles(200));
        var snapshot = await new HistoricalChartQuery(provider, Source).ExecuteAsync(Request(count), CancellationToken.None);
        Assert.Equal(count, snapshot.Candles.Count);
        Assert.Equal(count, provider.Delivered);
        Assert.Equal(Math.Max(0, count - 19), snapshot.Indicators[0].Result.Series[0].Series.Count);
        Assert.Equal(Math.Max(0, count - 25), snapshot.Indicators[1].Result.Series[0].Series.Count);
        Assert.Equal(Math.Max(0, count - 33), snapshot.Indicators[1].Result.Series[1].Series.Count);
        Assert.Equal(provider.Candles[0].OpenTime, snapshot.Candles[0].OpenTime);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(201)]
    public async Task InvalidBarLimitsFailBeforeSourceConsumption(int count)
    {
        var provider = new Provider(Candles(200));
        var failure = await Assert.ThrowsAsync<HistoricalChartException>(() =>
            new HistoricalChartQuery(provider, Source).ExecuteAsync(Request(count), CancellationToken.None));
        Assert.Equal(HistoricalChartFailure.ResourceLimit, failure.Failure);
        Assert.Equal(0, provider.Delivered);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnknownInstrumentOrIntervalDoesNotFallBack(bool interval)
    {
        var provider = new Provider(Candles(200));
        var request = Request(200) with
        {
            InstrumentId = interval ? A : new InstrumentId(new Guid("22222222-2222-2222-2222-222222222222")),
            Interval = interval ? CandleInterval.FiveMinutes : CandleInterval.OneMinute
        };
        var failure = await Assert.ThrowsAsync<HistoricalChartException>(() =>
            new HistoricalChartQuery(provider, Source).ExecuteAsync(request, CancellationToken.None));
        Assert.Equal(HistoricalChartFailure.ContextNotFound, failure.Failure);
        Assert.Equal(0, provider.Delivered);
    }

    [Fact]
    public async Task EmptyMatchingHistoryIsExplicitlyUnavailable()
    {
        var failure = await Assert.ThrowsAsync<HistoricalChartException>(() =>
            new HistoricalChartQuery(new Provider([]), Source).ExecuteAsync(Request(200), CancellationToken.None));
        Assert.Equal(HistoricalChartFailure.ContextNotFound, failure.Failure);
    }

    [Fact]
    public async Task SourceIoFailureIsUnavailableWithoutLeakingItsMessage()
    {
        var failure = await Assert.ThrowsAsync<HistoricalChartException>(() =>
            new HistoricalChartQuery(new Provider([]) { Unavailable = true }, Source).ExecuteAsync(Request(200), CancellationToken.None));
        Assert.Equal(HistoricalChartFailure.SourceUnavailable, failure.Failure);
        Assert.DoesNotContain("private", failure.Message);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("backward")]
    [InlineData("wrong context")]
    public async Task InvalidServerSequencePropagatesAsDatasetFailure(string kind)
    {
        var candles = Candles(2);
        if (kind == "duplicate") candles[1] = candles[0];
        if (kind == "backward") Array.Reverse(candles);
        var source = kind == "wrong context" ? Source with { InstrumentId = new InstrumentId(Guid.NewGuid()) } : Source;
        var request = Request(200) with { InstrumentId = source.InstrumentId };
        var failure = await Assert.ThrowsAsync<ArgumentException>(() =>
            new HistoricalChartQuery(new Provider(candles), source).ExecuteAsync(request, CancellationToken.None));
        Assert.Equal("candles", failure.ParamName);
    }

    [Theory]
    [InlineData("sma zero")]
    [InlineData("sma negative")]
    [InlineData("macd order")]
    [InlineData("macd zero")]
    public async Task FrozenConstructorsDetermineMathematicalValidity(string kind)
    {
        IndicatorSpecification specification = kind switch
        {
            "sma zero" => new SmaSpecification(0),
            "sma negative" => new SmaSpecification(-1),
            "macd zero" => new MacdSpecification(0, 26, 9),
            _ => new MacdSpecification(26, 12, 9)
        };
        var failure = await Assert.ThrowsAsync<HistoricalChartException>(() =>
            new HistoricalChartQuery(new Provider(Candles(2)), Source).ExecuteAsync(
                Request(2) with { Indicators = [specification] }, CancellationToken.None));
        Assert.Equal(HistoricalChartFailure.InvalidConfiguration, failure.Failure);
    }

    [Fact]
    public async Task UpperPeriodCeilingAndIndicatorCountAreResourcePolicy()
    {
        var provider = new Provider(Candles(200));
        foreach (var specifications in new IndicatorSpecification[][]
        {
            [], [new SmaSpecification(201)], [new MacdSpecification(12, 26, 201)],
            [new SmaSpecification(1), new SmaSpecification(2), new SmaSpecification(3)], [new EmaSpecification(20)]
        })
        {
            var failure = await Assert.ThrowsAsync<HistoricalChartException>(() =>
                new HistoricalChartQuery(provider, Source).ExecuteAsync(Request(200) with { Indicators = specifications }, CancellationToken.None));
            Assert.Equal(HistoricalChartFailure.ResourceLimit, failure.Failure);
        }
        Assert.Equal(0, provider.Delivered);
    }

    [Fact]
    public async Task OverflowDoesNotProduceAPartialSnapshot()
    {
        var price = new Price(decimal.MaxValue);
        var candles = Candles(2).Select(c => new Candle(A, c.Interval, c.OpenTime, price, price, price, price, c.Volume)).ToArray();
        var failure = await Assert.ThrowsAsync<HistoricalChartException>(() =>
            new HistoricalChartQuery(new Provider(candles), Source).ExecuteAsync(
                Request(2) with { Indicators = [new SmaSpecification(2)] }, CancellationToken.None));
        Assert.Equal(HistoricalChartFailure.CalculationUnavailable, failure.Failure);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationBeforeAndDuringAcquisitionPropagates(bool during)
    {
        using var cancellation = new CancellationTokenSource();
        var provider = new Provider(Candles(200)) { CancelAfterFirst = during ? cancellation : null };
        if (!during) cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new HistoricalChartQuery(provider, Source).ExecuteAsync(Request(200), cancellation.Token));
        Assert.Equal(during ? 1 : 0, provider.Delivered);
    }

    private static HistoricalChartRequest Request(int count) => new(A, CandleInterval.OneMinute, count, Specifications);
    private static Candle[] Candles(int count) => Enumerable.Range(0, count).Select(index =>
    {
        var close = new Price(10m + index % 5 + index * 0.1m);
        return new Candle(A, CandleInterval.OneMinute,
            new UtcInstant(new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero).AddMinutes(index)),
            close, new Price(close.Value + 1m), new Price(close.Value - 1m), close, new Quantity(index + 1m));
    }).ToArray();

    private sealed class Provider(Candle[] candles) : IMarketDataProvider
    {
        public Candle[] Candles => candles;
        public int Delivered { get; private set; }
        public bool Unavailable { get; init; }
        public CancellationTokenSource? CancelAfterFirst { get; init; }
        public MarketDataProviderDescriptor Descriptor { get; } = new("test", "Test history", MarketDataCapabilities.Candles);
        public IAsyncEnumerable<Quote> StreamQuotesAsync(IReadOnlyCollection<InstrumentId> ids, CancellationToken token) => throw new NotSupportedException();
        public IAsyncEnumerable<Trade> StreamTradesAsync(IReadOnlyCollection<InstrumentId> ids, CancellationToken token) => throw new NotSupportedException();
        public async IAsyncEnumerable<Candle> StreamCandlesAsync(IReadOnlyCollection<InstrumentId> ids,
            CandleInterval interval, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            if (Unavailable) throw new IOException("private source details");
            foreach (var candle in candles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Delivered++;
                yield return candle;
                if (Delivered == 1) CancelAfterFirst?.Cancel();
            }
        }
    }
}
