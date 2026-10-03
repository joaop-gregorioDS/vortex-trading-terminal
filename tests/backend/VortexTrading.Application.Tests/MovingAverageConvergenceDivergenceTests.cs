using VortexTrading.Application.Indicators;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Application.Tests;

public sealed class MovingAverageConvergenceDivergenceTests
{
    private static readonly InstrumentId A = new(new Guid("11111111-1111-1111-1111-111111111111"));
    private static readonly InstrumentId B = new(new Guid("22222222-2222-2222-2222-222222222222"));
    private static readonly UtcInstant Start = new(new DateTimeOffset(2026, 10, 3, 10, 0, 0, TimeSpan.Zero));

    [Theory]
    [InlineData(2, 3, 2)]
    [InlineData(12, 26, 9)]
    [InlineData(1, 2, 1)]
    [InlineData(1, int.MaxValue, int.MaxValue)]
    public void PreservesValidPeriodsAndImplementsTheIndependentMultiSeriesContract(int fast, int slow, int signal)
    {
        var macd = new MovingAverageConvergenceDivergence(fast, slow, signal);
        IMultiSeriesIndicator indicator = macd;

        Assert.Equal("MACD", indicator.Name);
        Assert.Equal(fast, macd.FastPeriod);
        Assert.Equal(slow, macd.SlowPeriod);
        Assert.Equal(signal, macd.SignalPeriod);
        Assert.False(typeof(IIndicator).IsAssignableFrom(macd.GetType()));
    }

