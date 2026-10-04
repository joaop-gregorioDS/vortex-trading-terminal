using VortexTrading.Application.IndicatorConsumption;
using VortexTrading.Application.Indicators;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Application.Tests.IndicatorConsumption;

public sealed class IndicatorBatchCalculatorTests
{
    private static readonly InstrumentId Instrument = new(new Guid("11111111-1111-1111-1111-111111111111"));
    private static readonly UtcInstant Start = new(
        new DateTimeOffset(2026, 10, 4, 10, 0, 0, TimeSpan.Zero).AddTicks(1234567));

    public static TheoryData<IndicatorSpecification, IIndicator> SingleMappings => new()
    {
        { new SmaSpecification(4), new SimpleMovingAverage(4) },
        { new EmaSpecification(4), new ExponentialMovingAverage(4) },
        { new RsiSpecification(4), new RelativeStrengthIndex(4) },
        { new AtrSpecification(4), new AverageTrueRange(4) },
        { new VwapSpecification(), new VolumeWeightedAveragePrice() },
        { new ObvSpecification(), new OnBalanceVolume() }
    };

    public static TheoryData<IndicatorSpecification, IMultiSeriesIndicator, string[]> MultiMappings => new()
    {
        { new MacdSpecification(2, 7, 3), new MovingAverageConvergenceDivergence(2, 7, 3), ["MACD", "Signal", "Histogram"] },
        { new BollingerBandsSpecification(4, 2.125m), new BollingerBands(4, 2.125m), ["Middle", "Upper", "Lower"] },
        { new StochasticSpecification(5, 2, 3), new StochasticOscillator(5, 2, 3), ["%K", "%D"] },
        { new AdxSpecification(4), new AverageDirectionalIndex(4), ["+DI", "-DI", "ADX"] }
    };

    [Theory]
    [MemberData(nameof(SingleMappings))]
    public void EverySingleMappingMatchesTheFrozenOracleAndExposesOnlyValue(
        IndicatorSpecification specification, IIndicator oracle)
    {
        var candles = Sequence(60);
        var expected = oracle.Calculate(candles);
        var actual = new IndicatorBatchCalculator().Calculate(specification, candles);

        var entry = Assert.Single(actual.Series);
        Assert.Equal(1, actual.Count);
        Assert.Equal("Value", entry.Name);
        AssertSeries(expected, entry.Series);
        Assert.True(actual.TryGetSeries("Value", out var lookup));
        Assert.Same(entry.Series, lookup);
        Assert.False(actual.TryGetSeries("value", out _));
    }

    [Theory]
    [MemberData(nameof(MultiMappings))]
    public void EveryMultiMappingPreservesTheFrozenOracleOutputsWithoutRenamingOrAligning(
        IndicatorSpecification specification, IMultiSeriesIndicator oracle, string[] names)
    {
        var candles = Sequence(60);
        var expected = oracle.Calculate(candles);
        var actual = new IndicatorBatchCalculator().Calculate(specification, candles);

        Assert.Equal(names, actual.Series.Select(item => item.Name).ToArray());
        AssertResult(expected, actual);
        foreach (var entry in actual.Series)
        {
            Assert.True(actual.TryGetSeries(entry.Name, out var lookup));
            Assert.Same(entry.Series, lookup);
        }
    }

    [Theory]
    [MemberData(nameof(SingleMappings))]
    public void EmptySingleBatchRetainsOneEmptyValueSeries(
        IndicatorSpecification specification, IIndicator oracle)
    {
        var result = new IndicatorBatchCalculator().Calculate(specification, []);

        var entry = Assert.Single(result.Series);
        Assert.Equal("Value", entry.Name);
        AssertSeries(oracle.Calculate([]), entry.Series);
        Assert.Empty(entry.Series.Points);
    }

    [Theory]
    [MemberData(nameof(MultiMappings))]
    public void EmptyMultiBatchRetainsAllPublicComponents(
        IndicatorSpecification specification, IMultiSeriesIndicator oracle, string[] names)
    {
        var result = new IndicatorBatchCalculator().Calculate(specification, []);

        Assert.Equal(names, result.Series.Select(item => item.Name).ToArray());
        AssertResult(oracle.Calculate([]), result);
        Assert.All(result.Series, entry => Assert.Empty(entry.Series.Points));
    }

