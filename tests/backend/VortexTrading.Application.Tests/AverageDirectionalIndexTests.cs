using VortexTrading.Application.Indicators;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Application.Tests;

public sealed class AverageDirectionalIndexTests
{
    private static readonly InstrumentId A = new(new Guid("11111111-1111-1111-1111-111111111111"));
    private static readonly InstrumentId B = new(new Guid("22222222-2222-2222-2222-222222222222"));
    private static readonly UtcInstant Start = new(new DateTimeOffset(2026, 10, 3, 10, 0, 0, TimeSpan.Zero));
    private const decimal MinimumPositive = 0.0000000000000000000000000001m;

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(14)]
    [InlineData(1073741824)]
    [InlineData(int.MaxValue)]
    public void PreservesTheReadOnlyPeriodAndMultiSeriesContract(int period)
    {
        var adx = new AverageDirectionalIndex(period);
        IMultiSeriesIndicator indicator = adx;

        Assert.Equal("ADX", indicator.Name);
        Assert.Equal(period, adx.Period);
        Assert.True(typeof(AverageDirectionalIndex).IsSealed);
        Assert.False(typeof(IIndicator).IsAssignableFrom(adx.GetType()));
        Assert.Null(typeof(AverageDirectionalIndex).GetProperty(nameof(adx.Period))!.SetMethod);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void RejectsNonpositivePeriods(int period)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new AverageDirectionalIndex(period));

        Assert.Equal("period", exception.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(28)]
    [InlineData(40)]
    public void AlwaysReturnsOnlyThreePublicSeriesInOrderWithExactLookupReferences(int count)
    {
        var result = new AverageDirectionalIndex(14).Calculate(Sequence(count));

        Assert.Equal(3, result.Count);
        Assert.Equal(new[] { "+DI", "-DI", "ADX" }, result.Series.Select(item => item.Name).ToArray());
        foreach (var item in result.Series)
        {
            Assert.True(result.TryGetSeries(item.Name, out var found));
            Assert.Same(item.Series, found);
        }
        foreach (var name in new[] { "TR", "+DM", "-DM", "SmoothedTR", "DX", "adx" })
        {
            Assert.False(result.TryGetSeries(name, out var missing));
            Assert.Null(missing);
        }
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 0)]
    [InlineData(2, 1)]
    [InlineData(2, 2)]
    [InlineData(14, 13)]
    [InlineData(14, 14)]
    [InlineData(1073741824, 5)]
    [InlineData(int.MaxValue, 5)]
    public void NoTransitionsOrInsufficientSeedRetainsThreeEmptySeries(int period, int count)
    {
        var result = new AverageDirectionalIndex(period).Calculate(Sequence(count));

        Assert.Equal(3, result.Count);
        Assert.All(result.Series, item => Assert.Empty(item.Series.Points));
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(1, 5)]
    [InlineData(2, 3)]
    [InlineData(2, 4)]
    [InlineData(2, 5)]
    [InlineData(3, 4)]
    [InlineData(3, 5)]
    [InlineData(3, 6)]
    [InlineData(3, 10)]
    [InlineData(14, 15)]
    [InlineData(14, 27)]
    [InlineData(14, 28)]
    [InlineData(14, 40)]
    public void IndependentLengthsAndAlignmentFollowTheExactWarmUp(int period, int count)
    {
        var candles = Sequence(count);
        var result = new AverageDirectionalIndex(period).Calculate(candles);
        var diCount = Math.Max(0, count - period);
        var adxCount = (int)Math.Max(0L, count - 2L * period + 1);

        Assert.Equal(diCount, result.Series[0].Series.Count);
        Assert.Equal(diCount, result.Series[1].Series.Count);
        Assert.Equal(adxCount, result.Series[2].Series.Count);
        for (var index = 0; index < diCount; index++)
        {
            Assert.Equal(candles[index + period].CloseTime, result.Series[0].Series.Points[index].Time);
            Assert.Equal(candles[index + period].CloseTime, result.Series[1].Series.Points[index].Time);
        }
        for (var index = 0; index < adxCount; index++)
        {
            var time = candles[index + 2 * period - 1].CloseTime;
            Assert.Equal(time, result.Series[2].Series.Points[index].Time);
            Assert.Equal(time, result.Series[0].Series.Points[index + period - 1].Time);
            Assert.Equal(time, result.Series[1].Series.Points[index + period - 1].Time);
            Assert.NotEqual(candles[index + 2 * period - 1].OpenTime, time);
        }
    }

    [Fact]
    public void ConventionalFourteenStartsDiAtCandleFifteenAndAdxAtTwentyEight()
    {
        var candles = Sequence(30);
        var indicator = new AverageDirectionalIndex(14);

        Assert.All(indicator.Calculate(candles.Take(14).ToArray()).Series, item => Assert.Empty(item.Series.Points));
        var firstDi = indicator.Calculate(candles.Take(15).ToArray());
        Assert.Equal(candles[14].CloseTime, Assert.Single(firstDi.Series[0].Series.Points).Time);
        Assert.Equal(candles[14].CloseTime, Assert.Single(firstDi.Series[1].Series.Points).Time);
        Assert.Empty(firstDi.Series[2].Series.Points);
        Assert.Empty(indicator.Calculate(candles.Take(27).ToArray()).Series[2].Series.Points);
        Assert.Equal(candles[27].CloseTime,
            Assert.Single(indicator.Calculate(candles.Take(28).ToArray()).Series[2].Series.Points).Time);
    }

    [Fact]
    public void RejectsNullCollectionWithCorrectParameter()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new AverageDirectionalIndex(14).Calculate(null!));

        Assert.Equal("candles", exception.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void NullElementsAreRejectedAcrossTheWholeInput(int index)
    {
        var candles = Sequence(5);
        candles[index] = null!;

        var exception = Assert.Throws<ArgumentException>(() => new AverageDirectionalIndex(14).Calculate(candles));

        Assert.Equal("candles", exception.ParamName);
    }

    [Theory]
    [InlineData("null", 14)]
    [InlineData("instrument", 14)]
    [InlineData("interval", 14)]
    [InlineData("duplicate", 14)]
    [InlineData("backward", 14)]
    [InlineData("null", 1)]
    [InlineData("instrument", 1)]
    [InlineData("interval", 1)]
    [InlineData("duplicate", 1)]
    [InlineData("backward", 1)]
    public void ValidatesCoherenceAndOrderingBeforeWarmUpOrReturn(string kind, int period)
    {
        var candles = new[] { Flat(10m, 0), Flat(11m, 1), InvalidTail(kind, 2) };
        var before = candles.ToArray();

        var exception = Assert.Throws<ArgumentException>(() => new AverageDirectionalIndex(period).Calculate(candles));

        Assert.Equal("candles", exception.ParamName);
        Assert.Equal(before, candles);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("instrument")]
    [InlineData("interval")]
    [InlineData("duplicate")]
    [InlineData("backward")]
    public void InvalidTailIsRejectedBeforeAnEarlierSeedCouldOverflow(string kind)
    {
        var candles = SeedOverflowSequence().Concat(new[] { InvalidTail(kind, 3) }).ToArray();

        var exception = Assert.Throws<ArgumentException>(() => new AverageDirectionalIndex(2).Calculate(candles));

        Assert.Equal("candles", exception.ParamName);
    }

    [Fact]
    public void AcceptsValueEqualInstrumentsAndIntervals()
    {
        var second = Create(11m, 11m, 11m, At(1), new InstrumentId(A.Value),
            new CandleInterval(TimeSpan.FromMinutes(1)));

        AssertValues(new AverageDirectionalIndex(1).Calculate([Flat(10m, 0), second]), [100m], [0m], [100m]);
    }

    [Fact]
    public void IntrabarRangeDominatesTrueRangeAndNormalizesPositiveMovement()
    {
        var candles = new[] { Create(8m, 10m, 9m, At(0)), Create(7m, 12m, 10m, At(1)) };

        // TR=5, UpMove=2 > DownMove=1, so +DI=2/5*100 and -DI=0.
        AssertValues(new AverageDirectionalIndex(1).Calculate(candles), [40m], [0m], [100m]);
    }

    [Fact]
    public void PreviousCloseToHighGapDominatesTrueRange()
    {
        var candles = new[] { Flat(5m, 0), Create(8m, 10m, 9m, At(1)) };

        // TR=5 rather than intrabar 2; +DM=5.
        AssertValues(new AverageDirectionalIndex(1).Calculate(candles), [100m], [0m], [100m]);
    }

    [Fact]
    public void PreviousCloseToLowGapDominatesTrueRange()
    {
        var candles = new[] { Flat(15m, 0), Create(10m, 12m, 11m, At(1)) };

        // TR=5 rather than intrabar 2; -DM=5.
        AssertValues(new AverageDirectionalIndex(1).Calculate(candles), [0m], [100m], [100m]);
    }

    [Fact]
    public void FirstCandleIntrabarRangeIsNotAnAtrStyleSeedContribution()
    {
        var candles = new[] { Create(1m, decimal.MaxValue, 1m, At(0)), Flat(1m, 1), Flat(2m, 2) };

        // Only transitions contribute: TR=0 then 1, +DM=0 then 1.
        AssertValues(new AverageDirectionalIndex(2).Calculate(candles), [100m], [0m], []);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    public void EqualPositiveUpAndDownMovesDiscardBothDirections(int movement)
    {
        var candles = new[] { Create(10m, 20m, 15m, At(0)),
            Create(10m - movement, 20m + movement, 15m, At(1)) };

        AssertValues(new AverageDirectionalIndex(1).Calculate(candles), [0m], [0m], [0m]);
    }

    [Fact]
    public void BothNonpositiveMovesProduceZeroDirectionalMovement()
    {
        var candles = new[] { Create(8m, 10m, 9m, At(0)), Create(8.5m, 9m, 8.75m, At(1)) };

        AssertValues(new AverageDirectionalIndex(1).Calculate(candles), [0m], [0m], [0m]);
    }

    [Fact]
    public void GreaterDownMoveWinsEvenWhenUpMoveIsAlsoPositive()
    {
        var candles = new[] { Create(10m, 20m, 15m, At(0)), Create(6m, 22m, 15m, At(1)) };

        AssertValues(new AverageDirectionalIndex(1).Calculate(candles), [0m], [25m], [100m]);
    }

    [Fact]
    public void ManualWilderSumsDiDxSeedAndRecurrenceMatchExactly()
    {
        var result = new AverageDirectionalIndex(2).Calculate(ManualSequence());

        // First two transitions: (TR,+DM,-DM)=(6,6,0),(4,0,2).
        // Sum seed: (10,6,2), DI=(60,20), DX=50.
        // Next: (TR,+DM,-DM)=(4,0,2), smoothed=(9,3,3), DX=0.
        // First ADX=(50+0)/2=25. Next unchanged bar yields (8.5,1.5,1.5), DX=0, ADX=12.5.
        AssertValues(result, [60m, (3m / 9m) * 100m, (1.5m / 8.5m) * 100m],
            [20m, (3m / 9m) * 100m, (1.5m / 8.5m) * 100m], [25m, 12.5m]);
        Assert.NotEqual(25m * (1m - 2m / 3m), result.Series[2].Series.Points[1].Value);
    }

    [Fact]
    public void MinimumDecimalSeedDistinguishesSmoothedSumsFromRoundedMeans()
    {
        var candles = new[] { Flat(MinimumPositive, 0), Flat(2m * MinimumPositive, 1),
            Flat(2m * MinimumPositive, 2), Flat(2m * MinimumPositive, 3) };

        // N=2: sums TR=+DM=1e-28; premature division by N would round both to zero.
        Assert.Equal(0m, MinimumPositive / 2m);
        AssertValues(new AverageDirectionalIndex(2).Calculate(candles), [100m, 100m], [0m, 0m], [100m]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(14)]
    public void CompletelyFlatMarketProducesOnlyZeroOutputs(int period)
    {
        var candles = Enumerable.Range(0, 40).Select(index => Flat(10m, index)).ToArray();
        var result = new AverageDirectionalIndex(period).Calculate(candles);

        Assert.All(result.Series, item =>
        {
            Assert.NotEmpty(item.Series.Points);
            Assert.All(item.Series.Points, point => Assert.Equal(0m, point.Value));
        });
    }

    [Fact]
    public void NonzeroTrueRangeWithNoDirectionalMovementHasZeroDxAndAdx()
    {
        var candles = Enumerable.Range(0, 6).Select(index => Create(10m, 20m, 15m, At(index))).ToArray();

        AssertValues(new AverageDirectionalIndex(2).Calculate(candles), [0m, 0m, 0m, 0m],
            [0m, 0m, 0m, 0m], [0m, 0m, 0m]);
    }

    [Fact]
    public void PeriodOneEqualsCurrentDxAndDoesNotCarryAcrossZeroDenominators()
    {
        var candles = new[] { Flat(10m, 0), Flat(11m, 1), Flat(11m, 2),
            Flat(9m, 3), Create(8m, 10m, 9m, At(4)) };

        // Positive, flat TR=0, negative, and equal positive directional tie.
        AssertValues(new AverageDirectionalIndex(1).Calculate(candles), [100m, 0m, 0m, 0m],
            [0m, 0m, 100m, 0m], [100m, 0m, 100m, 0m]);
    }

    [Fact]
    public void ZeroDiDenominatorAfterPositiveDxReturnsZeroRatherThanCarryingDx()
    {
        var candles = new[] { Create(8m, 12m, 10m, At(0)), Create(9m, 13m, 11m, At(1)),
            Create(9m, 13m, 11m, At(2)) };

        AssertValues(new AverageDirectionalIndex(1).Calculate(candles), [25m, 0m], [0m, 0m], [100m, 0m]);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(14, true)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(14, false)]
    public void PerfectDirectionalTrendsHaveOneActiveDiAndAdxOneHundred(int period, bool up)
    {
        var candles = Enumerable.Range(0, 40).Select(index => Flat(up ? 10m + index : 100m - index, index)).ToArray();
        var result = new AverageDirectionalIndex(period).Calculate(candles);

        Assert.All(result.Series[up ? 0 : 1].Series.Points, point => Assert.Equal(100m, point.Value));
        Assert.All(result.Series[up ? 1 : 0].Series.Points, point => Assert.Equal(0m, point.Value));
        Assert.NotEmpty(result.Series[2].Series.Points);
        Assert.All(result.Series[2].Series.Points, point => Assert.Equal(100m, point.Value));
    }

    [Fact]
    public void FractionalPricesPreserveTheManualDecimalCalculation()
    {
        var candles = ManualSequence().Select(candle => Create(candle.Low.Value / 10m, candle.High.Value / 10m,
            candle.Close.Value / 10m, candle.OpenTime)).ToArray();

        AssertValues(new AverageDirectionalIndex(2).Calculate(candles),
            [60m, (0.3m / 0.9m) * 100m, (0.15m / 0.85m) * 100m],
            [20m, (0.3m / 0.9m) * 100m, (0.15m / 0.85m) * 100m], [25m, 12.5m]);
    }

    [Fact]
    public void OpenAndVolumeDoNotParticipateInAdx()
    {
        var candles = Sequence(40);
        var changed = candles.Select((candle, index) => new Candle(candle.InstrumentId, candle.Interval,
            candle.OpenTime, candle.High, candle.High, candle.Low, candle.Close,
            new Quantity(index % 2 == 0 ? 0m : decimal.MaxValue))).ToArray();
        var indicator = new AverageDirectionalIndex(14);

        AssertSameResults(indicator.Calculate(candles), indicator.Calculate(changed));
    }

    [Fact]
    public void PreviousCloseContributesToTrueRangeButCurrentCloseOnlyAffectsLaterTransitions()
    {
        var current = Create(6m, 17m, 10m, At(1));
        var indicator = new AverageDirectionalIndex(1);
        var lowPreviousClose = indicator.Calculate([Create(5m, 15m, 5m, At(0)), current]);
        var highPreviousClose = indicator.Calculate([Create(5m, 15m, 15m, At(0)), current]);

        Assert.Equal((2m / 12m) * 100m, lowPreviousClose.Series[0].Series.Points[0].Value);
        Assert.Equal((2m / 11m) * 100m, highPreviousClose.Series[0].Series.Points[0].Value);
        AssertSameResults(lowPreviousClose, indicator.Calculate(
            [Create(5m, 15m, 5m, At(0)), Create(6m, 17m, 17m, At(1))]));
    }

    [Fact]
    public void LargeDirectionalRatioDividesBeforeMultiplyingByOneHundred()
    {
        var candles = new[] { Flat(1m, 0), Flat(decimal.MaxValue, 1) };
        Assert.Throws<OverflowException>(() => (candles[1].High.Value - candles[0].High.Value) * 100m);

        AssertValues(new AverageDirectionalIndex(1).Calculate(candles), [100m], [0m], [100m]);
    }

    [Fact]
    public void TrueRangeSeedOverflowPropagatesEvenIfAnAverageWouldFit()
    {
        var candles = SeedOverflowSequence();

        Assert.Throws<OverflowException>(() => new AverageDirectionalIndex(2).Calculate(candles));
    }

    [Fact]
    public void TrueRangeRecurrenceOverflowPropagatesAfterARepresentableSeedAndPrefix()
    {
        var candles = new[] { Flat(1m, 0), Flat(2m, 1), Flat(3m, 2),
            Create(1m, decimal.MaxValue, 1m, At(3)), Create(1m, decimal.MaxValue, 1m, At(4)) };
        var indicator = new AverageDirectionalIndex(2);

        var prefix = indicator.Calculate(candles.Take(4).ToArray());
        Assert.Equal(2, prefix.Series[0].Series.Count);
        Assert.Single(prefix.Series[2].Series.Points);
        Assert.Throws<OverflowException>(() => indicator.Calculate(candles));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(14)]
    [InlineData(30)]
    public void PublicValuesRemainInRangeAcrossMixedInsideExpandingAndOscillatingMarkets(int period)
    {
        var datasets = new[]
        {
            Sequence(96),
            Enumerable.Range(0, 96).Select(index => Create(1m + index / 2m, 200m - index / 2m, 100.5m, At(index))).ToArray(),
            Enumerable.Range(0, 96).Select(index => Create(90m - index / 10m, 110m + index / 10m, 100m, At(index))).ToArray(),
            Enumerable.Range(0, 96).Select(index => Flat(30m + index * 7 % 23, index)).ToArray()
        };
        foreach (var candles in datasets)
        {
            var result = new AverageDirectionalIndex(period).Calculate(candles);
            Assert.All(result.Series, item =>
            {
                Assert.NotEmpty(item.Series.Points);
                Assert.All(item.Series.Points, point => Assert.InRange(point.Value, 0m, 100m));
            });
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(60)]
    [InlineData(1440)]
    [InlineData(525600)]
    public void TemporalGapsPreserveTheManualWilderValuesAndUseActualCloseTimes(int gapMinutes)
    {
        var candles = ManualSequence().Select((candle, index) => Create(candle.Low.Value, candle.High.Value,
            candle.Close.Value, At(index * gapMinutes))).ToArray();
        var indicator = new AverageDirectionalIndex(2);
        var expected = indicator.Calculate(ManualSequence());
        var actual = indicator.Calculate(candles);

        for (var component = 0; component < 3; component++)
        {
            var offset = component == 2 ? 3 : 2;
            Assert.Equal(expected.Series[component].Series.Points.Select(point => point.Value).ToArray(),
                actual.Series[component].Series.Points.Select(point => point.Value).ToArray());
            for (var index = 0; index < actual.Series[component].Series.Count; index++)
                Assert.Equal(candles[index + offset].CloseTime, actual.Series[component].Series.Points[index].Time);
        }
    }

    [Fact]
    public void InputReferencesDecimalBitsAndTimesRemainUnchanged()
    {
        var candles = Sequence(40);
        var references = candles.ToArray();
        var bits = candles.Select(candle => new[] { candle.Open.Value, candle.High.Value, candle.Low.Value,
            candle.Close.Value, candle.Volume.Value }.SelectMany(decimal.GetBits).ToArray()).ToArray();
        var times = candles.Select(candle => (candle.OpenTime, candle.CloseTime)).ToArray();

        _ = new AverageDirectionalIndex(14).Calculate(candles);

        for (var index = 0; index < candles.Length; index++)
        {
            Assert.Same(references[index], candles[index]);
            Assert.Equal(bits[index], new[] { candles[index].Open.Value, candles[index].High.Value, candles[index].Low.Value,
                candles[index].Close.Value, candles[index].Volume.Value }.SelectMany(decimal.GetBits).ToArray());
            Assert.Equal(times[index], (candles[index].OpenTime, candles[index].CloseTime));
        }
    }

    [Fact]
    public void ResultsRemainImmutableAfterReplacingReorderingAndClearingTheSource()
    {
        var candles = ManualSequence().ToList();
        var result = new AverageDirectionalIndex(2).Calculate(candles);
        var points = result.Series.Select(item => item.Series.Points.ToArray()).ToArray();

        candles[0] = Flat(999m, 0);
        candles.Reverse();
        candles.Clear();

        for (var component = 0; component < result.Count; component++)
        {
            Assert.Equal(points[component], result.Series[component].Series.Points.ToArray());
            var list = Assert.IsAssignableFrom<IList<IndicatorPoint>>(result.Series[component].Series.Points);
            Assert.True(list.IsReadOnly);
            Assert.Throws<NotSupportedException>(() => list.Clear());
            Assert.Throws<NotSupportedException>(() => list[0] = new IndicatorPoint(At(9), 99m));
        }
        var seriesList = Assert.IsAssignableFrom<IList<NamedIndicatorSeries>>(result.Series);
        Assert.True(seriesList.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => seriesList.Clear());
    }

    [Fact]
    public void SameInstanceHasNoStateAcrossSuccessEmptyValidationFailureOrOverflow()
    {
        var indicator = new AverageDirectionalIndex(2);
        var expected = indicator.Calculate(ManualSequence());
        var flat = Enumerable.Range(0, 6).Select(index => Flat(20m, index)).ToArray();

        Assert.All(indicator.Calculate(flat).Series, item => Assert.All(item.Series.Points, point => Assert.Equal(0m, point.Value)));
        Assert.All(indicator.Calculate([]).Series, item => Assert.Empty(item.Series.Points));
        Assert.Throws<ArgumentException>(() => indicator.Calculate([null!]));
        Assert.Throws<OverflowException>(() => indicator.Calculate(SeedOverflowSequence()));
        AssertSameResults(expected, indicator.Calculate(ManualSequence()));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(14)]
    public void RepeatedCalculationsAreExactlyDeterministic(int period)
    {
        var indicator = new AverageDirectionalIndex(period);
        var candles = Sequence(72);
        var expected = indicator.Calculate(candles);

        for (var run = 0; run < 5; run++)
        {
            var actual = indicator.Calculate(candles);
            AssertSameResults(expected, actual);
            for (var component = 0; component < 3; component++)
            {
                for (var index = 0; index < actual.Series[component].Series.Count; index++)
                    Assert.Equal(decimal.GetBits(expected.Series[component].Series.Points[index].Value),
                        decimal.GetBits(actual.Series[component].Series.Points[index].Value));
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(14)]
    [InlineData(30)]
    public void AllThreeSeriesArePrefixInvariantIncludingAFutureExtremePriceShock(int period)
    {
        var candles = Sequence(72).Concat(new[] { Create(MinimumPositive, 1000000m, 500000m, At(72)) }).ToArray();
        var indicator = new AverageDirectionalIndex(period);
        var full = indicator.Calculate(candles);

        for (var count = 0; count <= candles.Length; count++)
        {
            var prefix = indicator.Calculate(candles.Take(count).ToArray());
            for (var component = 0; component < 3; component++)
            {
                var points = prefix.Series[component].Series.Points;
                Assert.Equal(points.ToArray(), full.Series[component].Series.Points.Take(points.Count).ToArray());
            }
        }
        Assert.True(full.Series[0].Series.Points[^1].Value > 99m);
    }

    [Fact]
    public void MirroringPricesSwapsDirectionalIndicesAndPreservesAdx()
    {
        var candles = ManualSequence();
        var mirror = candles.Select(candle => Create(100m - candle.High.Value, 100m - candle.Low.Value,
            100m - candle.Close.Value, candle.OpenTime)).ToArray();
        var indicator = new AverageDirectionalIndex(2);
        var original = indicator.Calculate(candles);
        var reflected = indicator.Calculate(mirror);

        Assert.Equal(original.Series[0].Series.Points.ToArray(), reflected.Series[1].Series.Points.ToArray());
        Assert.Equal(original.Series[1].Series.Points.ToArray(), reflected.Series[0].Series.Points.ToArray());
        Assert.Equal(original.Series[2].Series.Points.ToArray(), reflected.Series[2].Series.Points.ToArray());
    }

    [Fact]
    public void LargestRepresentableCloseTimeBelongsToAllEligibleOutputs()
    {
        var interval = new CandleInterval(TimeSpan.FromTicks(1));
        var candles = new[] { Create(10m, 10m, 10m, new UtcInstant(DateTimeOffset.MaxValue.AddTicks(-2)), interval: interval),
            Create(11m, 11m, 11m, new UtcInstant(DateTimeOffset.MaxValue.AddTicks(-1)), interval: interval) };
        var result = new AverageDirectionalIndex(1).Calculate(candles);

        Assert.All(result.Series, item => Assert.Equal(new UtcInstant(DateTimeOffset.MaxValue), Assert.Single(item.Series.Points).Time));
    }

    private static Candle[] ManualSequence() => [Create(8m, 10m, 10m, At(0)),
        Create(14m, 16m, 16m, At(1)), Create(12m, 14m, 14m, At(2)),
        Create(10m, 14m, 12m, At(3)), Create(10m, 14m, 12m, At(4))];

    private static Candle[] SeedOverflowSequence() => [Flat(1m, 0),
        Create(1m, decimal.MaxValue, 1m, At(1)), Create(1m, decimal.MaxValue, 1m, At(2))];

    private static Candle InvalidTail(string kind, int ordinal) => kind switch
    {
        "null" => null!,
        "instrument" => Create(10m, 20m, 15m, At(ordinal), id: B),
        "interval" => Create(10m, 20m, 15m, At(ordinal), interval: CandleInterval.FiveMinutes),
        "duplicate" => Flat(10m, ordinal - 1),
        "backward" => Flat(10m, ordinal - 2),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static void AssertValues(MultiSeriesIndicatorResult result, decimal[] plus, decimal[] minus, decimal[] adx)
    {
        Assert.Equal(plus, result.Series[0].Series.Points.Select(point => point.Value).ToArray());
        Assert.Equal(minus, result.Series[1].Series.Points.Select(point => point.Value).ToArray());
        Assert.Equal(adx, result.Series[2].Series.Points.Select(point => point.Value).ToArray());
    }

    private static void AssertSameResults(MultiSeriesIndicatorResult expected, MultiSeriesIndicatorResult actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var index = 0; index < expected.Count; index++)
        {
            Assert.Equal(expected.Series[index].Name, actual.Series[index].Name);
            Assert.Equal(expected.Series[index].Series.Points.ToArray(), actual.Series[index].Series.Points.ToArray());
        }
    }

    private static Candle[] Sequence(int count) => Enumerable.Range(0, count).Select(index =>
        Create(10m + index % 7 / 10m, 20m + index % 5 / 10m, 15m + index % 3 / 10m, At(index))).ToArray();

    private static UtcInstant At(int minutes) => new(Start.Value.AddMinutes(minutes));

    private static Candle Flat(decimal price, int minutes) => Create(price, price, price, At(minutes));

    private static Candle Create(decimal low, decimal high, decimal close, UtcInstant time,
        InstrumentId? id = null, CandleInterval? interval = null) =>
        new(id ?? A, interval ?? CandleInterval.OneMinute, time, new Price(close), new Price(high),
            new Price(low), new Price(close), new Quantity(1m));
}
