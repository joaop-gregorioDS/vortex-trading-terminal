using System.Globalization;
using VortexTrading.Application.Indicators;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Application.Tests;

public sealed class OnBalanceVolumeTests
{
    private static readonly InstrumentId A = new(new Guid("11111111-1111-1111-1111-111111111111"));
    private static readonly InstrumentId B = new(new Guid("22222222-2222-2222-2222-222222222222"));
    private static readonly UtcInstant Start = new(new DateTimeOffset(2026, 10, 4, 10, 0, 0, TimeSpan.Zero));
    private const decimal MinimumPositive = 0.0000000000000000000000000001m;

    [Fact]
    public void ImplementsOnlyTheSingleSeriesContractWithoutConfiguration()
    {
        IIndicator indicator = new OnBalanceVolume();

        Assert.Equal("OBV", indicator.Name);
        Assert.True(typeof(OnBalanceVolume).IsSealed);
        Assert.False(typeof(IMultiSeriesIndicator).IsAssignableFrom(indicator.GetType()));
        Assert.IsType<IndicatorSeries>(indicator.Calculate([]));
        Assert.Single(typeof(OnBalanceVolume).GetConstructors());
        Assert.Empty(typeof(OnBalanceVolume).GetConstructors()[0].GetParameters());
        Assert.Equal(["Name"], typeof(OnBalanceVolume).GetProperties().Select(property => property.Name).ToArray());
    }

    [Fact]
    public void EmptyInputProducesAnEmptySeries()
    {
        var result = new OnBalanceVolume().Calculate([]);

        Assert.Equal(0, result.Count);
        Assert.Empty(result.Points);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("500")]
    [InlineData("1.25")]
    [InlineData("0.0000000000000000000000000001")]
    [InlineData("79228162514264337593543950335")]
    public void FirstVolumeIsIrrelevantToTheZeroBaseline(string volume)
    {
        var candle = Flat(10m, Parse(volume), 0);

        var point = Assert.Single(new OnBalanceVolume().Calculate([candle]).Points);

        Assert.Equal(0m, point.Value);
        Assert.Equal(candle.CloseTime, point.Time);
    }

    [Theory]
    [InlineData(11, 100)]
    [InlineData(9, -100)]
    [InlineData(10, 0)]
    public void DirectionUsesTheCurrentVolumeRatherThanTheFirstVolume(int close, int expected)
    {
        var candles = new[] { Flat(10m, 500m, 0), Flat(close, 100m, 1) };

        AssertValues(new OnBalanceVolume().Calculate(candles), 0m, expected);
    }

    [Fact]
    public void MixedManualSequenceMatchesEverySignedBalance()
    {
        var candles = new[]
        {
            Flat(10m, 500m, 0), Flat(11m, 100m, 1), Flat(12m, 50m, 2),
            Flat(12m, decimal.MaxValue, 3), Flat(9m, 200m, 4),
            Flat(10m, 25m, 5), Flat(8m, 75m, 6)
        };

        AssertValues(new OnBalanceVolume().Calculate(candles), 0m, 100m, 150m, 150m, -50m, -25m, -100m);
    }

    [Theory]
    [InlineData(11, 100)]
    [InlineData(9, -100)]
    [InlineData(10, 0)]
    public void MaximumFirstVolumeNeverEntersTheAccumulation(int close, int expected)
    {
        AssertValues(new OnBalanceVolume().Calculate([Flat(10m, decimal.MaxValue, 0), Flat(close, 100m, 1)]),
            0m, expected);
    }

    [Theory]
    [InlineData(11, 100)]
    [InlineData(9, -100)]
    [InlineData(10, 0)]
    public void EqualCloseIgnoresMaximumVolumeAfterAPositiveNegativeOrZeroBalance(int close, int expected)
    {
        AssertValues(new OnBalanceVolume().Calculate(
            [Flat(10m, 500m, 0), Flat(close, 100m, 1), Flat(close, decimal.MaxValue, 2)]),
            0m, expected, expected);
    }

