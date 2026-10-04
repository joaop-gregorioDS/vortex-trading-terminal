using System.Globalization;
using VortexTrading.Application.Indicators;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Application.Tests;

public sealed class VolumeWeightedAveragePriceTests
{
    private static readonly InstrumentId A = new(new Guid("11111111-1111-1111-1111-111111111111"));
    private static readonly InstrumentId B = new(new Guid("22222222-2222-2222-2222-222222222222"));
    private static readonly UtcInstant Start = new(new DateTimeOffset(2026, 10, 3, 10, 0, 0, TimeSpan.Zero));
    private const decimal MinimumPositive = 0.0000000000000000000000000001m;

    [Fact]
    public void ImplementsOnlyTheSingleSeriesContractWithoutConfiguration()
    {
        IIndicator indicator = new VolumeWeightedAveragePrice();

        Assert.Equal("VWAP", indicator.Name);
        Assert.True(typeof(VolumeWeightedAveragePrice).IsSealed);
        Assert.False(typeof(IMultiSeriesIndicator).IsAssignableFrom(indicator.GetType()));
        Assert.IsType<IndicatorSeries>(indicator.Calculate([]));
    }

    [Fact]
    public void EmptyInputProducesAnEmptySeries()
    {
        var result = new VolumeWeightedAveragePrice().Calculate([]);

        Assert.Equal(0, result.Count);
        Assert.Empty(result.Points);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(100)]
    public void SinglePositiveVolumeUsesHlcTypicalPriceRatherThanClose(int volume)
    {
        var candle = Create(9m, 18m, 12m, volume, At(0));

        var point = Assert.Single(new VolumeWeightedAveragePrice().Calculate([candle]).Points);

        Assert.Equal(13m, point.Value);
        Assert.NotEqual(candle.Close.Value, point.Value);
        Assert.Equal(candle.CloseTime, point.Time);
    }

    [Fact]
    public void TwoCandlesMatchTheManualVolumeWeightedAverage()
    {
        var candles = new[] { Create(9m, 18m, 12m, 2m, At(0)), Create(15m, 27m, 18m, 6m, At(1)) };

        var result = new VolumeWeightedAveragePrice().Calculate(candles);

        // TP = 13 and 20; (13 * 2 + 20 * 6) / 8 = 18.25, not (13 + 20) / 2.
        AssertValues(result, 13m, 18.25m);
        Assert.NotEqual(16.5m, result.Points[1].Value);
    }

    [Theory]
    [InlineData(1, 9, 19)]
    [InlineData(9, 1, 11)]
    [InlineData(5, 5, 15)]
    public void RelativeVolumeControlsTheWeight(int firstVolume, int secondVolume, int expected)
    {
        var result = new VolumeWeightedAveragePrice().Calculate(
            [Flat(10m, firstVolume, 0), Flat(20m, secondVolume, 1)]);

        AssertValues(result, 10m, expected);
    }

    [Fact]
    public void FractionalPricesAndVolumesHaveExactDecimalResults()
    {
        var candles = new[] { Create(0.6m, 1.8m, 0.9m, 0.25m, At(0)), Create(1.2m, 3m, 1.8m, 0.75m, At(1)) };

        AssertValues(new VolumeWeightedAveragePrice().Calculate(candles), 1.1m, 1.775m);
    }

    [Fact]
    public void OpenDoesNotParticipateInTheTypicalPrice()
    {
        var first = Create(9m, 18m, 12m, 2m, At(0));
        var changedOpen = new Candle(A, first.Interval, first.OpenTime, new Price(18m),
            first.High, first.Low, first.Close, first.Volume);
        var indicator = new VolumeWeightedAveragePrice();

        Assert.Equal(indicator.Calculate([first]).Points.ToArray(), indicator.Calculate([changedOpen]).Points.ToArray());
        AssertValues(indicator.Calculate([changedOpen]), 13m);
    }

    [Fact]
    public void SingleZeroVolumeUsesItsCurrentTypicalPrice()
    {
        AssertValues(new VolumeWeightedAveragePrice().Calculate([Create(9m, 18m, 12m, 0m, At(0))]), 13m);
    }