    public static TheoryData<IndicatorSpecification, int, string[], int[]> WarmUpCases => new()
    {
        { new SmaSpecification(4), 3, ["Value"], [0] },
        { new EmaSpecification(4), 3, ["Value"], [0] },
        { new RsiSpecification(4), 4, ["Value"], [0] },
        { new AtrSpecification(4), 3, ["Value"], [0] },
        { new MacdSpecification(2, 7, 3), 6, ["MACD", "Signal", "Histogram"], [0, 0, 0] },
        { new MacdSpecification(2, 7, 3), 8, ["MACD", "Signal", "Histogram"], [2, 0, 0] },
        { new MacdSpecification(2, 7, 3), 9, ["MACD", "Signal", "Histogram"], [3, 1, 1] },
        { new BollingerBandsSpecification(4, 2m), 3, ["Middle", "Upper", "Lower"], [0, 0, 0] },
        { new StochasticSpecification(5, 2, 3), 5, ["%K", "%D"], [0, 0] },
        { new StochasticSpecification(5, 2, 3), 7, ["%K", "%D"], [2, 0] },
        { new StochasticSpecification(5, 2, 3), 8, ["%K", "%D"], [3, 1] },
        { new AdxSpecification(4), 4, ["+DI", "-DI", "ADX"], [0, 0, 0] },
        { new AdxSpecification(4), 7, ["+DI", "-DI", "ADX"], [3, 3, 0] },
        { new AdxSpecification(4), 8, ["+DI", "-DI", "ADX"], [4, 4, 1] },
        { new VwapSpecification(), 1, ["Value"], [1] },
        { new ObvSpecification(), 1, ["Value"], [1] }
    };

    [Theory]
    [MemberData(nameof(WarmUpCases))]
    public void WarmUpsRemainIndependentWithoutPaddingOrDelayingAvailableComponents(
        IndicatorSpecification specification, int count, string[] names, int[] expectedCounts)
    {
        var candles = Sequence(count);
        var result = new IndicatorBatchCalculator().Calculate(specification, candles);

        Assert.Equal(names, result.Series.Select(entry => entry.Name).ToArray());
        Assert.Equal(expectedCounts, result.Series.Select(entry => entry.Series.Count).ToArray());
        foreach (var entry in result.Series.Where(entry => entry.Series.Count > 0))
            Assert.Equal(candles[^1].CloseTime, entry.Series.Points[^1].Time);
    }

    [Fact]
    public void MultipleSmaAndEmaConfigurationsCoexistWithoutNameBasedSelection()
    {
        var calculator = new IndicatorBatchCalculator();
        var candles = Sequence(60);
        var specifications = new IndicatorSpecification[]
        {
            new SmaSpecification(20), new SmaSpecification(50), new EmaSpecification(20)
        };
        var results = specifications.ToDictionary(s => s, s => calculator.Calculate(s, candles));

        Assert.Equal(3, results.Count);
        AssertSeries(new SimpleMovingAverage(20).Calculate(candles), results[new SmaSpecification(20)].Series[0].Series);
        AssertSeries(new SimpleMovingAverage(50).Calculate(candles), results[new SmaSpecification(50)].Series[0].Series);
        AssertSeries(new ExponentialMovingAverage(20).Calculate(candles), results[new EmaSpecification(20)].Series[0].Series);
        Assert.Equal(41, results[new SmaSpecification(20)].Series[0].Series.Count);
        Assert.Equal(11, results[new SmaSpecification(50)].Series[0].Series.Count);
        Assert.NotEqual(results[new SmaSpecification(20)].Series[0].Series.Points[^1].Value,
            results[new EmaSpecification(20)].Series[0].Series.Points[^1].Value);
    }

    [Fact]
    public void MacdParameterPositionsAreNotInterchangeable()
    {
        var candles = Sequence(30);
        var actual = new IndicatorBatchCalculator().Calculate(new MacdSpecification(2, 7, 3), candles);
        var expected = new MovingAverageConvergenceDivergence(2, 7, 3).Calculate(candles);
        var exchanged = new MovingAverageConvergenceDivergence(3, 7, 2).Calculate(candles);

        AssertResult(expected, actual);
        Assert.NotEqual(exchanged.Series[1].Series.Count, actual.Series[1].Series.Count);
        Assert.NotEqual(exchanged.Series[0].Series.Points[^1].Value, actual.Series[0].Series.Points[^1].Value);
        Assert.Throws<ArgumentException>(() => new MovingAverageConvergenceDivergence(7, 2, 3));
    }

