using VortexTrading.Application.Indicators;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Application.Tests;

public sealed class AverageTrueRangeTests
{
    private static readonly InstrumentId A = new(new Guid("11111111-1111-1111-1111-111111111111"));
    private static readonly InstrumentId B = new(new Guid("22222222-2222-2222-2222-222222222222"));
    private static readonly UtcInstant Start = new(new DateTimeOffset(2026, 10, 3, 10, 0, 0, TimeSpan.Zero));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void RejectsNonpositivePeriods(int period)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new AverageTrueRange(period));

        Assert.Equal("period", exception.ParamName);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(14)]
    [InlineData(int.MaxValue)]
    public void PreservesPositivePeriodAndStableInterfaceName(int period)
    {
        var atr = new AverageTrueRange(period);
        IIndicator indicator = atr;

        Assert.Equal(period, atr.Period);
        Assert.Equal("ATR", indicator.Name);
    }

    [Fact]
    public void RejectsNullInput()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new AverageTrueRange(3).Calculate(null!));

        Assert.Equal("candles", exception.ParamName);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(int.MaxValue)]
    public void EmptyInputReturnsAnEmptyNonNullSeries(int period)
    {
        var result = new AverageTrueRange(period).Calculate([]);

        Assert.NotNull(result);
        Assert.Equal(0, result.Count);
        Assert.Empty(result.Points);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void PeriodThreeDoesNotEmitBeforeThreeCandles(int count)
    {
        var result = new AverageTrueRange(3).Calculate(ReferenceCandles().Take(count).ToArray());

        Assert.Equal(0, result.Count);
        Assert.Empty(result.Points);
    }

    [Fact]
    public void MaximumPeriodAcceptsAShortBatchWithoutOverflowOrAllocationByPeriod()
    {
        var result = new AverageTrueRange(int.MaxValue).Calculate(ReferenceCandles());

        Assert.Empty(result.Points);
    }

    [Fact]
    public void PeriodFourteenNeedsFourteenCandlesRatherThanFifteen()
    {
        var candles = FlatCandles(Enumerable.Repeat(10m, 14).ToArray());
        var atr = new AverageTrueRange(14);

        Assert.Empty(atr.Calculate(candles.Take(13).ToArray()).Points);
        var point = Assert.Single(atr.Calculate(candles).Points);
        Assert.Equal(0m, point.Value);
        Assert.Equal(candles[13].CloseTime, point.Time);
    }

    [Fact]
    public void ExactlyNCandlesProduceOneArithmeticSeedAtTheNthClose()
    {
        var candles = ReferenceCandles().Take(3).ToArray();
        var result = new AverageTrueRange(3).Calculate(candles);

        // First three TRs are 10, 12, 8. The first candle participates in the seed.
        var point = Assert.Single(result.Points);
        Assert.Equal(1, result.Count);
        Assert.Equal(10m, point.Value);
        Assert.Equal(candles[2].CloseTime, point.Time);
        Assert.NotEqual(candles[2].OpenTime, point.Time);
    }

    [Fact]
    public void FirstCandleUsesHighMinusLowWithoutAnExternalPreviousClose()
    {
        var candle = Create(110m, 100m, 105m, Start, open: 102m);

        var point = Assert.Single(new AverageTrueRange(1).Calculate([candle]).Points);

        Assert.Equal(10m, point.Value);
        Assert.Equal(candle.CloseTime, point.Time);
    }

    [Fact]
    public void IntrabarRangeDominatesWhenPreviousCloseIsInsideTheCurrentRange()
    {
        var candles = new[] { Flat(100m, Start), Create(105m, 95m, 100m, At(1)) };

        var result = new AverageTrueRange(1).Calculate(candles);

        Assert.Equal(new[] { 0m, 10m }, Values(result));
    }

    [Fact]
    public void GapUpUsesDistanceFromHighToPreviousCloseRatherThanOnlyIntrabarRange()
    {
        var candles = new[] { Flat(100m, Start), Create(112m, 108m, 110m, At(1)) };

        var result = new AverageTrueRange(1).Calculate(candles);

        Assert.Equal(new[] { 0m, 12m }, Values(result));
    }

    [Fact]
    public void GapDownUsesDistanceFromLowToPreviousCloseRatherThanOnlyIntrabarRange()
    {
        var candles = new[] { Flat(100m, Start), Create(92m, 88m, 90m, At(1)) };

        var result = new AverageTrueRange(1).Calculate(candles);

        Assert.Equal(new[] { 0m, 12m }, Values(result));
    }

    [Fact]
    public void SeedAndTwoSubsequentPointsUseWilderRatherThanConventionalEma()
    {
        var result = new AverageTrueRange(3).Calculate(ReferenceCandles());

        // TRs: 10, 12, 8, 16, 6. Seed=10; (10*2+16)/3=12; (12*2+6)/3=10.
        // Conventional EMA alpha 0.5 would instead produce 10, 13, 9.5.
        Assert.Equal(new[] { 10m, 12m, 10m }, Values(result));
    }

    [Fact]
    public void PeriodOneReproducesEveryTrueRangeAndItsCloseTime()
    {
        var candles = ReferenceCandles();
        var result = new AverageTrueRange(1).Calculate(candles);

        Assert.Equal(new[] { 10m, 12m, 8m, 16m, 6m }, Values(result));
        Assert.Equal(candles.Select(candle => candle.CloseTime).ToArray(),
            result.Points.Select(point => point.Time).ToArray());
    }

    [Fact]
    public void UsesPreviousCloseRatherThanPreviousHighLowOrOpen()
    {
        var candles = new[]
        {
            Create(200m, 1m, 100m, Start, open: 20m),
            Create(112m, 108m, 110m, At(1), open: 108m)
        };

        var result = new AverageTrueRange(1).Calculate(candles);

        Assert.Equal(new[] { 199m, 12m }, Values(result));
    }

    [Fact]
    public void CurrentCloseBecomesPreviousCloseForTheNextTrueRange()
    {
        var candles = new[]
        {
            Flat(100m, Start),
            Create(120m, 100m, 119m, At(1), open: 100m),
            Create(111m, 109m, 110m, At(2))
        };

        var result = new AverageTrueRange(1).Calculate(candles);

        Assert.Equal(new[] { 0m, 20m, 10m }, Values(result));
    }

    [Fact]
    public void CurrentOpenAndVolumeDoNotContributeToTrueRange()
    {
        var candles = ReferenceCandles();
        var changed = candles.Select(candle => new Candle(
            candle.InstrumentId, candle.Interval, candle.OpenTime, candle.Low,
            candle.High, candle.Low, candle.Close, new Quantity(1000m))).ToArray();

        var atr = new AverageTrueRange(3);

        Assert.Equal(atr.Calculate(candles).Points.ToArray(), atr.Calculate(changed).Points.ToArray());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void FlatCandlesWithoutPriceGapsProduceZero(int period)
    {
        var result = new AverageTrueRange(period).Calculate(FlatCandles(10m, 10m, 10m, 10m, 10m));

        Assert.Equal(6 - period, result.Count);
        Assert.All(result.Points, point => Assert.Equal(0m, point.Value));
    }

    [Fact]
    public void FlatCandlesAtDifferentPricesStillHavePriceGapTrueRanges()
    {
        var result = new AverageTrueRange(1).Calculate(FlatCandles(100m, 112m, 100m, 100m));

        Assert.Equal(new[] { 0m, 12m, 12m, 0m }, Values(result));
    }

    [Fact]
    public void ZeroTrueRangeAfterSeedDecaysTheAtrRatherThanResettingIt()
    {
        var candles = ReferenceCandles().Take(3).Append(Flat(110m, At(3))).ToArray();

        var result = new AverageTrueRange(3).Calculate(candles);

        Assert.Equal(new[] { 10m, 6.6666666666666666666666666667m }, Values(result));
    }

    [Fact]
    public void FractionalPricesPreserveExactDecimalSeedAndSmoothing()
    {
        var candles = new[]
        {
            Create(10.35m, 10.10m, 10.20m, At(0)),
            Create(10.50m, 10.30m, 10.40m, At(1)),
            Create(10.45m, 10.25m, 10.35m, At(2)),
            Create(10.65m, 10.45m, 10.55m, At(3)),
            Create(10.60m, 10.40m, 10.50m, At(4))
        };

        var result = new AverageTrueRange(3).Calculate(candles);

        // TRs: 0.25, 0.30, 0.20, 0.30, 0.20; seed=0.25.
        Assert.Equal(new[] { 0.25m, 0.2666666666666666666666666667m, 0.2444444444444444444444444445m }, Values(result));
    }

    [Fact]
    public void NonterminatingSeedUsesNaturalDecimalDivisionWithoutRounding()
    {
        var candles = new[] { Create(3m, 1m, 2m, At(0)), Flat(2m, At(1)), Flat(2m, At(2)) };

        var result = new AverageTrueRange(3).Calculate(candles);

        Assert.Equal(0.6666666666666666666666666667m, Assert.Single(result.Points).Value);
    }

    [Fact]
    public void EveryOutputUsesItsCorrespondingCloseTimeRatherThanOpenTime()
    {
        var candles = ReferenceCandles().Select((candle, index) => new Candle(
            candle.InstrumentId, CandleInterval.FiveMinutes, At(index * 5),
            candle.Open, candle.High, candle.Low, candle.Close, candle.Volume)).ToArray();

        var result = new AverageTrueRange(3).Calculate(candles);

        Assert.Equal(new[] { At(15), At(20), At(25) }, result.Points.Select(point => point.Time).ToArray());
        for (var index = 0; index < result.Count; index++)
        {
            Assert.Equal(candles[index + 2].CloseTime, result.Points[index].Time);
            Assert.NotEqual(candles[index + 2].OpenTime, result.Points[index].Time);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void RejectsMixedInstrumentsEvenBeforeWarmUp(int period)
    {
        var candles = new[] { Flat(10m, Start), Flat(20m, At(1), id: B) };

        var exception = Assert.Throws<ArgumentException>(() => new AverageTrueRange(period).Calculate(candles));

        Assert.Equal("candles", exception.ParamName);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void RejectsMixedIntervalsEvenBeforeWarmUp(int period)
    {
        var candles = new[] { Flat(10m, Start), Flat(20m, At(1), interval: CandleInterval.FiveMinutes) };

        var exception = Assert.Throws<ArgumentException>(() => new AverageTrueRange(period).Calculate(candles));

        Assert.Equal("candles", exception.ParamName);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void RejectsBackwardTimeWithoutSortingEvenBeforeWarmUp(int period)
    {
        var candles = new[] { Flat(10m, At(1)), Flat(20m, Start) };
        var originals = candles.ToArray();

        var exception = Assert.Throws<ArgumentException>(() => new AverageTrueRange(period).Calculate(candles));

        Assert.Equal("candles", exception.ParamName);
        Assert.Same(originals[0], candles[0]);
        Assert.Same(originals[1], candles[1]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void RejectsDuplicateTimeWithoutDeduplicationEvenBeforeWarmUp(int period)
    {
        var first = Flat(10m, Start);
        var candles = new[] { first, first };

        var exception = Assert.Throws<ArgumentException>(() => new AverageTrueRange(period).Calculate(candles));

        Assert.Equal("candles", exception.ParamName);
        Assert.Equal(2, candles.Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void RejectsNullElementsAnywhereEvenBeforeWarmUp(int nullIndex)
    {
        var candles = ReferenceCandles().Take(3).ToArray();
        candles[nullIndex] = null!;

        var exception = Assert.Throws<ArgumentException>(() => new AverageTrueRange(4).Calculate(candles));

        Assert.Equal("candles", exception.ParamName);
    }

    [Fact]
    public void ValidatesTheEntireBatchBeforeSeedArithmeticCanOverflow()
    {
        var candles = new[]
        {
            Create(decimal.MaxValue, 1m, 1m, At(0)),
            Create(decimal.MaxValue, 1m, 1m, At(1)),
            Flat(1m, At(2), id: B)
        };

        var exception = Assert.Throws<ArgumentException>(() => new AverageTrueRange(2).Calculate(candles));

        Assert.Equal("candles", exception.ParamName);
    }

    [Fact]
    public void InstrumentAndIntervalCoherenceUsesExistingValueEquality()
    {
        var candles = new[]
        {
            Flat(10m, Start),
            Flat(12m, At(1), new InstrumentId(A.Value), new CandleInterval(TimeSpan.FromMinutes(1)))
        };
        Assert.NotSame(candles[0].InstrumentId, candles[1].InstrumentId);
        Assert.NotSame(candles[0].Interval, candles[1].Interval);

        var result = new AverageTrueRange(2).Calculate(candles);

        Assert.Equal(1m, Assert.Single(result.Points).Value);
    }

    [Fact]
    public void TemporalGapsDoNotResetPreviousCloseSeedOrAtr()
    {
        var times = new[] { 0, 10, 20, 60, 120 };
        var candles = ReferenceCandles().Select((candle, index) => new Candle(
            candle.InstrumentId, candle.Interval, At(times[index]), candle.Open,
            candle.High, candle.Low, candle.Close, candle.Volume)).ToArray();

        var result = new AverageTrueRange(3).Calculate(candles);

        Assert.Equal(new[] { 10m, 12m, 10m }, Values(result));
        Assert.Equal(new[] { At(21), At(61), At(121) }, result.Points.Select(point => point.Time).ToArray());
    }

    [Fact]
    public void DoesNotModifyInputReferencesOhlcvOrTimestamps()
    {
        var candles = ReferenceCandles().ToList();
        var originals = candles.ToArray();
        var fields = candles.Select(candle => (
            candle.InstrumentId, candle.Interval, candle.OpenTime, candle.CloseTime,
            candle.Open.Value, candle.High.Value, candle.Low.Value, candle.Close.Value, candle.Volume.Value)).ToArray();

        new AverageTrueRange(3).Calculate(candles);

        Assert.Equal(originals.Length, candles.Count);
        for (var index = 0; index < candles.Count; index++) Assert.Same(originals[index], candles[index]);
        Assert.Equal(fields, candles.Select(candle => (
            candle.InstrumentId, candle.Interval, candle.OpenTime, candle.CloseTime,
            candle.Open.Value, candle.High.Value, candle.Low.Value, candle.Close.Value, candle.Volume.Value)).ToArray());
    }

    [Fact]
    public void ResultRemainsAnImmutableSnapshotAfterSourceMutation()
    {
        var candles = ReferenceCandles().ToList();
        var result = new AverageTrueRange(3).Calculate(candles);
        var points = result.Points.ToArray();

        candles[2] = Flat(1m, At(2));
        candles.Clear();
        candles.Add(Flat(1m, Start));

        Assert.Equal(3, result.Count);
        Assert.Equal(points, result.Points.ToArray());
        Assert.Equal(new[] { 10m, 12m, 10m }, Values(result));
        var list = Assert.IsAssignableFrom<IList<IndicatorPoint>>(result.Points);
        Assert.Throws<NotSupportedException>(() => list.Clear());
    }

    [Fact]
    public void RepeatedCalculationsProduceTheSameValuesTimesAndOrder()
    {
        var atr = new AverageTrueRange(3);
        var candles = ReferenceCandles();

        var first = atr.Calculate(candles);
        var second = atr.Calculate(candles);

        Assert.NotSame(first, second);
        Assert.Equal(first.Points.ToArray(), second.Points.ToArray());
    }

    [Fact]
    public void ReusingOneInstanceAcrossBatchesDoesNotRetainPreviousCloseOrAtr()
    {
        var atr = new AverageTrueRange(3);
        var first = atr.Calculate(ReferenceCandles());
        var other = atr.Calculate(FlatCandles(1000m, 1000m, 1000m, 1000m));
        var empty = atr.Calculate(FlatCandles(1m));
        var restarted = atr.Calculate(ReferenceCandles());

        Assert.Equal(new[] { 10m, 12m, 10m }, Values(first));
        Assert.Equal(new[] { 0m, 0m }, Values(other));
        Assert.Empty(empty.Points);
        Assert.Equal(first.Points.ToArray(), restarted.Points.ToArray());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void EveryCommonPrefixPointIsUnchangedByAppendingFutureCandles(int period)
    {
        var candles = ReferenceCandles().Append(Flat(10000m, At(5))).ToArray();
        var atr = new AverageTrueRange(period);
        var extended = atr.Calculate(candles);

        for (var count = 0; count <= candles.Length; count++)
        {
            var prefix = atr.Calculate(candles.Take(count).ToArray());
            Assert.Equal(Math.Max(count - period + 1, 0), prefix.Count);
            Assert.Equal(prefix.Points.ToArray(), extended.Points.Take(prefix.Count).ToArray());
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void MixedSeriesOutputsRemainNonnegativeWithoutClamping(int period)
    {
        var candles = ReferenceCandles().Concat(new[] { Flat(1m, At(5)), Flat(200m, At(6)), Flat(200m, At(7)) }).ToArray();

        var result = new AverageTrueRange(period).Calculate(candles);

        Assert.Equal(candles.Length - period + 1, result.Count);
        Assert.All(result.Points, point => Assert.True(point.Value >= 0m));
    }

    [Fact]
    public void InitialTrueRangeSumOverflowWithValidCandlesPropagates()
    {
        var candles = new[] { Create(decimal.MaxValue, 1m, 1m, At(0)), Create(decimal.MaxValue, 1m, 1m, At(1)) };
        var originals = candles.ToArray();

        Assert.Throws<OverflowException>(() => new AverageTrueRange(2).Calculate(candles));

        Assert.Same(originals[0], candles[0]);
        Assert.Same(originals[1], candles[1]);
    }

    [Fact]
    public void WilderNumeratorOverflowAfterAValidSeedPropagatesInsteadOfReturningPartialOutput()
    {
        var candles = new[]
        {
            Create(decimal.MaxValue, 1m, 1m, At(0)), Flat(1m, At(1)), Flat(1m, At(2)),
            Create(decimal.MaxValue, 1m, 1m, At(3))
        };
        var atr = new AverageTrueRange(3);
        var prefix = atr.Calculate(candles.Take(3).ToArray());

        Assert.Equal((decimal.MaxValue - 1m) / 3m, Assert.Single(prefix.Points).Value);
        Assert.Throws<OverflowException>(() => atr.Calculate(candles));
        Assert.Equal(prefix.Points.ToArray(), atr.Calculate(candles.Take(3).ToArray()).Points.ToArray());
    }

    [Fact]
    public void ExtremePositivePriceGapsUseNormalDecimalAbsIncludingMinimumDecimalDifference()
    {
        var tiny = 0.0000000000000000000000000001m;
        Assert.Equal(decimal.MinValue, tiny - decimal.MaxValue);

        var result = new AverageTrueRange(1).Calculate(FlatCandles(decimal.MaxValue, tiny, decimal.MaxValue));

        Assert.Equal(new[] { 0m, decimal.MaxValue, decimal.MaxValue }, Values(result));
    }

    [Fact]
    public void FlatMaximumDecimalClosesRemainZeroWithoutSummingPrices()
    {
        var result = new AverageTrueRange(3).Calculate(FlatCandles(decimal.MaxValue, decimal.MaxValue, decimal.MaxValue, decimal.MaxValue));

        Assert.Equal(new[] { 0m, 0m }, Values(result));
    }

    [Fact]
    public void CloseTimeAtMaximumRepresentableInstantRemainsSupported()
    {
        var interval = new CandleInterval(TimeSpan.FromTicks(1));
        var candles = new[]
        {
            Flat(10m, new UtcInstant(DateTimeOffset.MaxValue.AddTicks(-2)), interval: interval),
            Flat(12m, new UtcInstant(DateTimeOffset.MaxValue.AddTicks(-1)), interval: interval)
        };

        var point = Assert.Single(new AverageTrueRange(2).Calculate(candles).Points);

        Assert.Equal(1m, point.Value);
        Assert.Equal(new UtcInstant(DateTimeOffset.MaxValue), point.Time);
    }

    private static decimal[] Values(IndicatorSeries series) => series.Points.Select(point => point.Value).ToArray();

    private static Candle[] ReferenceCandles() =>
    [
        Create(105m, 95m, 100m, At(0)),
        Create(112m, 108m, 110m, At(1)),
        Create(114m, 106m, 110m, At(2)),
        Create(118m, 102m, 110m, At(3)),
        Create(113m, 107m, 110m, At(4))
    ];

    private static Candle[] FlatCandles(params decimal[] closes) => closes
        .Select((close, index) => Flat(close, At(index))).ToArray();

    private static UtcInstant At(int minutes) => new(Start.Value.AddMinutes(minutes));

    private static Candle Flat(decimal close, UtcInstant time, InstrumentId? id = null, CandleInterval? interval = null) =>
        Create(close, close, close, time, id, interval);

    private static Candle Create(decimal high, decimal low, decimal close, UtcInstant time,
        InstrumentId? id = null, CandleInterval? interval = null, decimal? open = null) =>
        new(id ?? A, interval ?? CandleInterval.OneMinute, time, new Price(open ?? close),
            new Price(high), new Price(low), new Price(close), new Quantity(1.0000m));
}
