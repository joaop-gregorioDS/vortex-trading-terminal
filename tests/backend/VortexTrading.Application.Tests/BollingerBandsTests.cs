using System.Globalization;
using VortexTrading.Application.Indicators;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Application.Tests;

public sealed class BollingerBandsTests
{
    private const decimal Tolerance = 0.000000000000000000000000001m;
    private static readonly InstrumentId A = new(new Guid("11111111-1111-1111-1111-111111111111"));
    private static readonly InstrumentId B = new(new Guid("22222222-2222-2222-2222-222222222222"));
    private static readonly UtcInstant Start = new(new DateTimeOffset(2026, 10, 3, 10, 0, 0, TimeSpan.Zero));

    [Theory]
    [InlineData(1, "0.5")]
    [InlineData(3, "1.25")]
    [InlineData(20, "2")]
    [InlineData(int.MaxValue, "79228162514264337593543950335")]
    [InlineData(2, "0.0000000000000000000000000001")]
    public void PreservesPositivePeriodAndMultiplierWithTheIndependentMultiSeriesContract(int period, string multiplier)
    {
        var k = Parse(multiplier);
        var bands = new BollingerBands(period, k);
        IMultiSeriesIndicator indicator = bands;

        Assert.Equal("Bollinger Bands", indicator.Name);
        Assert.Equal(period, bands.Period);
        Assert.Equal(k, bands.StandardDeviationMultiplier);
        Assert.False(typeof(IIndicator).IsAssignableFrom(bands.GetType()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void RejectsNonpositivePeriods(int period)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new BollingerBands(period, 2m));

        Assert.Equal("period", exception.ParamName);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-0.25")]
    [InlineData("-1")]
    [InlineData("-79228162514264337593543950335")]
    public void RejectsNonpositiveMultipliers(string multiplier)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new BollingerBands(3, Parse(multiplier)));

        Assert.Equal("standardDeviationMultiplier", exception.ParamName);
    }