    [Theory]
    [InlineData(11)]
    [InlineData(9)]
    [InlineData(10)]
    public void ZeroVolumePreservesTheBalanceButStillProducesANewPoint(int close)
    {
        var candles = new[] { Flat(8m, 500m, 0), Flat(10m, 100m, 1), Flat(close, 0m, 2) };
        var result = new OnBalanceVolume().Calculate(candles);

        AssertValues(result, 0m, 100m, 100m);
        Assert.Equal(candles[2].CloseTime, result.Points[2].Time);
    }

    [Theory]
    [InlineData(12, 11, -25)]
    [InlineData(8, 9, 25)]
    public void ZeroVolumeCandleStillEstablishesTheImmediatelyPreviousClose(int zeroClose, int nextClose, int expected)
    {
        // Comparing with the first Close instead would reverse the final direction.
        AssertValues(new OnBalanceVolume().Calculate(
            [Flat(10m, 500m, 0), Flat(zeroClose, 0m, 1), Flat(nextClose, 25m, 2)]),
            0m, 0m, expected);
    }

    [Fact]
    public void FractionalVolumesAccumulateExactly()
    {
        AssertValues(new OnBalanceVolume().Calculate(
            [Flat(10m, 500m, 0), Flat(11m, 1.25m, 1), Flat(10m, 0.50m, 2)]), 0m, 1.25m, 0.75m);
    }