    [Fact]
    public void StochasticParameterPositionsAreNotInterchangeable()
    {
        var candles = Sequence(30);
        var actual = new IndicatorBatchCalculator().Calculate(new StochasticSpecification(5, 2, 3), candles);

        AssertResult(new StochasticOscillator(5, 2, 3).Calculate(candles), actual);
        var exchangedSmoothing = new StochasticOscillator(5, 3, 2).Calculate(candles);
        var exchangedRaw = new StochasticOscillator(2, 5, 3).Calculate(candles);
        Assert.NotEqual(exchangedSmoothing.Series[0].Series.Count, actual.Series[0].Series.Count);
        Assert.NotEqual(exchangedRaw.Series[0].Series.Points[^1].Value, actual.Series[0].Series.Points[^1].Value);
    }

    [Fact]
    public void BollingerMultiplierPreservesDecimalDigitsBeyondBinaryFloatingPointPrecision()
    {
        const decimal multiplier = 2.000000000000000000000000001m;
        var specification = new BollingerBandsSpecification(2, multiplier);
        var candles = FlatCandles(2m, 4m);
        var result = new IndicatorBatchCalculator().Calculate(specification, candles);

        Assert.Equal(multiplier, specification.StandardDeviationMultiplier);
        AssertResult(new BollingerBands(2, multiplier).Calculate(candles), result);
        Assert.Equal(3m, Assert.Single(result.Series[0].Series.Points).Value);
        Assert.Equal(5.000000000000000000000000001m, Assert.Single(result.Series[1].Series.Points).Value);
        Assert.Equal(0.999999999999999999999999999m, Assert.Single(result.Series[2].Series.Points).Value);
    }

    public static TheoryData<IndicatorSpecification, string> InvalidConfigurations => new()
    {
        { new SmaSpecification(0), "period" },
        { new EmaSpecification(-1), "period" },
        { new RsiSpecification(0), "period" },
        { new AtrSpecification(0), "period" },
        { new AdxSpecification(0), "period" },
        { new MacdSpecification(0, 7, 3), "fastPeriod" },
        { new MacdSpecification(2, 0, 3), "slowPeriod" },
        { new MacdSpecification(2, 7, 0), "signalPeriod" },
        { new StochasticSpecification(0, 2, 3), "kPeriod" },
        { new StochasticSpecification(5, 0, 3), "kSmoothingPeriod" },
        { new StochasticSpecification(5, 2, 0), "dPeriod" },
        { new BollingerBandsSpecification(0, 2m), "period" },
        { new BollingerBandsSpecification(2, 0m), "standardDeviationMultiplier" }
    };