    [Fact]
    public void ZeroVolumePrefixUsesEachCurrentTypicalPrice()
    {
        AssertValues(new VolumeWeightedAveragePrice().Calculate(
            [Flat(100m, 0m, 0), Flat(20m, 0m, 1), Flat(70m, 0m, 2)]), 100m, 20m, 70m);
    }

    [Fact]
    public void FirstPositiveVolumeDoesNotGiveRetroactiveWeightToZeroVolumePrefix()
    {
        AssertValues(new VolumeWeightedAveragePrice().Calculate(
            [Flat(1000m, 0m, 0), Flat(2000m, 0m, 1), Flat(10m, 2m, 2), Flat(30m, 6m, 3)]),
            1000m, 2000m, 10m, 25m);
    }

    [Fact]
    public void ZeroAfterPositiveVolumePreservesValueAndAdvancesCloseTime()
    {
        var candles = new[] { Flat(10m, 2m, 0), Flat(20m, 1m, 1), Flat(decimal.MaxValue, 0m, 2), Flat(30m, 3m, 3) };
        var result = new VolumeWeightedAveragePrice().Calculate(candles);

        AssertValues(result, 10m, 13.333333333333333333333333333m, 13.333333333333333333333333333m,
            21.666666666666666666666666667m);
        Assert.Equal(result.Points[1].Value, result.Points[2].Value);
        Assert.Equal(candles[2].CloseTime, result.Points[2].Time);
        Assert.NotEqual(result.Points[1].Time, result.Points[2].Time);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(100)]
    public void EntireZeroVolumeBatchEmitsEachTypicalPrice(int count)
    {
        var candles = Enumerable.Range(0, count).Select(index => Flat(index + 1m, 0m, index)).ToArray();
        var result = new VolumeWeightedAveragePrice().Calculate(candles);

        AssertValues(result, Enumerable.Range(0, count).Select(index => index + 1m).ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(14)]
    [InlineData(100)]
    public void NoWarmUpAndOnePointAtEachCorrespondingCloseTime(int count)
    {
        var candles = Sequence(count);
        var result = new VolumeWeightedAveragePrice().Calculate(candles);

        Assert.Equal(count, result.Count);
        for (var index = 0; index < count; index++)
        {
            Assert.Equal(candles[index].CloseTime, result.Points[index].Time);
            Assert.NotEqual(candles[index].OpenTime, result.Points[index].Time);
        }
    }

    [Fact]
    public void RejectsNullCollectionWithCorrectParameter()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new VolumeWeightedAveragePrice().Calculate(null!));