    [Theory]
    [InlineData(0, 3, 2, "fastPeriod")]
    [InlineData(-1, 3, 2, "fastPeriod")]
    [InlineData(int.MinValue, 3, 2, "fastPeriod")]
    [InlineData(1, 0, 2, "slowPeriod")]
    [InlineData(1, -1, 2, "slowPeriod")]
    [InlineData(1, int.MinValue, 2, "slowPeriod")]
    [InlineData(1, 3, 0, "signalPeriod")]
    [InlineData(1, 3, -1, "signalPeriod")]
    [InlineData(1, 3, int.MinValue, "signalPeriod")]
    public void RejectsNonpositivePeriodsWithTheirCorrespondingParameter(int fast, int slow, int signal, string parameter)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MovingAverageConvergenceDivergence(fast, slow, signal));

        Assert.Equal(parameter, exception.ParamName);
    }

    [Theory]
    [InlineData(3, 3)]
    [InlineData(4, 3)]
    [InlineData(int.MaxValue, int.MaxValue)]
    public void RejectsFastGreaterThanOrEqualToSlowWithoutSwapping(int fast, int slow)
    {
        var exception = Assert.Throws<ArgumentException>(() => new MovingAverageConvergenceDivergence(fast, slow, 2));

        Assert.Equal("fastPeriod", exception.ParamName);
    }

    [Fact]
    public void RejectsNullInput()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new MovingAverageConvergenceDivergence(2, 3, 2).Calculate(null!));

        Assert.Equal("candles", exception.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(8)]
    public void AlwaysReturnsThreeOrderedNamedSeriesWithExactLookupReferences(int count)
    {
        var result = new MovingAverageConvergenceDivergence(2, 3, 2).Calculate(Sequence(count));

        Assert.Equal(3, result.Count);
        Assert.Equal(new[] { "MACD", "Signal", "Histogram" }, result.Series.Select(item => item.Name).ToArray());
        foreach (var item in result.Series)
        {
            Assert.True(result.TryGetSeries(item.Name, out var found));
            Assert.Same(item.Series, found);
        }
        Assert.False(result.TryGetSeries("macd", out var missing));
        Assert.Null(missing);
    }

    [Fact]
    public void EmptyInputReturnsThreeEmptySeriesRatherThanAnEmptyResult()
    {
        var result = new MovingAverageConvergenceDivergence(2, 4, 2).Calculate([]);

        Assert.Equal(3, result.Count);
        Assert.All(result.Series, item => Assert.Empty(item.Series.Points));
    }

    [Fact]
    public void BeforeSlowWarmUpThereAreNoPartialSeeds()
    {
        var result = new MovingAverageConvergenceDivergence(2, 4, 2).Calculate(Candles(10m, 20m, 30m));

        Assert.All(result.Series, item => Assert.Empty(item.Series.Points));
    }

    [Fact]
    public void FirstMacdUsesTheFastEmaEvolvedFromItsOwnSeedRatherThanRestartingAtSlowWarmUp()
    {
        var candles = Candles(10m, 20m, 30m, 100m);
        var result = new MovingAverageConvergenceDivergence(2, 4, 2).Calculate(candles);

        // Fast seed = 15; Fast at C3 = 25; Fast at C4 = 75 (with decimal precision).
        // Slow seed = (10 + 20 + 30 + 100) / 4 = 40. Restarting Fast from 30/100 yields 65.
        var point = Assert.Single(result.Series[0].Series.Points);
        Assert.Equal(35.000000000000000000000000002m, point.Value);
        Assert.NotEqual(25m, point.Value);
        Assert.Equal(candles[3].CloseTime, point.Time);
        Assert.Empty(result.Series[1].Series.Points);
        Assert.Empty(result.Series[2].Series.Points);
    }

    [Fact]
    public void NonlinearPeriodTwoThreeTwoReferenceHasExactMacdSignalAndHistogramValues()
    {
        var candles = Candles(10m, 20m, 30m, 100m, 5m, 40m, 80m);
        var result = new MovingAverageConvergenceDivergence(2, 3, 2).Calculate(candles);

        // Fast seed 15, Slow seed 20; alphaFast = alphaSignal = 2/3, alphaSlow = 1/2.
        // First MACD = 25 - 20 = 5; next = 75 - 60 = 15; Signal seed = (5 + 15)/2.
        // Literals retain .NET decimal intermediate precision, without rounding to price scale.
        AssertValues(result.Series[0].Series,
            5.000000000000000000000000000m,
            15.000000000000000000000000002m,
            -4.166666666666666666666666668m,
            -0.138888888888888888888888889m,
            7.245370370370370370370370372m);
        AssertValues(result.Series[1].Series,
            10.000000000000000000000000001m,
            0.5555555555555555555555555545m,
            0.0925925925925925925925925921m,
            4.8611111111111111111111111123m);
        AssertValues(result.Series[2].Series,
            5.000000000000000000000000001m,
            -4.7222222222222222222222222225m,
            -0.2314814814814814814814814811m,
            2.3842592592592592592592592597m);
    }

    [Fact]
    public void FractionalReferenceConfirmsSignalSeedAndTwoRecurrencesOverMacdRatherThanCloses()
    {
        var candles = Candles(10.10m, 10.20m, 10.30m, 10.40m, 10.80m, 10.60m, 10.90m);
        var result = new MovingAverageConvergenceDivergence(1, 3, 3).Calculate(candles);

        // Slow EMA: 10.20, 10.30, 10.55, 10.575, 10.7375; Fast equals the closes.
        // Signal seed = (0.10 + 0.10 + 0.25)/3 = 0.15; then alphaSignal = 1/2.
        AssertValues(result.Series[0].Series, 0.10m, 0.10m, 0.25m, 0.025m, 0.1625m);
        AssertValues(result.Series[1].Series, 0.15m, 0.0875m, 0.125m);
        AssertValues(result.Series[2].Series, 0.10m, -0.0625m, 0.0375m);
        Assert.Equal(candles[4].CloseTime, result.Series[1].Series.Points[0].Time);
        Assert.Equal((0.10m + 0.10m + 0.25m) / 3m, result.Series[1].Series.Points[0].Value);
    }

    [Fact]
    public void FastAndSlowMatchTheCanonicalExistingEmaAcrossEveryOverlappingClose()
    {
        var candles = Candles(10m, 20m, 5m, 90m, 3m, 100m, 45m, 12m);
        var fast = new ExponentialMovingAverage(2).Calculate(candles);
        var slow = new ExponentialMovingAverage(5).Calculate(candles);

        var result = new MovingAverageConvergenceDivergence(2, 5, 2).Calculate(candles);

        for (var index = 0; index < slow.Count; index++)
        {
            var point = result.Series[0].Series.Points[index];
            Assert.Equal(slow.Points[index].Time, fast.Points[index + 3].Time);
            Assert.Equal(slow.Points[index].Time, point.Time);
            Assert.Equal(fast.Points[index + 3].Value - slow.Points[index].Value, point.Value);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(15)]
    public void CountsFollowIndependentWarmUpFormulas(int count)
    {
        var result = new MovingAverageConvergenceDivergence(2, 4, 3).Calculate(Sequence(count));
        var macdCount = Math.Max(0, count - 4 + 1);
        var signalCount = Math.Max(0, macdCount - 3 + 1);

        Assert.Equal(macdCount, result.Series[0].Series.Count);
        Assert.Equal(signalCount, result.Series[1].Series.Count);
        Assert.Equal(signalCount, result.Series[2].Series.Count);
    }

    [Fact]
    public void ExactFirstSignalAndHistogramOccurAtSlowPlusSignalMinusOne()
    {
        var candles = Sequence(6);
        var result = new MovingAverageConvergenceDivergence(2, 4, 3).Calculate(candles);

        Assert.Equal(3, result.Series[0].Series.Count);
        var signal = Assert.Single(result.Series[1].Series.Points);
        var histogram = Assert.Single(result.Series[2].Series.Points);
        Assert.Equal(candles[5].CloseTime, signal.Time);
        Assert.Equal(signal.Time, histogram.Time);
        Assert.Equal(result.Series[0].Series.Points[2].Time, signal.Time);
        Assert.Equal(result.Series[0].Series.Points.Sum(point => point.Value) / 3m, signal.Value);
    }

    [Fact]
    public void SignalPeriodOneEqualsEveryMacdPointAndHistogramIsZero()
    {
        var result = new MovingAverageConvergenceDivergence(2, 3, 1).Calculate(Candles(10m, 20m, 30m, 100m, 5m, 40m));

        Assert.Equal(result.Series[0].Series.Points.ToArray(), result.Series[1].Series.Points.ToArray());
        for (var index = 0; index < result.Series[2].Series.Count; index++)
        {
            Assert.Equal(0m, result.Series[2].Series.Points[index].Value);
            Assert.Equal(result.Series[0].Series.Points[index].Time, result.Series[2].Series.Points[index].Time);
        }
    }

    [Theory]
    [InlineData(25, 0, 0)]
    [InlineData(26, 1, 0)]
    [InlineData(33, 8, 0)]
    [InlineData(34, 9, 1)]
    [InlineData(40, 15, 7)]
    public void ConventionalPeriodsStartMacdAtCandleTwentySixAndSignalAtThirtyFour(int count, int macdCount, int signalCount)
    {
        var candles = Sequence(count);
        var result = new MovingAverageConvergenceDivergence(12, 26, 9).Calculate(candles);

        Assert.Equal(macdCount, result.Series[0].Series.Count);
        Assert.Equal(signalCount, result.Series[1].Series.Count);
        Assert.Equal(signalCount, result.Series[2].Series.Count);
        if (macdCount > 0) Assert.Equal(candles[25].CloseTime, result.Series[0].Series.Points[0].Time);
        if (signalCount > 0)
        {
            Assert.Equal(candles[33].CloseTime, result.Series[1].Series.Points[0].Time);
            Assert.Equal(candles[33].CloseTime, result.Series[2].Series.Points[0].Time);
        }
    }

    [Fact]
    public void EveryHistogramValueIsMacdMinusSignalAtTheSameCloseTime()
    {
        var candles = Sequence(20);
        var result = new MovingAverageConvergenceDivergence(2, 4, 3).Calculate(candles);

        for (var index = 0; index < result.Series[1].Series.Count; index++)
        {
            var macd = result.Series[0].Series.Points[index + 2];
            var signal = result.Series[1].Series.Points[index];
            var histogram = result.Series[2].Series.Points[index];
            Assert.Equal(candles[index + 5].CloseTime, macd.Time);
            Assert.NotEqual(candles[index + 5].OpenTime, macd.Time);
            Assert.Equal(macd.Time, signal.Time);
            Assert.Equal(signal.Time, histogram.Time);
            Assert.Equal(macd.Value - signal.Value, histogram.Value);
        }
    }

    [Fact]
    public void FastAndSlowUseOnlyCloseRatherThanOtherOhlcvFields()
    {
        var closes = new[] { 10m, 20m, 30m, 100m, 5m, 40m, 80m };
        var varied = closes.Select((close, index) => new Candle(A, CandleInterval.OneMinute, At(index),
            new Price(1m), new Price(1000m), new Price(1m), new Price(close), new Quantity(999m))).ToArray();
        var indicator = new MovingAverageConvergenceDivergence(2, 3, 2);

        AssertSameResults(indicator.Calculate(Candles(closes)), indicator.Calculate(varied));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(-1, -1)]
    public void FlatRisingAndFallingClosesPermitZeroPositiveAndNegativeOutputs(int change, int expected)
    {
        var candles = Candles(Enumerable.Range(0, 8).Select(index => 10m + change * index).ToArray());

        var result = new MovingAverageConvergenceDivergence(1, 3, 3).Calculate(candles);

        Assert.All(result.Series[0].Series.Points, point => Assert.Equal((decimal)expected, point.Value));
        Assert.All(result.Series[1].Series.Points, point => Assert.Equal((decimal)expected, point.Value));
        Assert.All(result.Series[2].Series.Points, point => Assert.Equal(0m, point.Value));
    }

    [Theory]
    [InlineData("instrument", 3)]
    [InlineData("interval", 3)]
    [InlineData("duplicate", 3)]
    [InlineData("backward", 3)]
    [InlineData("instrument", 20)]
    [InlineData("interval", 20)]
    [InlineData("duplicate", 20)]
    [InlineData("backward", 20)]
    public void RejectsInvalidSequencesIncludingBeforeWarmUp(string problem, int slow)
    {
        var candles = Candles(10m, 20m, 30m, 40m, 50m);
        candles[4] = problem switch
        {
            "instrument" => Create(50m, At(4), id: B),
            "interval" => Create(50m, At(4), interval: CandleInterval.FiveMinutes),
            "duplicate" => Create(50m, At(3)),
            "backward" => Create(50m, At(2)),
            _ => throw new ArgumentException("Unknown invalid input.", nameof(problem))
        };
        var originals = candles.ToArray();

        var exception = Assert.Throws<ArgumentException>(() => new MovingAverageConvergenceDivergence(2, slow, 2).Calculate(candles));

        Assert.Equal("candles", exception.ParamName);
        for (var index = 0; index < candles.Length; index++) Assert.Same(originals[index], candles[index]);
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(2, 3)]
    [InlineData(4, 3)]
    [InlineData(0, 20)]
    [InlineData(2, 20)]
    [InlineData(4, 20)]
    public void RejectsNullElementsAnywhereInTheEntireBatch(int index, int slow)
    {
        var candles = Sequence(5);
        candles[index] = null!;

        var exception = Assert.Throws<ArgumentException>(() => new MovingAverageConvergenceDivergence(2, slow, 2).Calculate(candles));

        Assert.Equal("candles", exception.ParamName);
    }

    [Fact]
    public void FullValidationPrecedesPotentialSeedOverflow()
    {
        var candles = Candles(decimal.MaxValue, decimal.MaxValue, 1m);
        candles[2] = null!;

        var exception = Assert.Throws<ArgumentException>(() => new MovingAverageConvergenceDivergence(1, 2, 1).Calculate(candles));

        Assert.Equal("candles", exception.ParamName);
    }

    [Fact]
    public void InstrumentAndIntervalCoherenceUseExistingValueEquality()
    {
        var candles = new[] { Create(10m, At(0)), Create(20m, At(1),
            new InstrumentId(A.Value), new CandleInterval(TimeSpan.FromMinutes(1))) };

        var result = new MovingAverageConvergenceDivergence(1, 2, 1).Calculate(candles);

        Assert.Equal(5m, Assert.Single(result.Series[0].Series.Points).Value);
    }

    [Fact]
    public void TemporalGapsKeepAllThreeEmaStatesAndUseOnlyCorrespondingCloseTimes()
    {
        var closes = new[] { 10.10m, 10.20m, 10.30m, 10.40m, 10.80m, 10.60m, 10.90m };
        var times = new[] { 0, 1, 5, 10, 50, 100, 200 };
        var candles = closes.Select((close, index) => Create(close, At(times[index]), interval: CandleInterval.FiveMinutes)).ToArray();
        var indicator = new MovingAverageConvergenceDivergence(1, 3, 3);
        var regular = indicator.Calculate(Candles(closes));

        var result = indicator.Calculate(candles);

        for (var component = 0; component < 3; component++)
        {
            Assert.Equal(regular.Series[component].Series.Points.Select(point => point.Value).ToArray(),
                result.Series[component].Series.Points.Select(point => point.Value).ToArray());
            var offset = component == 0 ? 2 : 4;
            for (var index = 0; index < result.Series[component].Series.Count; index++)
                Assert.Equal(candles[index + offset].CloseTime, result.Series[component].Series.Points[index].Time);
        }
    }

    [Fact]
    public void InputReferencesOhlcvAndTimestampValuesRemainUnchanged()
    {
        var source = Candles(10.1000m, 20.2000m, 5.0500m, 40.4000m, 50.5000m).ToList();
        var originals = source.ToArray();
        var times = source.Select(candle => (candle.OpenTime, candle.CloseTime)).ToArray();
        var bits = source.Select(candle => new[] { candle.Open.Value, candle.High.Value, candle.Low.Value,
            candle.Close.Value, candle.Volume.Value }.Select(decimal.GetBits).ToArray()).ToArray();

        new MovingAverageConvergenceDivergence(2, 3, 2).Calculate(source);

        Assert.Equal(originals.Length, source.Count);
        for (var index = 0; index < source.Count; index++)
        {
            Assert.Same(originals[index], source[index]);
            Assert.Same(times[index].OpenTime, source[index].OpenTime);
            Assert.Equal(times[index].CloseTime, source[index].CloseTime);
            var values = new[] { source[index].Open.Value, source[index].High.Value, source[index].Low.Value,
                source[index].Close.Value, source[index].Volume.Value };
            for (var field = 0; field < values.Length; field++) Assert.Equal(bits[index][field], decimal.GetBits(values[field]));
        }
    }

    [Fact]
    public void AllResultsRemainSnapshotsAfterSourceReplacementReorderAndClear()
    {
        var source = Sequence(10).ToList();
        var result = new MovingAverageConvergenceDivergence(2, 3, 2).Calculate(source);
        var originals = result.Series.Select(item => item.Series.Points.ToArray()).ToArray();

        source[0] = Create(999m, At(0));
        source.Reverse();
        source.Clear();

        for (var component = 0; component < 3; component++)
        {
            Assert.Equal(originals[component], result.Series[component].Series.Points.ToArray());
            for (var index = 0; index < originals[component].Length; index++)
                Assert.Same(originals[component][index], result.Series[component].Series.Points[index]);
            var mutable = Assert.IsAssignableFrom<IList<IndicatorPoint>>(result.Series[component].Series.Points);
            Assert.Throws<NotSupportedException>(() => mutable.Clear());
        }
        var entries = Assert.IsAssignableFrom<IList<NamedIndicatorSeries>>(result.Series);
        Assert.Throws<NotSupportedException>(() => entries.Clear());
    }

    [Fact]
    public void RepeatedCalculationsAreDeterministicAndReuseAcrossDifferentBatchesIsStateless()
    {
        var indicator = new MovingAverageConvergenceDivergence(2, 3, 2);
        var source = Sequence(10);
        var first = indicator.Calculate(source);
        var other = Candles(100m, 200m, 300m, 50m, 400m, 5m, 200m);

        AssertSameResults(new MovingAverageConvergenceDivergence(2, 3, 2).Calculate(other), indicator.Calculate(other));
        AssertSameResults(first, indicator.Calculate(source));
        AssertSameResults(first, indicator.Calculate(source));
        Assert.All(indicator.Calculate([]).Series, item => Assert.Empty(item.Series.Points));
        AssertSameResults(first, indicator.Calculate(source));
    }

    [Theory]
    [InlineData(2, 3, 2)]
    [InlineData(1, 4, 1)]
    [InlineData(3, 7, 4)]
    [InlineData(12, 26, 9)]
    public void EveryPrefixOfAllThreeSeriesIsInvariantUnderFutureCandles(int fast, int slow, int signal)
    {
        var candles = Sequence(45);
        candles[^1] = Create(1000000m, At(44));
        var indicator = new MovingAverageConvergenceDivergence(fast, slow, signal);
        var extended = indicator.Calculate(candles);

        for (var length = 0; length < candles.Length; length++)
        {
            var prefix = indicator.Calculate(candles.Take(length).ToArray());
            for (var component = 0; component < 3; component++)
            {
                Assert.Equal(extended.Series[component].Name, prefix.Series[component].Name);
                Assert.Equal(prefix.Series[component].Series.Points.ToArray(),
                    extended.Series[component].Series.Points.Take(prefix.Series[component].Series.Count).ToArray());
            }
        }
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    public void SlowOrFastSeedOverflowFromValidCandlesPropagatesNaturally(int fast, int slow)
    {
        var candles = Candles(Enumerable.Repeat(decimal.MaxValue, slow).ToArray());
        var originals = candles.ToArray();

        Assert.Throws<OverflowException>(() => new MovingAverageConvergenceDivergence(fast, slow, 1).Calculate(candles));

        for (var index = 0; index < candles.Length; index++) Assert.Same(originals[index], candles[index]);
    }

    [Fact]
    public void SignalSeedSumOverflowPropagatesEvenThoughTheMeanWouldFit()
    {
        var candles = Candles(1m, 1m, 1m, 1m, decimal.MaxValue, decimal.MaxValue, decimal.MaxValue);
        // Slow(4) is finite: after its seed, MACD(1,4) is approximately 0.6M, 0.36M, 0.216M.
        // Signal(4) seed adds initial 0 plus those values; sum > M although sum/4 would fit.
        var partial = new MovingAverageConvergenceDivergence(1, 4, 5).Calculate(candles);
        Assert.Equal(4, partial.Series[0].Series.Count);
        Assert.Empty(partial.Series[1].Series.Points);

        Assert.Throws<OverflowException>(() => new MovingAverageConvergenceDivergence(1, 4, 4).Calculate(candles));
    }

    [Fact]
    public void MaximumPeriodsDoNotOverflowOrdinalArithmeticOrAllocateByPeriod()
    {
        var beforeSlow = new MovingAverageConvergenceDivergence(1, int.MaxValue, int.MaxValue).Calculate(Sequence(3));
        var beforeSignal = new MovingAverageConvergenceDivergence(1, 2, int.MaxValue).Calculate(Sequence(5));

        Assert.All(beforeSlow.Series, item => Assert.Empty(item.Series.Points));
        Assert.Equal(4, beforeSignal.Series[0].Series.Count);
        Assert.Empty(beforeSignal.Series[1].Series.Points);
        Assert.Empty(beforeSignal.Series[2].Series.Points);
    }

    [Fact]
    public void MaximumRepresentableCloseTimeIsPreservedByAllComponents()
    {
        var interval = new CandleInterval(TimeSpan.FromTicks(1));
        var candles = new[] { Create(10m, new UtcInstant(DateTimeOffset.MaxValue.AddTicks(-2)), interval: interval),
            Create(20m, new UtcInstant(DateTimeOffset.MaxValue.AddTicks(-1)), interval: interval) };

        var result = new MovingAverageConvergenceDivergence(1, 2, 1).Calculate(candles);

        Assert.All(result.Series, item => Assert.Equal(new UtcInstant(DateTimeOffset.MaxValue), Assert.Single(item.Series.Points).Time));
    }

    private static void AssertValues(IndicatorSeries series, params decimal[] expected) =>
        Assert.Equal(expected, series.Points.Select(point => point.Value).ToArray());

    private static void AssertSameResults(MultiSeriesIndicatorResult expected, MultiSeriesIndicatorResult actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var index = 0; index < expected.Count; index++)
        {
            Assert.Equal(expected.Series[index].Name, actual.Series[index].Name);
            Assert.Equal(expected.Series[index].Series.Points.ToArray(), actual.Series[index].Series.Points.ToArray());
        }
    }

    private static Candle[] Sequence(int count) => Enumerable.Range(0, count)
        .Select(index => Create(10m + (index * 17 % 29) / 10m, At(index))).ToArray();

    private static Candle[] Candles(params decimal[] closes) => closes.Select((close, index) => Create(close, At(index))).ToArray();

    private static UtcInstant At(int minutes) => new(Start.Value.AddMinutes(minutes));

    private static Candle Create(decimal close, UtcInstant time, InstrumentId? id = null, CandleInterval? interval = null)
    {
        var price = new Price(close);
        return new Candle(id ?? A, interval ?? CandleInterval.OneMinute, time,
            price, price, price, price, new Quantity(1.0000m));
    }
}
