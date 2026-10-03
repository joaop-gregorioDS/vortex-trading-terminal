using VortexTrading.Application.Indicators;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Application.Tests;

public sealed class RelativeStrengthIndexTests
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
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new RelativeStrengthIndex(period));

        Assert.Equal("period", exception.ParamName);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(14)]
    [InlineData(int.MaxValue)]
    public void PreservesPositivePeriodAndStableInterfaceName(int period)
    {
        var rsi = new RelativeStrengthIndex(period);
        IIndicator indicator = rsi;

        Assert.Equal(period, rsi.Period);
        Assert.Equal("RSI", indicator.Name);
    }

    [Fact]
    public void RejectsNullInput()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new RelativeStrengthIndex(3).Calculate(null!));

        Assert.Equal("candles", exception.ParamName);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(int.MaxValue)]
    public void EmptyInputReturnsAnEmptyNonNullSeries(int period)
    {
        var result = new RelativeStrengthIndex(period).Calculate([]);

        Assert.NotNull(result);
        Assert.Equal(0, result.Count);
        Assert.Empty(result.Points);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void PeriodThreeDoesNotEmitBeforeFourCandles(int count)
    {
        var result = new RelativeStrengthIndex(3).Calculate(Candles(10m, 12m, 11m).Take(count).ToArray());

        Assert.Equal(0, result.Count);
        Assert.Empty(result.Points);
    }

    [Fact]
    public void MaximumPeriodAcceptsAShortBatchWithoutIntegerOverflowOrAllocationByPeriod()
    {
        var result = new RelativeStrengthIndex(int.MaxValue).Calculate(Candles(10m, 20m));

        Assert.Empty(result.Points);
    }

    [Fact]
    public void PeriodFourteenNeedsFifteenCandlesForItsFirstPoint()
    {
        var candles = Candles(Enumerable.Repeat(10m, 15).ToArray());
        var rsi = new RelativeStrengthIndex(14);

        Assert.Empty(rsi.Calculate(candles.Take(14).ToArray()).Points);
        var point = Assert.Single(rsi.Calculate(candles).Points);
        Assert.Equal(50m, point.Value);
        Assert.Equal(candles[14].CloseTime, point.Time);
    }

    [Fact]
    public void ExactlyNPlusOneCandlesProduceOnePointAtTheirLastClose()
    {
        var candles = Candles(10m, 11m, 12m, 13m);
        var result = new RelativeStrengthIndex(3).Calculate(candles);

        var point = Assert.Single(result.Points);
        Assert.Equal(1, result.Count);
        Assert.Equal(100m, point.Value);
        Assert.Equal(candles[3].CloseTime, point.Time);
        Assert.NotEqual(candles[3].OpenTime, point.Time);
    }

    [Fact]
    public void AllLossesProduceZeroAtTheSeed()
    {
        var result = new RelativeStrengthIndex(3).Calculate(Candles(13m, 12m, 11m, 10m));

        Assert.Equal(0m, Assert.Single(result.Points).Value);
    }

    [Fact]
    public void FlatMarketProducesFiftyAtTheSeed()
    {
        var result = new RelativeStrengthIndex(3).Calculate(Candles(10m, 10m, 10m, 10m));

        Assert.Equal(50m, Assert.Single(result.Points).Value);
    }

    [Theory]
    [InlineData(10, 11, 100)]
    [InlineData(11, 10, 0)]
    [InlineData(10, 10, 50)]
    public void PeriodOneUsesTheCurrentChangeAfterTwoCandles(int previous, int current, int expected)
    {
        var candles = Candles(previous, current);
        var rsi = new RelativeStrengthIndex(1);

        Assert.Empty(rsi.Calculate(candles.Take(1).ToArray()).Points);
        var point = Assert.Single(rsi.Calculate(candles).Points);
        Assert.Equal((decimal)expected, point.Value);
        Assert.Equal(candles[1].CloseTime, point.Time);
    }

    [Fact]
    public void PeriodOneRecurrenceForgetsEarlierGainsLossesAndFlatChanges()
    {
        var result = new RelativeStrengthIndex(1).Calculate(Candles(10m, 11m, 10m, 10m, 12m));

        Assert.Equal(new[] { 100m, 0m, 50m, 100m }, Values(result));
    }

    [Fact]
    public void MixedSeedUsesTheArithmeticAverageOfTheFirstNChanges()
    {
        var result = new RelativeStrengthIndex(3).Calculate(Candles(10m, 12m, 11m, 14m));

        // Gains sum to 5, losses to 1. Decimal 5/3 divided by decimal 1/3 is
        // 5.0000000000000000000000000006, so the exact .NET RSI ends in 34.
        Assert.Equal(83.33333333333333333333333334m, Assert.Single(result.Points).Value);
    }

    [Fact]
    public void TwoPointsAfterTheSeedUseWilderSmoothingRatherThanConventionalEmaAlpha()
    {
        var result = new RelativeStrengthIndex(2).Calculate(Candles(10m, 13m, 12m, 14m, 11m));

        // Seed: averages 1.5 and 0.5, RS=3, RSI=75.
        // +2: averages (1.5+2)/2=1.75 and 0.5/2=0.25, RSI=87.5.
        // -3: averages 1.75/2=0.875 and (0.25+3)/2=1.625;
        // natural decimal RS=0.5384615384615384615384615385.
        Assert.Equal(new[] { 75m, 87.5m, 35.000000000000000000000000002m }, Values(result));
    }

    [Fact]
    public void ZeroLossRemainsOneHundredAcrossGainsAndFlatContinuation()
    {
        var result = new RelativeStrengthIndex(3).Calculate(Candles(10m, 11m, 12m, 13m, 13m, 15m, 15m));

        Assert.Equal(new[] { 100m, 100m, 100m, 100m }, Values(result));
    }

    [Fact]
    public void ZeroGainRemainsZeroAcrossLossesAndFlatContinuation()
    {
        var result = new RelativeStrengthIndex(3).Calculate(Candles(15m, 14m, 13m, 12m, 12m, 10m, 10m));

        Assert.Equal(new[] { 0m, 0m, 0m, 0m }, Values(result));
    }

    [Fact]
    public void EntirelyFlatContinuationRemainsFifty()
    {
        var result = new RelativeStrengthIndex(3).Calculate(Candles(10m, 10m, 10m, 10m, 10m, 10m));

        Assert.Equal(new[] { 50m, 50m, 50m }, Values(result));
    }

    [Fact]
    public void AFlatSeedCanTransitionToGainThenLossWithoutRemainingNeutral()
    {
        var result = new RelativeStrengthIndex(2).Calculate(Candles(10m, 10m, 10m, 12m, 11m));

        Assert.Equal(new[] { 50m, 100m, 50m }, Values(result));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(14)]
    public void EveryMixedSeriesOutputIsWithinTheRsiRange(int period)
    {
        var closes = new[] { 10m, 100m, 1m, 30m, 30m, 7m, 15m, 2m, 8m, 50m, 3m, 20m, 20m, 10m, 80m, 5m, 5m, 9m };
        var result = new RelativeStrengthIndex(period).Calculate(Candles(closes));

        Assert.Equal(closes.Length - period, result.Count);
        Assert.All(result.Points, point => Assert.InRange(point.Value, 0m, 100m));
    }

    [Fact]
    public void UsesCloseRatherThanOtherOhlcvFieldsForSeedAndSmoothing()
    {
        var candles = new[] { 10m, 13m, 12m, 14m, 11m }.Select((close, index) => new Candle(
            A, CandleInterval.OneMinute, At(index), new Price(1m), new Price(100m),
            new Price(1m), new Price(close), new Quantity(1000m))).ToArray();

        var result = new RelativeStrengthIndex(2).Calculate(candles);

        Assert.Equal(new[] { 75m, 87.5m, 35.000000000000000000000000002m }, Values(result));
    }

    [Fact]
    public void FractionalPricesKeepExactDecimalSeedAndRecursiveOutputsWithoutRounding()
    {
        var result = new RelativeStrengthIndex(3).Calculate(Candles(10.10m, 10.35m, 10.20m, 10.55m, 10.30m, 10.45m));

        // Seed averages: 0.20 and 0.05 -> RS=4 -> RSI=80.
        // Next averages: decimal 0.4/3 and 0.35/3.
        // Then: (previous gain*2+0.15)/3 and previous loss*2/3.
        Assert.Equal(new[] { 80m, 53.333333333333333333333333319m, 64.102564102564102564102564097m }, Values(result));
    }

    [Fact]
    public void EveryOutputUsesItsCorrespondingCloseTimeRatherThanOpenTime()
    {
        var candles = new[]
        {
            Create(10m, At(0), interval: CandleInterval.FiveMinutes),
            Create(13m, At(5), interval: CandleInterval.FiveMinutes),
            Create(12m, At(10), interval: CandleInterval.FiveMinutes),
            Create(14m, At(15), interval: CandleInterval.FiveMinutes),
            Create(11m, At(20), interval: CandleInterval.FiveMinutes)
        };
        var result = new RelativeStrengthIndex(2).Calculate(candles);

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
        var candles = new[] { Create(10m, Start), Create(20m, At(1), id: B) };

        var exception = Assert.Throws<ArgumentException>(() => new RelativeStrengthIndex(period).Calculate(candles));

        Assert.Equal("candles", exception.ParamName);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void RejectsMixedIntervalsEvenBeforeWarmUp(int period)
    {
        var candles = new[] { Create(10m, Start), Create(20m, At(1), interval: CandleInterval.FiveMinutes) };

        var exception = Assert.Throws<ArgumentException>(() => new RelativeStrengthIndex(period).Calculate(candles));

        Assert.Equal("candles", exception.ParamName);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void RejectsBackwardTimeWithoutSortingEvenBeforeWarmUp(int period)
    {
        var candles = new[] { Create(10m, At(1)), Create(20m, Start) };
        var originals = candles.ToArray();

        var exception = Assert.Throws<ArgumentException>(() => new RelativeStrengthIndex(period).Calculate(candles));

        Assert.Equal("candles", exception.ParamName);
        Assert.Same(originals[0], candles[0]);
        Assert.Same(originals[1], candles[1]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void RejectsDuplicateTimeWithoutDeduplicationEvenBeforeWarmUp(int period)
    {
        var first = Create(10m, Start);
        var candles = new[] { first, first };

        var exception = Assert.Throws<ArgumentException>(() => new RelativeStrengthIndex(period).Calculate(candles));

        Assert.Equal("candles", exception.ParamName);
        Assert.Equal(2, candles.Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void RejectsNullElementsAnywhereEvenBeforeWarmUp(int nullIndex)
    {
        var candles = Candles(10m, 20m, 30m);
        candles[nullIndex] = null!;

        var exception = Assert.Throws<ArgumentException>(() => new RelativeStrengthIndex(3).Calculate(candles));

        Assert.Equal("candles", exception.ParamName);
    }

    [Fact]
    public void ValidatesLaterCandlesBeforeSeedArithmeticCanOverflow()
    {
        var candles = Candles(1m, decimal.MaxValue, 1m, decimal.MaxValue, 1m);
        candles[4] = Create(1m, At(4), id: B);

        var exception = Assert.Throws<ArgumentException>(() => new RelativeStrengthIndex(3).Calculate(candles));

        Assert.Equal("candles", exception.ParamName);
    }

    [Fact]
    public void InstrumentAndIntervalCoherenceUsesExistingValueEquality()
    {
        var candles = new[]
        {
            Create(10m, Start),
            Create(11m, At(1), new InstrumentId(A.Value), new CandleInterval(TimeSpan.FromMinutes(1)))
        };
        Assert.NotSame(candles[0].InstrumentId, candles[1].InstrumentId);
        Assert.NotSame(candles[0].Interval, candles[1].Interval);

        var result = new RelativeStrengthIndex(1).Calculate(candles);

        Assert.Equal(100m, Assert.Single(result.Points).Value);
    }

    [Fact]
    public void GapsDoNotResetTheSeedOrRecursiveAverages()
    {
        var times = new[] { 0, 1, 10, 30, 60 };
        var closes = new[] { 10m, 13m, 12m, 14m, 11m };
        var candles = closes.Select((close, index) => Create(close, At(times[index]))).ToArray();
        var result = new RelativeStrengthIndex(2).Calculate(candles);

        Assert.Equal(new[] { 75m, 87.5m, 35.000000000000000000000000002m }, Values(result));
        Assert.Equal(new[] { At(11), At(31), At(61) }, result.Points.Select(point => point.Time).ToArray());
    }

    [Fact]
    public void DoesNotModifyInputReferencesOhlcvOrTimestamps()
    {
        var candles = Candles(10.10m, 10.35m, 10.20m, 10.55m, 10.30m).ToList();
        var originals = candles.ToArray();
        var fields = candles.Select(candle => (
            candle.InstrumentId, candle.Interval, candle.OpenTime, candle.CloseTime,
            candle.Open.Value, candle.High.Value, candle.Low.Value, candle.Close.Value, candle.Volume.Value)).ToArray();

        new RelativeStrengthIndex(3).Calculate(candles);

        Assert.Equal(originals.Length, candles.Count);
        for (var index = 0; index < candles.Count; index++) Assert.Same(originals[index], candles[index]);
        Assert.Equal(fields, candles.Select(candle => (
            candle.InstrumentId, candle.Interval, candle.OpenTime, candle.CloseTime,
            candle.Open.Value, candle.High.Value, candle.Low.Value, candle.Close.Value, candle.Volume.Value)).ToArray());
    }

    [Fact]
    public void ResultRemainsAnImmutableSnapshotAfterSourceMutation()
    {
        var candles = Candles(10m, 13m, 12m, 14m).ToList();
        var result = new RelativeStrengthIndex(2).Calculate(candles);
        var points = result.Points.ToArray();

        candles[2] = Create(100m, At(2));
        candles.Clear();
        candles.Add(Create(1m, Start));

        Assert.Equal(2, result.Count);
        Assert.Equal(points, result.Points.ToArray());
        Assert.Equal(new[] { 75m, 87.5m }, Values(result));
        var list = Assert.IsAssignableFrom<IList<IndicatorPoint>>(result.Points);
        Assert.Throws<NotSupportedException>(() => list.Clear());
    }

    [Fact]
    public void RepeatedCalculationsProduceTheSameValuesTimesAndOrder()
    {
        var rsi = new RelativeStrengthIndex(3);
        var candles = Candles(10.10m, 10.35m, 10.20m, 10.55m, 10.30m, 10.45m);

        var first = rsi.Calculate(candles);
        var second = rsi.Calculate(candles);

        Assert.NotSame(first, second);
        Assert.Equal(first.Points.ToArray(), second.Points.ToArray());
    }

    [Fact]
    public void ReusingOneInstanceAcrossBatchesDoesNotRetainCloseGainOrLoss()
    {
        var rsi = new RelativeStrengthIndex(2);
        var first = rsi.Calculate(Candles(10m, 13m, 12m, 14m));
        var other = rsi.Calculate(Candles(100m, 90m, 80m, 70m));
        var empty = rsi.Calculate(Candles(1m));
        var restarted = rsi.Calculate(Candles(10m, 13m, 12m, 14m));

        Assert.Equal(new[] { 75m, 87.5m }, Values(first));
        Assert.Equal(new[] { 0m, 0m }, Values(other));
        Assert.Empty(empty.Points);
        Assert.Equal(first.Points.ToArray(), restarted.Points.ToArray());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(14)]
    public void EveryCommonPrefixPointIsUnchangedByAppendingFutureCandles(int period)
    {
        var candles = Candles(10m, 13m, 12m, 14m, 11m, 20m, 20m, 5m, 8m, 10m, 30m, 12m, 12m, 15m, 50m, 2m, 10000m);
        var rsi = new RelativeStrengthIndex(period);
        var extended = rsi.Calculate(candles);

        for (var count = 0; count <= candles.Length; count++)
        {
            var prefix = rsi.Calculate(candles.Take(count).ToArray());
            Assert.Equal(Math.Max(count - period, 0), prefix.Count);
            Assert.Equal(prefix.Points.ToArray(), extended.Points.Take(prefix.Count).ToArray());
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SeedGainOrLossSumOverflowWithValidCandlesPropagates(bool gainsOverflow)
    {
        var candles = gainsOverflow
            ? Candles(1m, decimal.MaxValue, 1m, decimal.MaxValue)
            : Candles(decimal.MaxValue, 1m, decimal.MaxValue, 1m);
        var originals = candles.ToArray();

        Assert.Throws<OverflowException>(() => new RelativeStrengthIndex(3).Calculate(candles));

        for (var index = 0; index < candles.Length; index++) Assert.Same(originals[index], candles[index]);
    }

    [Fact]
    public void RelativeStrengthOverflowWithValidPricesPropagatesWithoutNumericFallback()
    {
        var candles = Candles(1m, 0.999999999999999999999999999m, decimal.MaxValue);

        // Nonzero average loss is 5e-28, while average gain is near MaxValue/2.
        Assert.Throws<OverflowException>(() => new RelativeStrengthIndex(2).Calculate(candles));
    }

    [Fact]
    public void FlatMaximumDecimalClosesRemainNeutralWithoutSummingPrices()
    {
        var result = new RelativeStrengthIndex(2).Calculate(Candles(decimal.MaxValue, decimal.MaxValue, decimal.MaxValue, decimal.MaxValue));

        Assert.Equal(new[] { 50m, 50m }, Values(result));
    }

    [Fact]
    public void CloseTimeAtMaximumRepresentableInstantRemainsSupported()
    {
        var interval = new CandleInterval(TimeSpan.FromTicks(1));
        var candles = new[]
        {
            Create(10m, new UtcInstant(DateTimeOffset.MaxValue.AddTicks(-2)), interval: interval),
            Create(11m, new UtcInstant(DateTimeOffset.MaxValue.AddTicks(-1)), interval: interval)
        };

        var point = Assert.Single(new RelativeStrengthIndex(1).Calculate(candles).Points);

        Assert.Equal(100m, point.Value);
        Assert.Equal(new UtcInstant(DateTimeOffset.MaxValue), point.Time);
    }

    private static decimal[] Values(IndicatorSeries series) => series.Points.Select(point => point.Value).ToArray();

    private static Candle[] Candles(params decimal[] closes) => closes
        .Select((close, index) => Create(close, At(index))).ToArray();

    private static UtcInstant At(int minutes) => new(Start.Value.AddMinutes(minutes));

    private static Candle Create(decimal close, UtcInstant time, InstrumentId? id = null, CandleInterval? interval = null)
    {
        var price = new Price(close);
        return new Candle(id ?? A, interval ?? CandleInterval.OneMinute, time,
            price, price, price, price, new Quantity(1.0000m));
    }
}