    [Fact]
    public void NegativeBalancesRemainSignedAndCanRecoverToPositive()
    {
        AssertValues(new OnBalanceVolume().Calculate(
            [Flat(10m, 500m, 0), Flat(9m, 100m, 1), Flat(8m, 50m, 2), Flat(9m, 200m, 3)]),
            0m, -100m, -150m, 50m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(1000)]
    public void ThereIsOneCloseTimePointPerCandleWithoutWarmUp(int count)
    {
        var candles = Sequence(count);
        var result = new OnBalanceVolume().Calculate(candles);

        Assert.Equal(count, result.Count);
        for (var index = 0; index < count; index++)
        {
            Assert.Equal(candles[index].CloseTime, result.Points[index].Time);
            Assert.NotEqual(candles[index].OpenTime, result.Points[index].Time);
        }
    }

    [Fact]
    public void RejectsANullCollectionWithTheExpectedParameter()
    {
        Assert.Equal("candles", Assert.Throws<ArgumentNullException>(() => new OnBalanceVolume().Calculate(null!)).ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void RejectsANullElementAtAnyPosition(int index)
    {
        var candles = Sequence(4);
        candles[index] = null!;

        Assert.Equal("candles", Assert.Throws<ArgumentException>(() => new OnBalanceVolume().Calculate(candles)).ParamName);
    }

    [Fact]
    public void RejectsANullSingleCandle()
    {
        Assert.Equal("candles", Assert.Throws<ArgumentException>(() => new OnBalanceVolume().Calculate([null!])).ParamName);
    }

    [Theory]
    [InlineData("instrument")]
    [InlineData("interval")]
    [InlineData("duplicate")]
    [InlineData("backward")]
    [InlineData("null")]
    public void RejectsInvalidShortInputWithoutSortingOrRepair(string kind)
    {
        Assert.Equal("candles", Assert.Throws<ArgumentException>(() => new OnBalanceVolume().Calculate(
            [Flat(10m, 500m, 1), InvalidTail(kind, 1, 0)])).ParamName);
    }

    [Theory]
    [InlineData("instrument", 1)]
    [InlineData("interval", 1)]
    [InlineData("duplicate", 1)]
    [InlineData("backward", 1)]
    [InlineData("null", 1)]
    [InlineData("instrument", -1)]
    [InlineData("interval", -1)]
    [InlineData("duplicate", -1)]
    [InlineData("backward", -1)]
    [InlineData("null", -1)]
    public void WholeBatchValidationPrecedesBothKindsOfEarlierArithmeticOverflow(string kind, int direction)
    {
        var valid = OverflowSequence(direction);
        Assert.Throws<OverflowException>(() => new OnBalanceVolume().Calculate(valid));
        var invalid = valid.Append(InvalidTail(kind, 2, 1)).ToArray();

        Assert.Equal("candles", Assert.Throws<ArgumentException>(() => new OnBalanceVolume().Calculate(invalid)).ParamName);
    }

    [Fact]
    public void CoherenceUsesExistingValueEqualityInsteadOfReferenceIdentity()
    {
        var id = new InstrumentId(A.Value);
        var interval = new CandleInterval(CandleInterval.OneMinute.Duration);
        Assert.NotSame(A, id);
        Assert.NotSame(CandleInterval.OneMinute, interval);

        AssertValues(new OnBalanceVolume().Calculate(
            [Flat(10m, 500m, 0), Create(11m, 100m, At(1), id, interval)]), 0m, 100m);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(60)]
    [InlineData(1440)]
    [InlineData(525600)]
    public void TemporalGapsDoNotResetPadOrChangeThePreviousSuppliedClose(int gapMinutes)
    {
        var candles = new[] { Flat(10m, 500m, 0), Flat(11m, 100m, 1), Flat(9m, 25m, gapMinutes + 1) };

        AssertValues(new OnBalanceVolume().Calculate(candles), 0m, 100m, 75m);
    }

    [Fact]
    public void CrossingUtcMidnightDoesNotResetTheBalance()
    {
        var before = new UtcInstant(new DateTimeOffset(2026, 10, 4, 23, 58, 0, TimeSpan.Zero));
        var candles = new[]
        {
            Create(10m, 500m, before),
            Create(11m, 100m, new UtcInstant(before.Value.AddMinutes(1))),
            Create(12m, 25m, new UtcInstant(before.Value.AddMinutes(2)))
        };
        Assert.NotEqual(candles[1].OpenTime.Value.Date, candles[2].OpenTime.Value.Date);

        var result = new OnBalanceVolume().Calculate(candles);

        AssertValues(result, 0m, 100m, 125m);
        Assert.Equal(candles.Select(candle => candle.CloseTime), result.Points.Select(point => point.Time));
    }

    [Fact]
    public void HighLowAndOpenDoNotParticipateInTheMathematics()
    {
        var original = Sequence(20);
        var changed = original.Select(candle => new Candle(candle.InstrumentId, candle.Interval, candle.OpenTime,
            new Price(decimal.MaxValue), new Price(decimal.MaxValue), new Price(MinimumPositive),
            candle.Close, candle.Volume)).ToArray();
        var indicator = new OnBalanceVolume();

        AssertExact(indicator.Calculate(original), indicator.Calculate(changed));
    }

    [Fact]
    public void MinimumPriceChangesAreComparedWithoutRounding()
    {
        AssertValues(new OnBalanceVolume().Calculate(
            [Flat(MinimumPositive, 500m, 0), Flat(2m * MinimumPositive, 10m, 1), Flat(MinimumPositive, 3m, 2)]),
            0m, 10m, 7m);
    }

    [Fact]
    public void NumericallyEqualClosesWithDifferentDecimalScalesIgnoreVolume()
    {
        var first = Flat(10.0m, 500m, 0);
        var second = Flat(10.000m, decimal.MaxValue, 1);
        Assert.NotEqual(decimal.GetBits(first.Close.Value), decimal.GetBits(second.Close.Value));

        AssertValues(new OnBalanceVolume().Calculate([first, second]), 0m, 0m);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void MinimumPositiveVolumeRemainsSignedWithoutQuantization(int direction)
    {
        AssertValues(new OnBalanceVolume().Calculate(
            [Flat(10m, 500m, 0), Flat(10m + direction, MinimumPositive, 1)]), 0m, direction * MinimumPositive);
    }

    [Fact]
    public void CalculationPreservesInputReferencesValuesAndTimestamps()
    {
        var candles = Sequence(30).ToList();
        var references = candles.ToArray();
        var closeBits = candles.Select(candle => decimal.GetBits(candle.Close.Value)).ToArray();
        var volumeBits = candles.Select(candle => decimal.GetBits(candle.Volume.Value)).ToArray();
        var times = candles.Select(candle => (candle.OpenTime, candle.CloseTime)).ToArray();

        new OnBalanceVolume().Calculate(candles);

        Assert.Equal(references.Length, candles.Count);
        for (var index = 0; index < candles.Count; index++)
        {
            Assert.Same(references[index], candles[index]);
            Assert.Equal(closeBits[index], decimal.GetBits(candles[index].Close.Value));
            Assert.Equal(volumeBits[index], decimal.GetBits(candles[index].Volume.Value));
            Assert.Equal(times[index], (candles[index].OpenTime, candles[index].CloseTime));
        }
    }

    [Fact]
    public void ResultIsAnImmutableSnapshotDespiteSubsequentSourceChanges()
    {
        var source = Sequence(10).ToList();
        var result = new OnBalanceVolume().Calculate(source);
        var expected = result.Points.ToArray();

        source[0] = Flat(100m, decimal.MaxValue, 0);
        source.Reverse();
        source.Clear();

        Assert.Equal(expected, result.Points.ToArray());
        var mutableView = Assert.IsAssignableFrom<IList<IndicatorPoint>>(result.Points);
        Assert.True(mutableView.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => mutableView[0] = new IndicatorPoint(At(0), 99m));
        Assert.Throws<NotSupportedException>(() => mutableView.Add(new IndicatorPoint(At(0), 99m)));
        Assert.Throws<NotSupportedException>(() => mutableView.Clear());
        Assert.Equal(expected, result.Points.ToArray());
    }

    [Fact]
    public void ReuseAcrossDifferentBatchesAndEmptyInputRetainsNoBalanceOrPreviousClose()
    {
        var indicator = new OnBalanceVolume();
        AssertValues(indicator.Calculate([Flat(10m, 500m, 0), Flat(11m, 100m, 1)]), 0m, 100m);
        Assert.Empty(indicator.Calculate([]).Points);

        AssertValues(indicator.Calculate([Flat(100m, decimal.MaxValue, 0), Flat(99m, 25m, 1)]), 0m, -25m);
        AssertValues(indicator.Calculate([Flat(1m, 999m, 0)]), 0m);
    }

    [Fact]
    public void InstanceIsReusableAfterValidationFailure()
    {
        var indicator = new OnBalanceVolume();
        Assert.Throws<ArgumentException>(() => indicator.Calculate([Flat(10m, 500m, 0), null!]));

        AssertValues(indicator.Calculate([Flat(100m, 999m, 0), Flat(101m, 25m, 1)]), 0m, 25m);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void BothOverflowsPropagateAndLeaveTheInstanceReusable(int direction)
    {
        var indicator = new OnBalanceVolume();
        var candles = OverflowSequence(direction);
        AssertValues(indicator.Calculate(candles.Take(2).ToArray()), 0m, direction * decimal.MaxValue);

        Assert.Throws<OverflowException>(() => indicator.Calculate(candles));

        AssertValues(indicator.Calculate([Flat(100m, 999m, 0), Flat(101m, 25m, 1)]), 0m, 25m);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void ExtremeBalanceCanBeCancelledWithoutAccumulatingRawVolume(int direction)
    {
        AssertValues(new OnBalanceVolume().Calculate(
            [Flat(10m, decimal.MaxValue, 0), Flat(10m + direction, decimal.MaxValue, 1), Flat(10m, decimal.MaxValue, 2)]),
            0m, direction * decimal.MaxValue, 0m);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void EqualCloseWithMaximumVolumeDoesNotOverflowAnExtremeBalance(int direction)
    {
        AssertValues(new OnBalanceVolume().Calculate(
            [Flat(10m, decimal.MaxValue, 0), Flat(10m + direction, decimal.MaxValue, 1),
                Flat(10m + direction, decimal.MaxValue, 2)]),
            0m, direction * decimal.MaxValue, direction * decimal.MaxValue);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void FiniteDecimalPrecisionMayAbsorbSubUnitVolumeAtAnExtremeBalance(int direction)
    {
        AssertValues(new OnBalanceVolume().Calculate(
            [Flat(10m, 500m, 0), Flat(10m + direction, decimal.MaxValue, 1),
                Flat(10m + 2m * direction, MinimumPositive, 2)]),
            0m, direction * decimal.MaxValue, direction * decimal.MaxValue);
    }

    [Fact]
    public void RepeatedCallsAreExactlyDeterministicIncludingDecimalRepresentation()
    {
        var candles = Sequence(80);
        var indicator = new OnBalanceVolume();
        var expected = indicator.Calculate(candles);

        for (var repeat = 0; repeat < 5; repeat++)
        {
            AssertExact(expected, indicator.Calculate(candles));
        }
    }

    [Fact]
    public void EveryValidPrefixMatchesTheCorrespondingFullResult()
    {
        var candles = Sequence(40);
        var indicator = new OnBalanceVolume();
        var full = indicator.Calculate(candles);

        for (var count = 0; count <= candles.Length; count++)
        {
            var prefix = indicator.Calculate(candles.Take(count).ToArray());
            AssertExact(new IndicatorSeries(full.Points.Take(count).ToArray()), prefix);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void FutureMaximumVolumeShockDoesNotChangeAnyEarlierPoint(int direction)
    {
        var candles = new[] { Flat(10m, 500m, 0), Flat(11m, 100m, 1), Flat(10m, 100m, 2) };
        var indicator = new OnBalanceVolume();
        var prefix = indicator.Calculate(candles);
        var full = indicator.Calculate(candles.Append(Flat(10m + direction, decimal.MaxValue, 3)).ToArray());

        AssertExact(prefix, new IndicatorSeries(full.Points.Take(prefix.Count).ToArray()));
        Assert.Equal(direction * decimal.MaxValue, full.Points[^1].Value);
    }

    private static Candle[] OverflowSequence(int direction) =>
        [Flat(10m, 500m, 0), Flat(10m + direction, decimal.MaxValue, 1), Flat(10m + 2m * direction, 1m, 2)];

    private static Candle InvalidTail(string kind, int duplicateMinutes, int backwardMinutes) => kind switch
    {
        "instrument" => Create(10m, 1m, At(duplicateMinutes + 1), id: B),
        "interval" => Create(10m, 1m, At(duplicateMinutes + 1), interval: CandleInterval.FiveMinutes),
        "duplicate" => Flat(10m, 1m, duplicateMinutes),
        "backward" => Flat(10m, 1m, backwardMinutes),
        "null" => null!,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static void AssertValues(IndicatorSeries series, params decimal[] expected) =>
        Assert.Equal(expected, series.Points.Select(point => point.Value).ToArray());

    private static void AssertExact(IndicatorSeries expected, IndicatorSeries actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var index = 0; index < expected.Count; index++)
        {
            Assert.Equal(expected.Points[index].Time, actual.Points[index].Time);
            Assert.Equal(decimal.GetBits(expected.Points[index].Value), decimal.GetBits(actual.Points[index].Value));
        }
    }

    private static Candle[] Sequence(int count) => Enumerable.Range(0, count).Select(index =>
        Flat(10m + index % 7 / 10m, index % 5 == 0 ? 0m : index % 4 + 0.125m, index)).ToArray();

    private static decimal Parse(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);

    private static UtcInstant At(int minutes) => new(Start.Value.AddMinutes(minutes));

    private static Candle Flat(decimal close, decimal volume, int minutes) => Create(close, volume, At(minutes));

    private static Candle Create(decimal close, decimal volume, UtcInstant time,
        InstrumentId? id = null, CandleInterval? interval = null) =>
        new(id ?? A, interval ?? CandleInterval.OneMinute, time, new Price(close), new Price(close),
            new Price(close), new Price(close), new Quantity(volume));
}
