using VortexTrading.Application.Indicators;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Application.Tests;

public sealed class StochasticOscillatorTests
{
    private static readonly InstrumentId A = new(new Guid("11111111-1111-1111-1111-111111111111"));
    private static readonly InstrumentId B = new(new Guid("22222222-2222-2222-2222-222222222222"));
    private static readonly UtcInstant Start = new(new DateTimeOffset(2026, 10, 3, 10, 0, 0, TimeSpan.Zero));

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(14, 3, 3)]
    [InlineData(7, 2, 5)]
    [InlineData(int.MaxValue, 1, 1)]
    [InlineData(1, int.MaxValue, 1)]
    [InlineData(1, 1, int.MaxValue)]
    [InlineData(int.MaxValue, int.MaxValue, int.MaxValue)]
    public void PreservesPositiveIndependentPeriodsAndTheMultiSeriesContract(int k, int smoothing, int d)
    {
        var stochastic = new StochasticOscillator(k, smoothing, d);
        IMultiSeriesIndicator indicator = stochastic;

        Assert.Equal("Stochastic Oscillator", indicator.Name);
        Assert.Equal(k, stochastic.KPeriod);
        Assert.Equal(smoothing, stochastic.KSmoothingPeriod);
        Assert.Equal(d, stochastic.DPeriod);
        Assert.False(typeof(IIndicator).IsAssignableFrom(stochastic.GetType()));
    }

    [Theory]
    [InlineData(0, 1, 1, "kPeriod")]
    [InlineData(-1, 1, 1, "kPeriod")]
    [InlineData(int.MinValue, 1, 1, "kPeriod")]
    [InlineData(1, 0, 1, "kSmoothingPeriod")]
    [InlineData(1, -1, 1, "kSmoothingPeriod")]
    [InlineData(1, int.MinValue, 1, "kSmoothingPeriod")]
    [InlineData(1, 1, 0, "dPeriod")]
    [InlineData(1, 1, -1, "dPeriod")]
    [InlineData(1, 1, int.MinValue, "dPeriod")]
    public void RejectsEachNonpositivePeriodWithItsCorrectParameter(int k, int smoothing, int d, string parameter)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new StochasticOscillator(k, smoothing, d));

        Assert.Equal(parameter, exception.ParamName);
    }

    [Fact]
    public void RejectsNullCollection()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new StochasticOscillator(14, 3, 3).Calculate(null!));

        Assert.Equal("candles", exception.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(6)]
    [InlineData(20)]
    public void AlwaysReturnsOnlyPublicKAndDInOrderWithExactLookupReferences(int count)
    {
        var result = new StochasticOscillator(3, 2, 3).Calculate(Sequence(count));

        Assert.Equal(2, result.Count);
        Assert.Equal(new[] { "%K", "%D" }, result.Series.Select(item => item.Name).ToArray());
        foreach (var item in result.Series)
        {
            Assert.True(result.TryGetSeries(item.Name, out var found));
            Assert.Same(item.Series, found);
        }
        Assert.False(result.TryGetSeries("Raw %K", out var missing));
        Assert.Null(missing);
        Assert.False(result.TryGetSeries("%k", out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void EmptyBeforeRawOrRawAvailableBeforeSlowWarmUpRetainsTwoEmptyPublicSeries(int count)
    {
        // Raw begins at candle 3, but smoothing needs two raw values before public K at candle 4.
        var result = new StochasticOscillator(3, 2, 3).Calculate(Sequence(count));

        Assert.Equal(2, result.Count);
        Assert.All(result.Series, item => Assert.Empty(item.Series.Points));
    }

    [Theory]
    [InlineData(4, 1)]
    [InlineData(5, 2)]
    public void PublicKIsNotDelayedUntilDHasCompletedItsOwnWarmUp(int count, int expectedK)
    {
        var result = new StochasticOscillator(3, 2, 3).Calculate(Sequence(count));

        Assert.Equal(expectedK, result.Series[0].Series.Count);
        Assert.Empty(result.Series[1].Series.Points);
    }

    [Theory]
    [InlineData(13, 0, 0)]
    [InlineData(14, 0, 0)]
    [InlineData(15, 0, 0)]
    [InlineData(16, 1, 0)]
    [InlineData(17, 2, 0)]
    [InlineData(18, 3, 1)]
    [InlineData(19, 4, 2)]
    [InlineData(20, 5, 3)]
    public void ConventionalFourteenThreeThreeHasExactlyTheApprovedWarmUpAndCloseTimes(int count, int kCount, int dCount)
    {
        var candles = Sequence(count);
        var result = new StochasticOscillator(14, 3, 3).Calculate(candles);

        Assert.Equal(kCount, result.Series[0].Series.Count);
        Assert.Equal(dCount, result.Series[1].Series.Count);
        if (kCount > 0) Assert.Equal(candles[15].CloseTime, result.Series[0].Series.Points[0].Time);
        if (dCount > 0) Assert.Equal(candles[17].CloseTime, result.Series[1].Series.Points[0].Time);
    }

    [Fact]
    public void ManualRawWindowUsesHighestHighLowestLowAndTheLastClose()
    {
        var candles = new[] { Create(10m, 20m, 12m, At(0)), Create(12m, 18m, 15m, At(1)) };

        var result = new StochasticOscillator(2, 1, 1).Calculate(candles);

        AssertValues(result.Series[0].Series, 50m);
        AssertValues(result.Series[1].Series, 50m);
        Assert.All(result.Series, item => Assert.Equal(candles[1].CloseTime, Assert.Single(item.Series.Points).Time));
    }

    [Theory]
    [InlineData(10, 0)]
    [InlineData(20, 100)]
    public void RawRangeEdgesProduceZeroAndOneHundredWithoutClamping(int close, int expected)
    {
        var candles = new[] { Create(10m, 20m, 15m, At(0)), Create(10m, 20m, close, At(1)) };

        var result = new StochasticOscillator(2, 1, 1).Calculate(candles);

        Assert.All(result.Series, item => AssertValues(item.Series, expected));
    }

    [Fact]
    public void HighChangesRawEvenWithIdenticalCloseSequences()
    {
        var regular = RangeCloses(15m, 15m);
        var wider = new[] { Create(10m, 30m, 15m, At(0)), Create(10m, 20m, 15m, At(1)) };
        var indicator = new StochasticOscillator(2, 1, 1);

        AssertValues(indicator.Calculate(regular).Series[0].Series, 50m);
        AssertValues(indicator.Calculate(wider).Series[0].Series, 25m);
    }

    [Fact]
    public void LowChangesRawEvenWithIdenticalCloseAndHighSequences()
    {
        var regular = new[] { Create(10m, 30m, 15m, At(0)), Create(10m, 30m, 15m, At(1)) };
        var wider = new[] { Create(5m, 30m, 15m, At(0)), Create(10m, 30m, 15m, At(1)) };
        var indicator = new StochasticOscillator(2, 1, 1);

        AssertValues(indicator.Calculate(regular).Series[0].Series, 25m);
        AssertValues(indicator.Calculate(wider).Series[0].Series, 40m);
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(3, 2, 4)]
    [InlineData(14, 3, 3)]
    public void MultipleDegenerateWindowsProduceNeutralFiftyInBothSmoothedSeries(int k, int smoothing, int d)
    {
        var candles = Enumerable.Range(0, 30).Select(index => Create(7m, 7m, 7m, At(index))).ToArray();

        var result = new StochasticOscillator(k, smoothing, d).Calculate(candles);

        Assert.All(result.Series, item =>
        {
            Assert.NotEmpty(item.Series.Points);
            Assert.All(item.Series.Points, point => Assert.Equal(50m, point.Value));
        });
    }

    [Fact]
    public void DegenerateRangeProducesFiftyInsteadOfCarryingThePreviousRaw()
    {
        var candles = new[] { Create(10m, 20m, 20m, At(0)), Create(7m, 7m, 7m, At(1)),
            Create(10m, 20m, 10m, At(2)) };

        var result = new StochasticOscillator(1, 1, 1).Calculate(candles);

        Assert.All(result.Series, item => AssertValues(item.Series, 100m, 50m, 0m));
    }

    [Fact]
    public void ManualSlowKIsTheFiniteArithmeticMeanOfRawTwentyFiftyEighty()
    {
        var candles = RangeCloses(12m, 15m, 18m);

        var result = new StochasticOscillator(1, 3, 1).Calculate(candles);

        Assert.All(result.Series, item => AssertValues(item.Series, 50m));
        Assert.All(result.Series, item => Assert.Equal(candles[2].CloseTime, Assert.Single(item.Series.Points).Time));
    }

    [Fact]
    public void ManualDIsTheFiniteArithmeticMeanOfSlowThirtyFiftySeventy()
    {
        // Raw 20,40,60,80; S=2 produces Slow K 30,50,70; D=3 produces 50.
        var candles = RangeCloses(12m, 14m, 16m, 18m);

        var result = new StochasticOscillator(1, 2, 3).Calculate(candles);

        AssertValues(result.Series[0].Series, 30m, 50m, 70m);
        AssertValues(result.Series[1].Series, 50m);
        Assert.Equal(candles[3].CloseTime, Assert.Single(result.Series[1].Series.Points).Time);
    }

    [Fact]
    public void BothSmoothingStagesDropOutgoingValuesInsteadOfAveragingAllHistory()
    {
        // Raw 0,20,40,100,0 -> Slow K(2) 10,30,70,50 -> D(2) 20,50,60.
        var result = new StochasticOscillator(1, 2, 2).Calculate(RangeCloses(10m, 12m, 14m, 20m, 10m));

        AssertValues(result.Series[0].Series, 10m, 30m, 70m, 50m);
        AssertValues(result.Series[1].Series, 20m, 50m, 60m);
    }

    [Fact]
    public void AllPeriodsOneSupportsNormalDegenerateAndFractionalCandlesFromTheFirstClose()
    {
        var candles = new[] { Create(1m, 11m, 3m, At(0)), Create(7m, 7m, 7m, At(1)),
            Create(1.2m, 1.7m, 1.325m, At(2)) };

        var result = new StochasticOscillator(1, 1, 1).Calculate(candles);

        Assert.All(result.Series, item => AssertValues(item.Series, 20m, 50m, 25m));
        Assert.Equal(result.Series[0].Series.Points.ToArray(), result.Series[1].Series.Points.ToArray());
        for (var index = 0; index < candles.Length; index++)
            Assert.Equal(candles[index].CloseTime, result.Series[0].Series.Points[index].Time);
    }

    [Fact]
    public void FractionalPricesAndBothMeansHaveExactDecimalValues()
    {
        var candles = new[] { Create(1.20m, 1.70m, 1.325m, At(0)), Create(1.20m, 1.70m, 1.45m, At(1)),
            Create(1.20m, 1.70m, 1.575m, At(2)), Create(1.20m, 1.70m, 1.45m, At(3)) };

        var result = new StochasticOscillator(1, 2, 2).Calculate(candles);

        AssertValues(result.Series[0].Series, 37.5m, 62.5m, 62.5m);
        AssertValues(result.Series[1].Series, 50m, 62.5m);
    }

    [Fact]
    public void NonterminatingRatioUsesDivisionBeforeMultiplicationAndNoExplicitRounding()
    {
        var result = new StochasticOscillator(1, 1, 1).Calculate([Create(1m, 4m, 2m, At(0))]);

        Assert.All(result.Series, item => AssertValues(item.Series, 33.333333333333333333333333330m));
        Assert.NotEqual(100m / 3m, result.Series[0].Series.Points[0].Value);
    }

    [Fact]
    public void ZeroWindowsAfterNonterminatingRawValuesHaveNoOutgoingSumDrift()
    {
        var candles = Enumerable.Range(0, 12).Select(index => Create(1m, 4m, index < 3 ? 2m : 1m, At(index))).ToArray();

        var result = new StochasticOscillator(1, 3, 3).Calculate(candles);

        Assert.All(result.Series[0].Series.Points.Skip(3), point => Assert.Equal(0m, point.Value));
        Assert.All(result.Series[1].Series.Points.Skip(3), point => Assert.Equal(0m, point.Value));
        AssertInRange(result);
    }

    [Fact]
    public void KDoesNotWaitAnAdditionalCandleWhenSmoothingPeriodIsOne()
    {
        var candles = RangeCloses(12m, 15m, 18m, 10m);

        var result = new StochasticOscillator(3, 1, 2).Calculate(candles);

        AssertValues(result.Series[0].Series, 80m, 0m);
        AssertValues(result.Series[1].Series, 40m);
        Assert.Equal(candles[2].CloseTime, result.Series[0].Series.Points[0].Time);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 3)]
    [InlineData(14, 3)]
    public void DPeriodOneExactlyReproducesSlowKCountTimeAndValue(int k, int smoothing)
    {
        var result = new StochasticOscillator(k, smoothing, 1).Calculate(Sequence(30));

        Assert.NotEmpty(result.Series[0].Series.Points);
        Assert.Equal(result.Series[0].Series.Count, result.Series[1].Series.Count);
        Assert.Equal(result.Series[0].Series.Points.ToArray(), result.Series[1].Series.Points.ToArray());
    }

    [Fact]
    public void OldHighestImmediatelyStopsInfluencingTheNextWindowAfterItExits()
    {
        var candles = new[] { Create(10m, 30m, 20m, At(0)), Create(10m, 20m, 15m, At(1)),
            Create(10m, 20m, 15m, At(2)) };

        var result = new StochasticOscillator(2, 1, 1).Calculate(candles);

        Assert.All(result.Series, item => AssertValues(item.Series, 25m, 50m));
    }

    [Fact]
    public void OldLowestImmediatelyStopsInfluencingTheNextWindowAfterItExits()
    {
        var candles = new[] { Create(1m, 21m, 11m, At(0)), Create(11m, 21m, 16m, At(1)),
            Create(11m, 21m, 16m, At(2)) };

        var result = new StochasticOscillator(2, 1, 1).Calculate(candles);

        Assert.All(result.Series, item => AssertValues(item.Series, 75m, 50m));
    }

    [Theory]
    [InlineData(0, 1, 1, 1)]
    [InlineData(1, 1, 1, 1)]
    [InlineData(3, 3, 1, 1)]
    [InlineData(4, 3, 2, 3)]
    [InlineData(6, 3, 2, 3)]
    [InlineData(8, 2, 3, 2)]
    [InlineData(20, 14, 3, 3)]
    [InlineData(10, 1, 8, 5)]
    [InlineData(6, int.MaxValue, 1, 1)]
    [InlineData(6, 1, int.MaxValue, 1)]
    [InlineData(6, 1, 1, int.MaxValue)]
    [InlineData(6, int.MaxValue, int.MaxValue, int.MaxValue)]
    public void SeriesLengthsFollowIndependentWarmUpsWithoutCombinedIntegerOverflow(int count, int k, int smoothing, int d)
    {
        var result = new StochasticOscillator(k, smoothing, d).Calculate(Sequence(count));
        var expectedK = Math.Max(0L, (long)count - k - smoothing + 2L);
        var expectedD = Math.Max(0L, (long)count - k - smoothing - d + 3L);

        Assert.Equal(expectedK, (long)result.Series[0].Series.Count);
        Assert.Equal(expectedD, (long)result.Series[1].Series.Count);
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(2, 3, 2)]
    [InlineData(14, 3, 3)]
    [InlineData(3, 1, 7)]
    public void EveryOutputUsesTheCorrectWindowEndingCloseTimeAndAlignedDSuffix(int k, int smoothing, int d)
    {
        var candles = Sequence(30);
        var result = new StochasticOscillator(k, smoothing, d).Calculate(candles);
        var kPoints = result.Series[0].Series.Points;
        var dPoints = result.Series[1].Series.Points;

        for (var index = 0; index < kPoints.Count; index++)
        {
            var candle = candles[k + smoothing - 2 + index];
            Assert.Equal(candle.CloseTime, kPoints[index].Time);
            Assert.NotEqual(candle.OpenTime, kPoints[index].Time);
        }
        for (var index = 0; index < dPoints.Count; index++)
        {
            var candle = candles[k + smoothing + d - 3 + index];
            Assert.Equal(candle.CloseTime, dPoints[index].Time);
            Assert.Equal(kPoints[index + d - 1].Time, dPoints[index].Time);
        }
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(3, 2, 3)]
    [InlineData(14, 3, 3)]
    [InlineData(5, 7, 9)]
    public void BothSeriesStayWithinZeroToOneHundredAcrossVariedValidDatasets(int k, int smoothing, int d)
    {
        var indicator = new StochasticOscillator(k, smoothing, d);
        var fractional = Enumerable.Range(0, 40).Select(index => Create(1m, 4m, 1m + (index % 4), At(index))).ToArray();
        var degenerate = Enumerable.Range(0, 40).Select(index => Create(7m, 7m, 7m, At(index))).ToArray();
        foreach (var candles in new[] { Sequence(40), fractional, degenerate })
        {
            var result = indicator.Calculate(candles);
            Assert.All(result.Series, item => Assert.NotEmpty(item.Series.Points));
            AssertInRange(result);
        }
    }

    [Theory]
    [InlineData("instrument", false)]
    [InlineData("interval", false)]
    [InlineData("duplicate", false)]
    [InlineData("backward", false)]
    [InlineData("instrument", true)]
    [InlineData("interval", true)]
    [InlineData("duplicate", true)]
    [InlineData("backward", true)]
    public void RejectsInvalidInputAcrossTheEntireBatchIncludingBeforeWarmUp(string problem, bool beforeWarmUp)
    {
        var candles = Sequence(5);
        candles[^1] = problem switch
        {
            "instrument" => Create(10m, 20m, 15m, At(4), id: B),
            "interval" => Create(10m, 20m, 15m, At(4), interval: CandleInterval.FiveMinutes),
            "duplicate" => Create(10m, 20m, 15m, At(3)),
            "backward" => Create(10m, 20m, 15m, At(2)),
            _ => throw new ArgumentException("Unknown problem.", nameof(problem))
        };
        var originals = candles.ToArray();
        var indicator = beforeWarmUp ? new StochasticOscillator(14, 3, 3) : new StochasticOscillator(2, 1, 1);

        var exception = Assert.Throws<ArgumentException>(() => indicator.Calculate(candles));

        Assert.Equal("candles", exception.ParamName);
        for (var index = 0; index < candles.Length; index++) Assert.Same(originals[index], candles[index]);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(2, false)]
    [InlineData(4, false)]
    [InlineData(0, true)]
    [InlineData(2, true)]
    [InlineData(4, true)]
    public void RejectsNullElementsBeforeRawCalculationOrAnyWarmUpReturn(int nullIndex, bool beforeWarmUp)
    {
        var candles = Sequence(5);
        candles[nullIndex] = null!;
        var indicator = beforeWarmUp ? new StochasticOscillator(14, 3, 3) : new StochasticOscillator(2, 1, 1);

        var exception = Assert.Throws<ArgumentException>(() => indicator.Calculate(candles));

        Assert.Equal("candles", exception.ParamName);
    }

    [Fact]
    public void InstrumentAndIntervalCoherenceUseExistingDomainValueEquality()
    {
        var candles = new[] { Create(10m, 20m, 12m, At(0)), Create(10m, 20m, 15m, At(1),
            new InstrumentId(A.Value), new CandleInterval(TimeSpan.FromMinutes(1))) };

        var result = new StochasticOscillator(2, 1, 1).Calculate(candles);

        Assert.All(result.Series, item => AssertValues(item.Series, 50m));
    }

    [Fact]
    public void TemporalGapsDoNotResetExtremesOrEitherSmoothingWindow()
    {
        var regular = RangeCloses(10m, 12m, 14m, 20m, 10m);
        var times = new[] { 0, 1, 50, 200, 999 };
        var gapped = regular.Select((candle, index) => Create(candle.Low.Value, candle.High.Value,
            candle.Close.Value, At(times[index]), interval: CandleInterval.FiveMinutes)).ToArray();
        var indicator = new StochasticOscillator(2, 2, 2);
        var expected = indicator.Calculate(regular);

        var actual = indicator.Calculate(gapped);

        for (var component = 0; component < 2; component++)
        {
            Assert.Equal(expected.Series[component].Series.Points.Select(point => point.Value).ToArray(),
                actual.Series[component].Series.Points.Select(point => point.Value).ToArray());
            var firstCandle = component == 0 ? 2 : 3;
            for (var index = 0; index < actual.Series[component].Series.Count; index++)
                Assert.Equal(gapped[firstCandle + index].CloseTime, actual.Series[component].Series.Points[index].Time);
        }
    }

    [Fact]
    public void OpenAndVolumeDoNotParticipateInTheCalculation()
    {
        var original = RangeCloses(12m, 15m, 18m, 14m, 11m);
        var changed = original.Select(candle => new Candle(candle.InstrumentId, candle.Interval, candle.OpenTime,
            new Price(20m), candle.High, candle.Low, candle.Close, new Quantity(999m))).ToArray();
        var indicator = new StochasticOscillator(2, 2, 2);

        AssertSameResults(indicator.Calculate(original), indicator.Calculate(changed));
    }

    [Fact]
    public void InputCollectionReferencesDecimalBitsOhlcvAndTimesRemainUntouched()
    {
        var source = new[] { Create(10.0000m, 20.0000m, 12.0000m, At(0)),
            Create(10.0000m, 20.0000m, 15.0000m, At(1)), Create(10.0000m, 20.0000m, 18.0000m, At(2)) }.ToList();
        var originals = source.ToArray();
        var bits = source.Select(candle => new[] { candle.Open.Value, candle.High.Value, candle.Low.Value,
            candle.Close.Value, candle.Volume.Value }.Select(decimal.GetBits).ToArray()).ToArray();

        new StochasticOscillator(1, 2, 2).Calculate(source);

        Assert.Equal(originals.Length, source.Count);
        for (var index = 0; index < source.Count; index++)
        {
            Assert.Same(originals[index], source[index]);
            Assert.Same(originals[index].OpenTime, source[index].OpenTime);
            Assert.Equal(originals[index].CloseTime, source[index].CloseTime);
            var values = new[] { source[index].Open.Value, source[index].High.Value, source[index].Low.Value,
                source[index].Close.Value, source[index].Volume.Value };
            for (var field = 0; field < values.Length; field++) Assert.Equal(bits[index][field], decimal.GetBits(values[field]));
        }
    }

    [Fact]
    public void BothPublicResultsRemainImmutableAfterSourceReplacementReversalAndClear()
    {
        var source = Sequence(20).ToList();
        var result = new StochasticOscillator(3, 2, 3).Calculate(source);
        var original = result.Series.Select(item => item.Series.Points.ToArray()).ToArray();

        source[0] = Create(1m, 1000000m, 50m, At(0));
        source.Reverse();
        source.Clear();

        for (var component = 0; component < 2; component++)
        {
            Assert.Equal(original[component], result.Series[component].Series.Points.ToArray());
            for (var index = 0; index < original[component].Length; index++)
                Assert.Same(original[component][index], result.Series[component].Series.Points[index]);
            var mutable = Assert.IsAssignableFrom<IList<IndicatorPoint>>(result.Series[component].Series.Points);
            Assert.Throws<NotSupportedException>(() => mutable.Clear());
        }
        Assert.Throws<NotSupportedException>(() => Assert.IsAssignableFrom<IList<NamedIndicatorSeries>>(result.Series).Clear());
    }

    [Fact]
    public void RepeatedCallsAndReuseAfterUnrelatedBatchesWarmUpAndValidationFailureAreStateless()
    {
        var indicator = new StochasticOscillator(3, 2, 3);
        var source = Sequence(20);
        var first = indicator.Calculate(source);
        var other = RangeCloses(10m, 20m, 10m, 20m, 15m, 11m, 19m, 14m);

        AssertSameResults(first, indicator.Calculate(source));
        AssertSameResults(new StochasticOscillator(3, 2, 3).Calculate(other), indicator.Calculate(other));
        Assert.All(indicator.Calculate([]).Series, item => Assert.Empty(item.Series.Points));
        var invalid = Sequence(3);
        invalid[^1] = null!;
        Assert.Throws<ArgumentException>(() => indicator.Calculate(invalid));
        AssertSameResults(first, indicator.Calculate(source));
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(3, 2, 3)]
    [InlineData(14, 3, 3)]
    [InlineData(5, 1, 4)]
    public void EveryPrefixOfBothSeriesIsExactlyInvariantWithAFutureHighAndLowShock(int k, int smoothing, int d)
    {
        var candles = Sequence(30);
        candles[^1] = Create(0.0000000000000000000000000001m, 10000000000000000000000000m, 100m, At(29));
        var indicator = new StochasticOscillator(k, smoothing, d);
        var extended = indicator.Calculate(candles);

        for (var length = 0; length < candles.Length; length++)
        {
            var prefix = indicator.Calculate(candles.Take(length).ToArray());
            for (var component = 0; component < 2; component++)
            {
                Assert.Equal(extended.Series[component].Name, prefix.Series[component].Name);
                Assert.Equal(prefix.Series[component].Series.Points.ToArray(),
                    extended.Series[component].Series.Points.Take(prefix.Series[component].Series.Count).ToArray());
            }
            AssertInRange(prefix);
        }
        AssertInRange(extended);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DivisionBeforeMultiplicationSupportsHighPricesDespiteTheUnsafeNumeratorProduct(bool atHigh)
    {
        var close = atHigh ? decimal.MaxValue : decimal.MaxValue / 2m;
        var candle = Create(1m, decimal.MaxValue, close, At(0));
        var numerator = close - 1m;
        Assert.Throws<OverflowException>(() => 100m * numerator);

        var result = new StochasticOscillator(1, 1, 1).Calculate([candle]);

        Assert.All(result.Series, item => AssertValues(item.Series, atHigh ? 100m : 50m));
        AssertInRange(result);
    }

    [Fact]
    public void MaximumPositivePricesInDegenerateWindowsDoNotCauseAccumulationOfPrices()
    {
        var candles = Enumerable.Range(0, 8).Select(index => Create(decimal.MaxValue, decimal.MaxValue,
            decimal.MaxValue, At(index))).ToArray();

        var result = new StochasticOscillator(3, 2, 3).Calculate(candles);

        Assert.All(result.Series, item =>
        {
            Assert.NotEmpty(item.Series.Points);
            Assert.All(item.Series.Points, point => Assert.Equal(50m, point.Value));
        });
    }

    [Fact]
    public void SmallestPositivePriceRangeStillNormalizesBothEdgesWithoutUnderflowRepair()
    {
        const decimal low = 0.0000000000000000000000000001m;
        const decimal high = 0.0000000000000000000000000002m;
        var candles = new[] { Create(low, high, low, At(0)), Create(low, high, high, At(1)) };

        var result = new StochasticOscillator(1, 1, 1).Calculate(candles);

        Assert.All(result.Series, item => AssertValues(item.Series, 0m, 100m));
    }

    [Fact]
    public void LargestRepresentableCloseTimeIsPreservedByBothSeries()
    {
        var interval = new CandleInterval(TimeSpan.FromTicks(1));
        var candles = new[] { Create(10m, 20m, 15m, new UtcInstant(DateTimeOffset.MaxValue.AddTicks(-2)), interval: interval),
            Create(10m, 20m, 15m, new UtcInstant(DateTimeOffset.MaxValue.AddTicks(-1)), interval: interval) };

        var result = new StochasticOscillator(2, 1, 1).Calculate(candles);

        Assert.All(result.Series, item => Assert.Equal(new UtcInstant(DateTimeOffset.MaxValue), Assert.Single(item.Series.Points).Time));
    }

    private static void AssertValues(IndicatorSeries series, params decimal[] expected) =>
        Assert.Equal(expected, series.Points.Select(point => point.Value).ToArray());

    private static void AssertInRange(MultiSeriesIndicatorResult result) =>
        Assert.All(result.Series, item => Assert.All(item.Series.Points, point => Assert.InRange(point.Value, 0m, 100m)));

    private static void AssertSameResults(MultiSeriesIndicatorResult expected, MultiSeriesIndicatorResult actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var component = 0; component < expected.Count; component++)
        {
            Assert.Equal(expected.Series[component].Name, actual.Series[component].Name);
            Assert.Equal(expected.Series[component].Series.Points.ToArray(), actual.Series[component].Series.Points.ToArray());
        }
    }

    private static Candle[] Sequence(int count) => Enumerable.Range(0, count).Select(index =>
    {
        var low = 10m + index % 7 / 10m;
        var high = 20m + index % 5 / 10m;
        var close = low + (high - low) * (index * 17 % 101) / 100m;
        return Create(low, high, close, At(index));
    }).ToArray();

    private static Candle[] RangeCloses(params decimal[] closes) =>
        closes.Select((close, index) => Create(10m, 20m, close, At(index))).ToArray();

    private static UtcInstant At(int minutes) => new(Start.Value.AddMinutes(minutes));

    private static Candle Create(decimal low, decimal high, decimal close, UtcInstant time,
        InstrumentId? id = null, CandleInterval? interval = null) =>
        new(id ?? A, interval ?? CandleInterval.OneMinute, time, new Price(close), new Price(high),
            new Price(low), new Price(close), new Quantity(1.0000m));
}