        Assert.Equal("candles", exception.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void RejectsNullCandlesAnywhere(int index)
    {
        var candles = Sequence(5);
        candles[index] = null!;

        var exception = Assert.Throws<ArgumentException>(() => new VolumeWeightedAveragePrice().Calculate(candles));

        Assert.Equal("candles", exception.ParamName);
    }

    [Theory]
    [InlineData("instrument")]
    [InlineData("interval")]
    [InlineData("duplicate")]
    [InlineData("backward")]
    [InlineData("null")]
    public void ValidatesTheWholeBatchBeforeAnyWeightedArithmetic(string invalidKind)
    {
        // The first valid candle would overflow during weighting if arithmetic preceded validation.
        var candles = new[] { Flat(2m, decimal.MaxValue, 0), Flat(3m, 1m, 1), InvalidTail(invalidKind) };

        var exception = Assert.Throws<ArgumentException>(() => new VolumeWeightedAveragePrice().Calculate(candles));

        Assert.Equal("candles", exception.ParamName);
    }

    [Theory]
    [InlineData("instrument")]
    [InlineData("interval")]
    [InlineData("duplicate")]
    [InlineData("backward")]
    public void RejectsIncoherentOrUnorderedInputWithoutRepair(string invalidKind)
    {
        var candles = new[] { Flat(2m, 1m, 0), Flat(3m, 1m, 1), InvalidTail(invalidKind) };
        var before = candles.ToArray();

        var exception = Assert.Throws<ArgumentException>(() => new VolumeWeightedAveragePrice().Calculate(candles));

        Assert.Equal("candles", exception.ParamName);
        Assert.Equal(before, candles);
    }

    [Fact]
    public void AcceptsValueEqualInstrumentAndIntervalInstances()
    {
        var second = Create(20m, 20m, 20m, 3m, At(1), new InstrumentId(A.Value),
            new CandleInterval(TimeSpan.FromMinutes(1)));

        AssertValues(new VolumeWeightedAveragePrice().Calculate([Flat(10m, 1m, 0), second]), 10m, 17.5m);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(60)]
    [InlineData(1440)]
    [InlineData(525600)]
    public void GapsIncludingLongAndCrossDayGapsNeverResetAccumulators(int gapMinutes)
    {
        var candles = new[] { Flat(10m, 1m, 0), Flat(20m, 3m, gapMinutes), Flat(40m, 4m, gapMinutes + 1) };

        var result = new VolumeWeightedAveragePrice().Calculate(candles);

        AssertValues(result, 10m, 17.5m, 28.75m);
        Assert.Equal(candles.Select(candle => candle.CloseTime).ToArray(), result.Points.Select(point => point.Time).ToArray());
    }

    [Fact]
    public void CrossingUtcMidnightContinuesTheSameBatch()
    {
        var beforeMidnight = new UtcInstant(new DateTimeOffset(2026, 10, 3, 23, 58, 0, TimeSpan.Zero));
        var afterMidnight = new UtcInstant(new DateTimeOffset(2026, 10, 4, 0, 1, 0, TimeSpan.Zero));
        var candles = new[] { Create(10m, 10m, 10m, 1m, beforeMidnight), Create(20m, 20m, 20m, 3m, afterMidnight) };

        AssertValues(new VolumeWeightedAveragePrice().Calculate(candles), 10m, 17.5m);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(60)]
    [InlineData(1440)]
    public void IntervalDoesNotSupplyASessionResetRule(int intervalMinutes)
    {
        var interval = new CandleInterval(TimeSpan.FromMinutes(intervalMinutes));
        var candles = new[] { Create(10m, 10m, 10m, 1m, At(0), interval: interval),
            Create(20m, 20m, 20m, 3m, At(intervalMinutes), interval: interval) };
        var result = new VolumeWeightedAveragePrice().Calculate(candles);

        AssertValues(result, 10m, 17.5m);
        Assert.Equal(candles[1].CloseTime, result.Points[1].Time);
    }

    [Fact]
    public void PreservesTheLargestDomainCloseTime()
    {
        var interval = new CandleInterval(TimeSpan.FromTicks(1));
        var candle = Create(1m, 1m, 1m, 1m,
            new UtcInstant(DateTimeOffset.MaxValue.AddTicks(-1)), interval: interval);

        var point = Assert.Single(new VolumeWeightedAveragePrice().Calculate([candle]).Points);

        Assert.Equal(new UtcInstant(DateTimeOffset.MaxValue), point.Time);
        Assert.Equal(1m, point.Value);
    }

    [Fact]
    public void InputReferencesPricesVolumesAndTimestampsRemainUnchanged()
    {
        var candles = Sequence(20);
        var references = candles.ToArray();
        var decimalBits = candles.Select(candle => new[] { candle.Open.Value, candle.High.Value,
            candle.Low.Value, candle.Close.Value, candle.Volume.Value }.SelectMany(decimal.GetBits).ToArray()).ToArray();
        var times = candles.Select(candle => (candle.OpenTime, candle.CloseTime)).ToArray();

        _ = new VolumeWeightedAveragePrice().Calculate(candles);

        for (var index = 0; index < candles.Length; index++)
        {
            Assert.Same(references[index], candles[index]);
            Assert.Equal(decimalBits[index], new[] { candles[index].Open.Value, candles[index].High.Value,
                candles[index].Low.Value, candles[index].Close.Value, candles[index].Volume.Value }.SelectMany(decimal.GetBits).ToArray());
            Assert.Equal(times[index], (candles[index].OpenTime, candles[index].CloseTime));
        }
    }

    [Fact]
    public void ResultIsImmutableAfterSourceReplacementReorderingAndClear()
    {
        var candles = Sequence(5).ToList();
        var result = new VolumeWeightedAveragePrice().Calculate(candles);
        var points = result.Points.ToArray();

        candles[0] = Flat(999m, 99m, 0);
        candles.Reverse();
        candles.Clear();

        Assert.Equal(points, result.Points.ToArray());
        var list = Assert.IsAssignableFrom<IList<IndicatorPoint>>(result.Points);
        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => list.Clear());
        Assert.Throws<NotSupportedException>(() => list[0] = new IndicatorPoint(At(9), 99m));
    }