    [Theory]
    [MemberData(nameof(InvalidConfigurations))]
    public void MathematicalValidationIsDeferredToTheFrozenConstructorBeforeBatchAccess(
        IndicatorSpecification specification, string parameter)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new IndicatorBatchCalculator().Calculate(specification, null!));

        Assert.Equal(parameter, exception.ParamName);
    }

    [Fact]
    public void InvalidMacdOrderingUsesTheFrozenConstructorException()
    {
        var specification = new MacdSpecification(7, 2, 3);
        var direct = Assert.Throws<ArgumentException>(() => new MovingAverageConvergenceDivergence(7, 2, 3));
        var actual = Assert.Throws<ArgumentException>(() => new IndicatorBatchCalculator().Calculate(specification, []));

        Assert.Equal(direct.ParamName, actual.ParamName);
        Assert.Equal(direct.Message, actual.Message);
    }

    [Fact]
    public void NullSpecificationFailsExplicitlyBeforeDispatch()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new IndicatorBatchCalculator().Calculate(null!, null!));

        Assert.Equal("specification", exception.ParamName);
    }

    [Fact]
    public void NullCandlesRetainTheFrozenEngineArgumentSemantics()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new IndicatorBatchCalculator().Calculate(new SmaSpecification(2), null!));

        Assert.Equal("candles", exception.ParamName);
    }

    [Fact]
    public void UnsupportedSubtypeCannotSelectAnIndicatorBySpoofingItsMachineId()
    {
        Assert.Throws<NotSupportedException>(() =>
            new IndicatorBatchCalculator().Calculate(new UnsupportedSpecification(), Sequence(3)));
    }

    [Theory]
    [InlineData("null element")]
    [InlineData("duplicate time")]
    [InlineData("backward time")]
    [InlineData("mixed instrument")]
    [InlineData("mixed interval")]
    public void InvalidBatchesPropagateWithoutSortingDeduplicatingOrRepairing(string problem)
    {
        var candles = Sequence(3);
        candles[1] = problem switch
        {
            "null element" => null!,
            "duplicate time" => candles[0],
            "backward time" => Create(12m, new UtcInstant(Start.Value.AddMinutes(-1))),
            "mixed instrument" => Create(12m, candles[1].OpenTime,
                id: new InstrumentId(new Guid("22222222-2222-2222-2222-222222222222"))),
            "mixed interval" => Create(12m, candles[1].OpenTime, interval: CandleInterval.FiveMinutes),
            _ => throw new InvalidOperationException()
        };
        // A deliberately incomplete warm-up must not hide invalid input.
        var direct = Assert.Throws<ArgumentException>(() => new SimpleMovingAverage(20).Calculate(candles));
        var actual = Assert.Throws<ArgumentException>(() =>
            new IndicatorBatchCalculator().Calculate(new SmaSpecification(20), candles));

        Assert.Equal(direct.ParamName, actual.ParamName);
        Assert.Equal(direct.Message, actual.Message);
    }

    [Fact]
    public void TrueDecimalOverflowPropagatesAndDoesNotContaminateTheNextCalculation()
    {
        var calculator = new IndicatorBatchCalculator();
        var specification = new SmaSpecification(2);
        var overflowing = FlatCandles(decimal.MaxValue, decimal.MaxValue);
        Assert.Throws<OverflowException>(() => new SimpleMovingAverage(2).Calculate(overflowing));
        Assert.Throws<OverflowException>(() => calculator.Calculate(specification, overflowing));

        var valid = FlatCandles(2m, 4m);
        AssertSeries(new SimpleMovingAverage(2).Calculate(valid), calculator.Calculate(specification, valid).Series[0].Series);
    }

    [Theory]
    [MemberData(nameof(SingleMappings))]
    public void SingleCalculationsRemainIndependentAcrossRepeatedAndUnrelatedBatches(
        IndicatorSpecification specification, IIndicator oracle)
    {
        var calculator = new IndicatorBatchCalculator();
        var candles = Sequence(30);
        var first = calculator.Calculate(specification, candles);
        calculator.Calculate(new VwapSpecification(), FlatCandles(2m, 4m));
        var second = calculator.Calculate(specification, candles);

        Assert.NotSame(first, second);
        AssertResult(first, second);
        AssertSeries(oracle.Calculate(candles), second.Series[0].Series);
    }

    [Theory]
    [MemberData(nameof(MultiMappings))]
    public void MultiCalculationsRemainIndependentAcrossRepeatedCallsAndPreviousValidationFailure(
        IndicatorSpecification specification, IMultiSeriesIndicator oracle, string[] names)
    {
        var calculator = new IndicatorBatchCalculator();
        var candles = Sequence(30);
        var first = calculator.Calculate(specification, candles);
        Assert.Throws<ArgumentException>(() => calculator.Calculate(specification, [candles[0], candles[0]]));
        var second = calculator.Calculate(specification, candles);

        Assert.NotSame(first, second);
        Assert.Equal(names, second.Series.Select(item => item.Name).ToArray());
        AssertResult(first, second);
        AssertResult(oracle.Calculate(candles), second);
    }

    [Fact]
    public async Task ConcurrentCallsOnOneCalculatorHaveIndependentResultsForStableBatches()
    {
        var calculator = new IndicatorBatchCalculator();
        var candles = Array.AsReadOnly(Sequence(60));
        var specification = new BollingerBandsSpecification(5, 2.125m);
        var expected = new BollingerBands(5, 2.125m).Calculate(candles);

        var results = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() => calculator.Calculate(specification, candles))));

        Assert.All(results, result => AssertResult(expected, result));
    }

    [Fact]
    public void SpecificationsCandlesAndResultRemainIndependentOfLaterSourceCollectionMutation()
    {
        var specification = new BollingerBandsSpecification(4, 2.125m);
        var originalSpecification = new BollingerBandsSpecification(4, 2.125m);
        var candles = Sequence(12).ToList();
        var original = candles.ToArray();
        var expected = new BollingerBands(4, 2.125m).Calculate(original);
        var result = new IndicatorBatchCalculator().Calculate(specification, candles);

        Assert.Equal(originalSpecification, specification);
        Assert.Equal(original.Length, candles.Count);
        for (var index = 0; index < original.Length; index++) Assert.Same(original[index], candles[index]);
        AssertResult(expected, result);
        var retained = result.Series[0].Series.Points[0];
        candles.Clear();
        AssertResult(expected, result);
        Assert.Same(retained, result.Series[0].Series.Points[0]);
        Assert.Throws<NotSupportedException>(() => ((IList<NamedIndicatorSeries>)result.Series).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<IndicatorPoint>)result.Series[0].Series.Points).Clear());
    }

    [Fact]
    public void BatchAnchorRatherThanUtcDateOrGapControlsVwapAndObv()
    {
        var calculator = new IndicatorBatchCalculator();
        var candles = new[]
        {
            Create(2m, new UtcInstant(new DateTimeOffset(2026, 10, 3, 23, 58, 0, TimeSpan.Zero))),
            Create(4m, new UtcInstant(new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero)))
        };

        var vwap = calculator.Calculate(new VwapSpecification(), candles).Series[0].Series;
        var obv = calculator.Calculate(new ObvSpecification(), candles).Series[0].Series;
        Assert.Equal(new[] { 2m, 3m }, vwap.Points.Select(point => point.Value).ToArray());
        Assert.Equal(new[] { 0m, 1m }, obv.Points.Select(point => point.Value).ToArray());
        Assert.Equal(4m, Assert.Single(calculator.Calculate(new VwapSpecification(), [candles[1]]).Series[0].Series.Points).Value);
        Assert.Equal(0m, Assert.Single(calculator.Calculate(new ObvSpecification(), [candles[1]]).Series[0].Series.Points).Value);
    }

    private static void AssertResult(MultiSeriesIndicatorResult expected, MultiSeriesIndicatorResult actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var index = 0; index < expected.Count; index++)
        {
            Assert.Equal(expected.Series[index].Name, actual.Series[index].Name);
            AssertSeries(expected.Series[index].Series, actual.Series[index].Series);
        }
    }

    private static void AssertSeries(IndicatorSeries expected, IndicatorSeries actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        Assert.Equal(expected.Points.ToArray(), actual.Points.ToArray());
    }

    private static Candle[] Sequence(int count) => Enumerable.Range(0, count).Select(index =>
    {
        var close = 10m + index * index % 17 + index * 0.125m;
        return new Candle(Instrument, CandleInterval.OneMinute, new UtcInstant(Start.Value.AddMinutes(index * 2)),
            new Price(close + 0.5m), new Price(close + 2m + index % 4 * 0.5m), new Price(close - 3m),
            new Price(close), new Quantity(index % 5 == 0 ? 0m : (index + 1) * 0.125m));
    }).ToArray();

    private static Candle[] FlatCandles(params decimal[] closes) => closes
        .Select((close, index) => Create(close, new UtcInstant(Start.Value.AddMinutes(index)))).ToArray();

    private static Candle Create(decimal close, UtcInstant time, InstrumentId? id = null, CandleInterval? interval = null)
    {
        var price = new Price(close);
        return new Candle(id ?? Instrument, interval ?? CandleInterval.OneMinute, time,
            price, price, price, price, new Quantity(1m));
    }

    // Records expose a protected copy constructor; an unsupported subtype must still fail closed.
    private sealed record UnsupportedSpecification : IndicatorSpecification
    {
        public override string MachineId => "sma";

        public UnsupportedSpecification() : base(new SmaSpecification(1)) { }
    }
}