    [Fact]
    public void RejectsNullInput()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new BollingerBands(3, 2m).Calculate(null!));

        Assert.Equal("candles", exception.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(8)]
    public void AlwaysReturnsMiddleUpperLowerInOrderWithExactLookupReferences(int count)
    {
        var result = new BollingerBands(3, 2m).Calculate(Sequence(count));

        Assert.Equal(3, result.Count);
        Assert.Equal(new[] { "Middle", "Upper", "Lower" }, result.Series.Select(item => item.Name).ToArray());
        foreach (var item in result.Series)
        {
            Assert.True(result.TryGetSeries(item.Name, out var found));
            Assert.Same(item.Series, found);
        }
        Assert.False(result.TryGetSeries("middle", out var missing));
        Assert.Null(missing);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void EmptyInputOrInsufficientWarmUpReturnsThreeEmptySeries(int count)
    {
        var result = new BollingerBands(3, 2m).Calculate(Sequence(count));

        Assert.Equal(3, result.Count);
        Assert.All(result.Series, item => Assert.Empty(item.Series.Points));
    }

    [Fact]
    public void ExactFirstWindowUsesPopulationVarianceRatherThanSampleVariance()
    {
        var candles = Candles(2m, 4m);

        var result = new BollingerBands(2, 2m).Calculate(candles);

        AssertValues(result.Series[0].Series, 3m);
        AssertValues(result.Series[1].Series, 5m);
        AssertValues(result.Series[2].Series, 1m);
        var sigma = (result.Series[1].Series.Points[0].Value - 3m) / 2m;
        Assert.Equal(1m, sigma * sigma); // Population variance 1; sample variance would be 2.
        Assert.NotEqual(2m, sigma * sigma);
        Assert.All(result.Series, item => Assert.Equal(candles[1].CloseTime, Assert.Single(item.Series.Points).Time));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void ExactRootsOfZeroOneFourNineAndSixteenAreExercisedThroughValidWindows(int sigma)
    {
        // Mean 10; deviations -sigma/+sigma; population variance sigma squared.
        var result = new BollingerBands(2, 1m).Calculate(Candles(10m - sigma, 10m + sigma));

        AssertValues(result.Series[0].Series, 10m);
        AssertValues(result.Series[1].Series, 10m + sigma);
        AssertValues(result.Series[2].Series, 10m - sigma);
    }

    [Theory]
    [InlineData("0.25")]
    [InlineData("2")]
    [InlineData("79228162514264337593543950335")]
    public void ConstantWindowsCollapseAllBandsRegardlessOfPositiveMultiplier(string multiplier)
    {
        var result = new BollingerBands(4, Parse(multiplier)).Calculate(Candles(10m, 10m, 10m, 10m, 10m, 10m));

        Assert.All(result.Series, item => AssertValues(item.Series, 10m, 10m, 10m));
    }

    [Fact]
    public void IrrationalRootOfTwoConvergesDeterministicallyWithDecimalStabilityAndResidualChecks()
    {
        // Mean = 3; squares = 4 + 0 + 0 + 4; population variance = 2.
        var indicator = new BollingerBands(4, 1m);
        var candles = Candles(1m, 3m, 3m, 5m);
        var result = indicator.Calculate(candles);
        var sigma = result.Series[1].Series.Points[0].Value - 3m;

        Assert.Equal(1.4142135623730950488016887242m, sigma);
        Assert.True(Math.Abs((sigma / 2m + (2m / sigma) / 2m) - sigma) <= Tolerance);
        // This small radicand permits a decimal residual check; it is not a universal absolute r-squared guarantee.
        Assert.True(Math.Abs(sigma * sigma - 2m) <= 0.00000000000000000000000001m);
        Assert.Equal(3m - sigma, result.Series[2].Series.Points[0].Value);
        AssertSameResults(result, indicator.Calculate(candles));
        AssertOrdered(result);
    }

    [Fact]
    public void FractionalClosesMultiplierAndSubUnitRootHaveExactDecimalBands()
    {
        // Mean 1.25, variance 0.0625, sigma 0.25, K 1.5, offset 0.375.
        var result = new BollingerBands(2, 1.5m).Calculate(Candles(1.00m, 1.50m, 2.00m));

        AssertValues(result.Series[0].Series, 1.25m, 1.75m);
        AssertValues(result.Series[1].Series, 1.625m, 2.125m);
        AssertValues(result.Series[2].Series, 0.875m, 1.375m);
    }

    [Fact]
    public void SmallestPositiveDecimalVarianceStillProducesItsRepresentableSquareRoot()
    {
        var result = new BollingerBands(2, 1m).Calculate(Candles(9.99999999999999m, 10.00000000000001m));

        AssertValues(result.Series[0].Series, 10m);
        AssertValues(result.Series[1].Series, 10.00000000000001m);
        AssertValues(result.Series[2].Series, 9.99999999999999m);
    }

    [Fact]
    public void DecimalUnderflowOfSubMinimumSquaredDeviationsIsNotReplacedByFloatingPoint()
    {
        var result = new BollingerBands(2, 1m).Calculate(Candles(9.999999999999999m, 10.000000000000001m));

        Assert.All(result.Series, item => AssertValues(item.Series, 10m));
    }

    [Fact]
    public void RollingWindowsDropTheOutgoingObservationRatherThanAccumulatingAllPastCloses()
    {
        var result = new BollingerBands(2, 1m).Calculate(Candles(2m, 4m, 8m, 10m));

        AssertValues(result.Series[0].Series, 3m, 6m, 9m);
        AssertValues(result.Series[1].Series, 4m, 8m, 10m);
        AssertValues(result.Series[2].Series, 2m, 4m, 8m);
    }

    [Fact]
    public void LargePriceBaseWithSmallDispersionUsesDeviationsInsteadOfRawSquaredPrices()
    {
        // Raw prices squared would overflow; deviations +/-1 and their squares are representable.
        const decimal mean = 100000000000000000000m;

        var result = new BollingerBands(2, 2m).Calculate(Candles(mean - 1m, mean + 1m));

        AssertValues(result.Series[0].Series, mean);
        AssertValues(result.Series[1].Series, mean + 2m);
        AssertValues(result.Series[2].Series, mean - 2m);
    }

    [Theory]
    [InlineData("0.25")]
    [InlineData("2")]
    [InlineData("79228162514264337593543950335")]
    public void PeriodOneReproducesEveryCloseAndCloseTimeForAllThreeBands(string multiplier)
    {
        var candles = Candles(10.10m, 20.20m, 5.05m, 40.40m);
        var result = new BollingerBands(1, Parse(multiplier)).Calculate(candles);

        Assert.All(result.Series, item =>
        {
            Assert.Equal(candles.Length, item.Series.Count);
            for (var index = 0; index < candles.Length; index++)
            {
                Assert.Equal(candles[index].Close.Value, item.Series.Points[index].Value);
                Assert.Equal(candles[index].CloseTime, item.Series.Points[index].Time);
            }
        });
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(0, 3)]
    [InlineData(2, 3)]
    [InlineData(3, 3)]
    [InlineData(4, 3)]
    [InlineData(8, 2)]
    [InlineData(8, 8)]
    [InlineData(8, 9)]
    [InlineData(30, 20)]
    public void AllThreeCountsFollowTheWindowLengthFormula(int count, int period)
    {
        var result = new BollingerBands(period, 2m).Calculate(Sequence(count));

        Assert.All(result.Series, item => Assert.Equal(Math.Max(0, count - period + 1), item.Series.Count));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(20)]
    public void MiddleExactlyMatchesCanonicalSmaOnMultipleDatasetsAndPeriods(int period)
    {
        foreach (var candles in new[] { Sequence(30), Candles(10m, 20m, 5m, 100m, 3m, 7m, 25m),
            Candles(10.10m, 10.20m, 10.30m, 10.40m, 10.80m) })
        {
            var expected = new SimpleMovingAverage(period).Calculate(candles);
            var actual = new BollingerBands(period, 1.25m).Calculate(candles).Series[0].Series;

            Assert.Equal(expected.Count, actual.Count);
            Assert.Equal(expected.Points.ToArray(), actual.Points.ToArray());
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public void AllPointsAreSynchronizedAndUseTheLastWindowCandleClose(int period)
    {
        var candles = Sequence(12);
        var result = new BollingerBands(period, 2m).Calculate(candles);

        Assert.Equal(result.Series[0].Series.Count, result.Series[1].Series.Count);
        Assert.Equal(result.Series[0].Series.Count, result.Series[2].Series.Count);
        for (var index = 0; index < result.Series[0].Series.Count; index++)
        {
            var expected = candles[index + period - 1].CloseTime;
            foreach (var item in result.Series)
            {
                Assert.Equal(expected, item.Series.Points[index].Time);
                Assert.NotEqual(candles[index + period - 1].OpenTime, item.Series.Points[index].Time);
            }
        }
        AssertOrdered(result);
    }

    [Fact]
    public void UsesOnlyCloseRatherThanOtherOhlcvFields()
    {
        var closes = new[] { 2m, 4m, 8m, 10m };
        var varied = closes.Select((close, index) => new Candle(A, CandleInterval.OneMinute, At(index),
            new Price(1m), new Price(1000m), new Price(1m), new Price(close), new Quantity(999m))).ToArray();
        var indicator = new BollingerBands(2, 2m);

        AssertSameResults(indicator.Calculate(Candles(closes)), indicator.Calculate(varied));
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
    public void RejectsInvalidInputIncludingBeforeWarmUpWithoutRepair(string problem, int period)
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
        var original = candles.ToArray();

        var exception = Assert.Throws<ArgumentException>(() => new BollingerBands(period, 2m).Calculate(candles));

        Assert.Equal("candles", exception.ParamName);
        for (var index = 0; index < candles.Length; index++) Assert.Same(original[index], candles[index]);
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(2, 3)]
    [InlineData(4, 3)]
    [InlineData(0, 20)]
    [InlineData(2, 20)]
    [InlineData(4, 20)]
    public void RejectsNullElementsThroughoutTheEntireBatch(int nullIndex, int period)
    {
        var candles = Sequence(5);
        candles[nullIndex] = null!;

        var exception = Assert.Throws<ArgumentException>(() => new BollingerBands(period, 2m).Calculate(candles));

        Assert.Equal("candles", exception.ParamName);
    }

    [Fact]
    public void FullValidationPrecedesPotentialMeanOverflow()
    {
        var candles = Candles(decimal.MaxValue, decimal.MaxValue, 1m);
        candles[2] = null!;

        var exception = Assert.Throws<ArgumentException>(() => new BollingerBands(2, 2m).Calculate(candles));

        Assert.Equal("candles", exception.ParamName);
    }

    [Fact]
    public void InstrumentAndIntervalCoherenceUseExistingValueEquality()
    {
        var candles = new[] { Create(2m, At(0)), Create(4m, At(1),
            new InstrumentId(A.Value), new CandleInterval(TimeSpan.FromMinutes(1))) };

        var result = new BollingerBands(2, 2m).Calculate(candles);

        AssertValues(result.Series[0].Series, 3m);
        AssertValues(result.Series[1].Series, 5m);
        AssertValues(result.Series[2].Series, 1m);
    }

    [Fact]
    public void TemporalGapsPreserveWindowValuesWithoutResetOrPadding()
    {
        var closes = new[] { 2m, 4m, 8m, 10m };
        var times = new[] { 0, 1, 50, 200 };
        var candles = closes.Select((close, index) => Create(close, At(times[index]), interval: CandleInterval.FiveMinutes)).ToArray();
        var indicator = new BollingerBands(2, 2m);
        var regular = indicator.Calculate(Candles(closes));

        var result = indicator.Calculate(candles);

        for (var component = 0; component < 3; component++)
        {
            Assert.Equal(regular.Series[component].Series.Points.Select(point => point.Value).ToArray(),
                result.Series[component].Series.Points.Select(point => point.Value).ToArray());
            for (var index = 0; index < result.Series[component].Series.Count; index++)
                Assert.Equal(candles[index + 1].CloseTime, result.Series[component].Series.Points[index].Time);
        }
    }

    [Fact]
    public void InputCollectionReferencesOhlcvAndTimestampsRemainUnchanged()
    {
        var source = Candles(10.1000m, 20.2000m, 5.0500m, 40.4000m).ToList();
        var originals = source.ToArray();
        var times = source.Select(candle => (candle.OpenTime, candle.CloseTime)).ToArray();
        var bits = source.Select(candle => new[] { candle.Open.Value, candle.High.Value, candle.Low.Value,
            candle.Close.Value, candle.Volume.Value }.Select(decimal.GetBits).ToArray()).ToArray();

        new BollingerBands(3, 2m).Calculate(source);

        Assert.Equal(originals.Length, source.Count);
        for (var index = 0; index < source.Count; index++)
        {
            Assert.Same(originals[index], source[index]);
            Assert.Same(times[index].OpenTime, source[index].OpenTime);
            Assert.Equal(times[index].CloseTime, source[index].CloseTime);
            var actual = new[] { source[index].Open.Value, source[index].High.Value, source[index].Low.Value,
                source[index].Close.Value, source[index].Volume.Value };
            for (var field = 0; field < actual.Length; field++) Assert.Equal(bits[index][field], decimal.GetBits(actual[field]));
        }
    }

    [Fact]
    public void EveryResultRemainsImmutableAfterSourceMutation()
    {
        var source = Sequence(10).ToList();
        var result = new BollingerBands(3, 2m).Calculate(source);
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
    public void RepeatedCalculationsAndReuseAcrossUnrelatedBatchesAreDeterministicAndStateless()
    {
        var indicator = new BollingerBands(3, 1.5m);
        var source = Sequence(10);
        var first = indicator.Calculate(source);
        var other = Candles(100m, 200m, 300m, 50m, 400m);

        AssertSameResults(new BollingerBands(3, 1.5m).Calculate(other), indicator.Calculate(other));
        AssertSameResults(first, indicator.Calculate(source));
        Assert.All(indicator.Calculate([]).Series, item => Assert.Empty(item.Series.Points));
        AssertSameResults(first, indicator.Calculate(source));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(20)]
    public void AllThreeSeriesAreInvariantAcrossEveryPrefixAndAFuturePriceShock(int period)
    {
        var candles = Sequence(30);
        candles[^1] = Create(1000000m, At(29));
        var indicator = new BollingerBands(period, 2m);
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
            AssertOrdered(prefix);
        }
    }

    [Fact]
    public void ARepresentableHighMagnitudeRootUsesTheDefensiveNewtonAverageWithoutArtificialOverflow()
    {
        // Mean 2e14; deviations +/-1e14; sum of squares 2e28 fits, variance 1e28.
        // The initial Newton estimate is 1e28; its representable root is 1e14.
        var result = new BollingerBands(2, 1m).Calculate(Candles(100000000000000m, 300000000000000m));

        AssertValues(result.Series[0].Series, 200000000000000m);
        AssertValues(result.Series[1].Series, 300000000000000m);
        AssertValues(result.Series[2].Series, 100000000000000m);
    }

    [Theory]
    [InlineData(50, 400, "151.66666666666666666666666667", "31172.222222222222222222222223",
        "176.55656946775507119032035448", "176.55656946775507119032035449", true)]
    [InlineData(1, 431, "145.66666666666666666666666667", "40710.222222222222222222222223",
        "201.76774326492880269775072947", "201.76774326492880269775072948", false)]
    public void DecimalTwoCycleSelectsTheMinimumNewtonErrorRatherThanAnArbitrarySmallerOrLargerRoot(
        int firstClose, int secondClose, string middleValue, string varianceValue,
        string lowValue, string highValue, bool selectHigh)
    {
        // These adjacent candidates were observed in bounded decimal Newton iterations for the valid windows.
        // Prove the actual A -> B -> A cycle and its candidate errors, then exercise the private policy via bands.
        var variance = Parse(varianceValue);
        var low = Parse(lowValue);
        var high = Parse(highValue);
        Assert.Equal(high, low / 2m + (variance / low) / 2m);
        Assert.Equal(low, high / 2m + (variance / high) / 2m);
        Assert.True(high - low > Tolerance);
        var expectedRoot = selectHigh ? high : low;
        var rejectedRoot = selectHigh ? low : high;
        Assert.Equal(0m, Math.Abs(expectedRoot - variance / expectedRoot));
        Assert.Equal(0.00000000000000000000000002m, Math.Abs(rejectedRoot - variance / rejectedRoot));
        var middle = Parse(middleValue);
        var candles = Candles(firstClose, secondClose, 5m);
        var originals = candles.ToArray();
        var indicator = new BollingerBands(3, 2m);

        var result = indicator.Calculate(candles);

        AssertValues(result.Series[0].Series, middle);
        AssertValues(result.Series[1].Series, middle + 2m * expectedRoot);
        AssertValues(result.Series[2].Series, middle - 2m * expectedRoot);
        Assert.NotEqual(middle + 2m * rejectedRoot, result.Series[1].Series.Points[0].Value);
        Assert.All(result.Series, item => Assert.Equal(candles[^1].CloseTime, Assert.Single(item.Series.Points).Time));
        AssertOrdered(result);
        for (var index = 0; index < candles.Length; index++) Assert.Same(originals[index], candles[index]);
    }

    [Fact]
    public void TwoCycleWindowProducesExactlyTheSameDecimalBandsAcrossRepeatedCalls()
    {
        var candles = Candles(50m, 400m, 5m);
        var indicator = new BollingerBands(3, 2m);
        var first = indicator.Calculate(candles);

        for (var repetition = 0; repetition < 5; repetition++)
            AssertSameResults(first, indicator.Calculate(candles));
        AssertSameResults(first, new BollingerBands(3, 2m).Calculate(candles));
    }

    [Fact]
    public void TwoCycleWindowAndAllEarlierBandsRemainIdenticalBeforeFutureCandles()
    {
        var candles = Candles(100m, 200m, 300m, 50m, 400m, 5m, 1000000m, 2m, 800m);
        var indicator = new BollingerBands(3, 2m);
        var prefix = indicator.Calculate(candles.Take(6).ToArray());
        var extended = indicator.Calculate(candles);
        var isolated = indicator.Calculate(candles.Skip(3).Take(3).ToArray());

        for (var component = 0; component < 3; component++)
        {
            var prefixPoints = prefix.Series[component].Series.Points;
            Assert.Equal(prefixPoints.ToArray(), extended.Series[component].Series.Points.Take(prefixPoints.Count).ToArray());
            Assert.Equal(Assert.Single(isolated.Series[component].Series.Points), prefixPoints[^1]);
            Assert.Equal(candles[5].CloseTime, prefixPoints[^1].Time);
        }
        AssertOrdered(prefix);
        AssertOrdered(extended);
    }

    [Fact]
    public void ReusingAnInstanceAfterTwoCycleStabilizationDoesNotAffectAnUnrelatedBatch()
    {
        var indicator = new BollingerBands(3, 2m);
        var cycleWindow = Candles(50m, 400m, 5m);
        var first = indicator.Calculate(cycleWindow);
        var other = Candles(2m, 4m, 8m, 10m, 12m);

        AssertSameResults(new BollingerBands(3, 2m).Calculate(other), indicator.Calculate(other));
        Assert.All(indicator.Calculate([]).Series, item => Assert.Empty(item.Series.Points));
        AssertSameResults(first, indicator.Calculate(cycleWindow));
    }

    [Fact]
    public void MeanAccumulationOverflowFromValidCandlesPropagates()
    {
        var candles = Candles(decimal.MaxValue, decimal.MaxValue);
        var originals = candles.ToArray();

        Assert.Throws<OverflowException>(() => new BollingerBands(2, 2m).Calculate(candles));

        Assert.Same(originals[0], candles[0]);
        Assert.Same(originals[1], candles[1]);
    }

    [Fact]
    public void SquaredDeviationOverflowFromValidCandlesPropagates()
    {
        Assert.Throws<OverflowException>(() => new BollingerBands(2, 1m).Calculate(Candles(1m, 1000000000000000m)));
    }

    [Fact]
    public void VarianceSumOverflowPropagatesEvenWhenEachSquareWouldFit()
    {
        // Each squared deviation is just below 4e28, but adding two exceeds decimal.MaxValue.
        Assert.Throws<OverflowException>(() => new BollingerBands(2, 1m).Calculate(Candles(1m, 400000000000000m)));
    }

    [Fact]
    public void MultiplierTimesSigmaOverflowPropagates()
    {
        // Sigma = 2, so MaxValue * sigma overflows before forming either band.
        Assert.Throws<OverflowException>(() => new BollingerBands(2, decimal.MaxValue).Calculate(Candles(1m, 5m)));
    }

    [Fact]
    public void UpperBandOverflowPropagatesWhenTheOffsetItselfFits()
    {
        // Sigma = 1, offset = MaxValue; Middle 3 plus that offset overflows.
        Assert.Throws<OverflowException>(() => new BollingerBands(2, decimal.MaxValue).Calculate(Candles(2m, 4m)));
    }

    [Fact]
    public void LowerMayBeNegativeWithoutPriceClamping()
    {
        var result = new BollingerBands(2, 10m).Calculate(Candles(2m, 4m));

        AssertValues(result.Series[0].Series, 3m);
        AssertValues(result.Series[1].Series, 13m);
        AssertValues(result.Series[2].Series, -7m);
    }

    [Fact]
    public void PeriodOneSupportsMaximumClosesAndMaximumMultiplierWithZeroVariance()
    {
        var result = new BollingerBands(1, decimal.MaxValue).Calculate(Candles(decimal.MaxValue, decimal.MaxValue));

        Assert.All(result.Series, item => AssertValues(item.Series, decimal.MaxValue, decimal.MaxValue));
    }

    [Fact]
    public void MaximumPeriodAcceptsShortValidInputWithoutAllocationOrIntegerOverflow()
    {
        var result = new BollingerBands(int.MaxValue, 2m).Calculate(Sequence(3));

        Assert.All(result.Series, item => Assert.Empty(item.Series.Points));
    }

    [Fact]
    public void MaximumRepresentableCandleCloseTimeIsPreservedByEveryBand()
    {
        var interval = new CandleInterval(TimeSpan.FromTicks(1));
        var candles = new[] { Create(2m, new UtcInstant(DateTimeOffset.MaxValue.AddTicks(-2)), interval: interval),
            Create(4m, new UtcInstant(DateTimeOffset.MaxValue.AddTicks(-1)), interval: interval) };

        var result = new BollingerBands(2, 2m).Calculate(candles);

        Assert.All(result.Series, item => Assert.Equal(new UtcInstant(DateTimeOffset.MaxValue), Assert.Single(item.Series.Points).Time));
    }

    private static void AssertOrdered(MultiSeriesIndicatorResult result)
    {
        for (var index = 0; index < result.Series[0].Series.Count; index++)
        {
            var middle = result.Series[0].Series.Points[index].Value;
            Assert.True(result.Series[1].Series.Points[index].Value >= middle);
            Assert.True(middle >= result.Series[2].Series.Points[index].Value);
        }
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

    private static decimal Parse(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);

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