    [Fact]
    public void ReusingTheSameInstanceStartsEachBatchFromZero()
    {
        var indicator = new VolumeWeightedAveragePrice();

        AssertValues(indicator.Calculate([Flat(10m, 9m, 0), Flat(20m, 1m, 1)]), 10m, 11m);
        AssertValues(indicator.Calculate([Flat(100m, 0m, 0), Flat(40m, 2m, 1)]), 100m, 40m);
        Assert.Empty(indicator.Calculate([]).Points);
        Assert.Throws<ArgumentException>(() => indicator.Calculate([null!]));
        Assert.Throws<OverflowException>(() => indicator.Calculate([Flat(2m, decimal.MaxValue, 0)]));
        AssertValues(indicator.Calculate([Flat(7m, 3m, 0)]), 7m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(20)]
    [InlineData(100)]
    public void RepeatedCalculationsAreExactlyDeterministic(int count)
    {
        var candles = Sequence(count);
        var indicator = new VolumeWeightedAveragePrice();
        var expected = indicator.Calculate(candles);

        for (var run = 0; run < 5; run++)
        {
            var actual = indicator.Calculate(candles);
            Assert.Equal(expected.Points.ToArray(), actual.Points.ToArray());
            for (var index = 0; index < actual.Count; index++)
                Assert.Equal(decimal.GetBits(expected.Points[index].Value), decimal.GetBits(actual.Points[index].Value));
        }
    }

    [Fact]
    public void EveryPrefixMatchesFullBatchIncludingAFutureVolumeShock()
    {
        var candles = Sequence(30).Concat(new[] { Flat(100000m, 1000000000000m, 30) }).ToArray();
        var indicator = new VolumeWeightedAveragePrice();
        var full = indicator.Calculate(candles);

        for (var count = 0; count <= candles.Length; count++)
        {
            var prefix = indicator.Calculate(candles.Take(count).ToArray());
            Assert.Equal(full.Points.Take(count).ToArray(), prefix.Points.ToArray());
        }
        var beforeShock = indicator.Calculate(candles.Take(30).ToArray());
        Assert.Equal(beforeShock.Points.ToArray(), full.Points.Take(30).ToArray());
        Assert.True(full.Points[30].Value > 99999m);
    }

    [Theory]
    [InlineData("1", "0.9999999999999999999999999999")]
    [InlineData("2", "2.0000000000000000000000000001")]
    public void PreservesDirectMeanWhenUnconditionalSplitWouldChangeTheLastDigit(string priceText, string splitText)
    {
        var price = decimal.Parse(priceText, CultureInfo.InvariantCulture);
        var split = price / 3m + price / 3m + price / 3m;

        Assert.Equal(decimal.Parse(splitText, CultureInfo.InvariantCulture), split);
        Assert.NotEqual(price, split);
        AssertValues(new VolumeWeightedAveragePrice().Calculate([Flat(price, 0m, 0)]), price);
    }

    [Fact]
    public void NonterminatingDirectMeanUsesTheNaturalDecimalDivision()
    {
        AssertValues(new VolumeWeightedAveragePrice().Calculate([Create(1m, 2m, 1m, 0m, At(0))]),
            1.3333333333333333333333333333m);
    }

    [Fact]
    public void MinimumPositiveTypicalPriceIsNotDestroyedByUnconditionalSplitUnderflow()
    {
        Assert.Equal(0m, MinimumPositive / 3m + MinimumPositive / 3m + MinimumPositive / 3m);
        AssertValues(new VolumeWeightedAveragePrice().Calculate([Flat(MinimumPositive, 0m, 0)]), MinimumPositive);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void MaximumPricesAvoidArtificialTypicalPriceSumOverflow(int volume)
    {
        var candle = Flat(decimal.MaxValue, volume, 0);
        Assert.Throws<OverflowException>(() => candle.High.Value + candle.Low.Value + candle.Close.Value);

        AssertValues(new VolumeWeightedAveragePrice().Calculate([candle]), decimal.MaxValue);
    }

    [Fact]
    public void LargeSplitFallbackHasTheDocumentedFinitePrecisionLoss()
    {
        var price = decimal.MaxValue - 2m;
        var candle = Flat(price, 0m, 0);
        Assert.Throws<OverflowException>(() => candle.High.Value + candle.Low.Value + candle.Close.Value);

        var point = Assert.Single(new VolumeWeightedAveragePrice().Calculate([candle]).Points);

        Assert.Equal(decimal.MaxValue - 3m, point.Value);
        Assert.NotEqual(price, point.Value);
    }

    [Fact]
    public void NonflatLargePricesUseTheSameOrderedSplitFallback()
    {
        var candle = Create(3m, decimal.MaxValue, 6m, 0m, At(0));
        Assert.Throws<OverflowException>(() => candle.High.Value + candle.Low.Value + candle.Close.Value);
        var expected = decimal.MaxValue / 3m + 1m + 2m;

        AssertValues(new VolumeWeightedAveragePrice().Calculate([candle]), expected);
    }

    [Fact]
    public void PropagatesTrueWeightedPriceOverflow()
    {
        var candle = Flat(2m, decimal.MaxValue, 0);
        Assert.Throws<OverflowException>(() => 2m * candle.Volume.Value);

        Assert.Throws<OverflowException>(() => new VolumeWeightedAveragePrice().Calculate([candle]));
    }

    [Fact]
    public void PropagatesCumulativeWeightedOverflowWhenIndividualProductsAndVolumeSumFit()
    {
        var candles = new[] { Flat(decimal.MaxValue, 1m, 0), Flat(decimal.MaxValue, 1m, 1) };
        Assert.Equal(decimal.MaxValue, candles[0].Close.Value * candles[0].Volume.Value);
        Assert.Equal(2m, candles[0].Volume.Value + candles[1].Volume.Value);
        AssertValues(new VolumeWeightedAveragePrice().Calculate(candles.Take(1).ToArray()), decimal.MaxValue);

        Assert.Throws<OverflowException>(() => new VolumeWeightedAveragePrice().Calculate(candles));
    }

    [Fact]
    public void PropagatesCumulativeVolumeOverflowEvenThoughWeightedSumFits()
    {
        var candles = new[] { Flat(MinimumPositive, decimal.MaxValue, 0), Flat(MinimumPositive, decimal.MaxValue, 1) };
        var product = MinimumPositive * candles[0].Volume.Value;
        Assert.InRange(product + product, 15m, 16m);
        Assert.Single(new VolumeWeightedAveragePrice().Calculate(candles.Take(1).ToArray()).Points);
        Assert.Throws<OverflowException>(() => candles[0].Volume.Value + candles[1].Volume.Value);

        Assert.Throws<OverflowException>(() => new VolumeWeightedAveragePrice().Calculate(candles));
    }

    [Fact]
    public void NaturalWeightedUnderflowIsNotRoundedUpOrRepaired()
    {
        AssertValues(new VolumeWeightedAveragePrice().Calculate([Flat(MinimumPositive, MinimumPositive, 0)]), 0m);
    }

    private static Candle InvalidTail(string kind) => kind switch
    {
        "instrument" => Create(3m, 3m, 3m, 1m, At(2), id: B),
        "interval" => Create(3m, 3m, 3m, 1m, At(2), interval: CandleInterval.FiveMinutes),
        "duplicate" => Flat(3m, 1m, 1),
        "backward" => Flat(3m, 1m, 0),
        "null" => null!,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static void AssertValues(IndicatorSeries series, params decimal[] expected) =>
        Assert.Equal(expected, series.Points.Select(point => point.Value).ToArray());

    private static Candle[] Sequence(int count) => Enumerable.Range(0, count).Select(index =>
        Create(10m + index % 7 / 10m, 20m + index % 5 / 10m, 15m + index % 3 / 10m,
            index % 5 == 0 ? 0m : index % 4 + 0.125m, At(index))).ToArray();

    private static UtcInstant At(int minutes) => new(Start.Value.AddMinutes(minutes));

    private static Candle Flat(decimal price, decimal volume, int minutes) => Create(price, price, price, volume, At(minutes));

    private static Candle Create(decimal low, decimal high, decimal close, decimal volume, UtcInstant time,
        InstrumentId? id = null, CandleInterval? interval = null) =>
        new(id ?? A, interval ?? CandleInterval.OneMinute, time, new Price(close), new Price(high),
            new Price(low), new Price(close), new Quantity(volume));
}
