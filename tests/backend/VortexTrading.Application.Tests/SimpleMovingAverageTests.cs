using VortexTrading.Application.Indicators;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Application.Tests;

public sealed class SimpleMovingAverageTests
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
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new SimpleMovingAverage(period));

        Assert.Equal("period", exception.ParamName);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(int.MaxValue)]
    public void PreservesPositivePeriodAndStableInterfaceName(int period)
    {
        var sma = new SimpleMovingAverage(period);
        IIndicator indicator = sma;

        Assert.Equal(period, sma.Period);
        Assert.Equal("SMA", indicator.Name);
    }

    [Fact]
    public void RejectsNullInput()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new SimpleMovingAverage(3).Calculate(null!));

        Assert.Equal("candles", exception.ParamName);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(int.MaxValue)]
    public void EmptyInputReturnsAnEmptyNonNullSeries(int period)
    {
        var result = new SimpleMovingAverage(period).Calculate([]);

        Assert.NotNull(result);
        Assert.Equal(0, result.Count);
        Assert.Empty(result.Points);
    }

    [Fact]
    public void InsufficientWarmUpDoesNotProducePartialAverages()
    {
        var result = new SimpleMovingAverage(3).Calculate(Candles(10m, 20m));

        Assert.Equal(0, result.Count);
        Assert.Empty(result.Points);
    }

    [Fact]
    public void ExactlyOneWindowProducesOnePointAtTheLastCandleClose()
    {
        var candles = Candles(10m, 20m, 30m);
        var result = new SimpleMovingAverage(3).Calculate(candles);

        var point = Assert.Single(result.Points);
        Assert.Equal(1, result.Count);
        Assert.Equal(20m, point.Value);
        Assert.Equal(candles[2].CloseTime, point.Time);
    }

    [Fact]
    public void RollingWindowsProduceTwentyThirtyAndFortyInOrder()
    {
        var candles = Candles(10m, 20m, 30m, 40m, 50m);
        var result = new SimpleMovingAverage(3).Calculate(candles);

        Assert.Equal(3, result.Count);
        Assert.Equal(new[] { 20m, 30m, 40m }, result.Points.Select(point => point.Value).ToArray());
        for (var index = 0; index < result.Count; index++)
        {
            Assert.Equal(candles[index + 2].CloseTime, result.Points[index].Time);
        }
    }

    [Fact]
    public void PeriodOneReproducesEveryClosePriceAndCloseTime()
    {
        var candles = Candles(10.10m, 10.20m, 10.30m);
        var result = new SimpleMovingAverage(1).Calculate(candles);

        Assert.Equal(candles.Length, result.Count);
        for (var index = 0; index < candles.Length; index++)
        {
            Assert.Equal(candles[index].Close.Value, result.Points[index].Value);
            Assert.Equal(candles[index].CloseTime, result.Points[index].Time);
        }
    }

    [Fact]
    public void UsesCloseRatherThanOtherOhlcvFields()
    {
        var candles = new[] { 10m, 20m, 30m }.Select((close, index) => new Candle(
            A, CandleInterval.OneMinute, At(index), new Price(1m), new Price(100m),
            new Price(1m), new Price(close), new Quantity(1000m))).ToArray();

        var result = new SimpleMovingAverage(3).Calculate(candles);

        Assert.Equal(20m, Assert.Single(result.Points).Value);
    }

    [Fact]
    public void DecimalAverageIsExactWithoutFloatingPointToleranceOrPriceRounding()
    {
        var result = new SimpleMovingAverage(3).Calculate(Candles(10.10m, 10.20m, 10.30m, 10.60m));

        Assert.Equal(new[] { 10.20m, 10.366666666666666666666666667m },
            result.Points.Select(point => point.Value).ToArray());
    }

    [Fact]
    public void OutputTimesRepresentCloseRatherThanOpen()
    {
        var candles = new[]
        {
            Create(10m, Start, interval: CandleInterval.FiveMinutes),
            Create(20m, At(5), interval: CandleInterval.FiveMinutes),
            Create(30m, At(10), interval: CandleInterval.FiveMinutes)
        };
        var result = new SimpleMovingAverage(2).Calculate(candles);

        Assert.Equal(new[] { At(10), At(15) }, result.Points.Select(point => point.Time).ToArray());
        for (var index = 0; index < result.Count; index++)
        {
            Assert.Equal(candles[index + 1].CloseTime, result.Points[index].Time);
            Assert.NotEqual(candles[index + 1].OpenTime, result.Points[index].Time);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void RejectsMixedInstrumentsEvenBeforeWarmUp(int period)
    {
        var candles = new[] { Create(10m, Start), Create(20m, At(1), id: B) };

        var exception = Assert.Throws<ArgumentException>(() => new SimpleMovingAverage(period).Calculate(candles));

        Assert.Equal("candles", exception.ParamName);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void RejectsMixedIntervalsEvenBeforeWarmUp(int period)
    {
        var candles = new[] { Create(10m, Start), Create(20m, At(1), interval: CandleInterval.FiveMinutes) };

        var exception = Assert.Throws<ArgumentException>(() => new SimpleMovingAverage(period).Calculate(candles));

        Assert.Equal("candles", exception.ParamName);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void RejectsBackwardTimeWithoutSortingEvenBeforeWarmUp(int period)
    {
        var candles = new[] { Create(10m, At(1)), Create(20m, Start) };
        var original = candles.ToArray();

        var exception = Assert.Throws<ArgumentException>(() => new SimpleMovingAverage(period).Calculate(candles));

        Assert.Equal("candles", exception.ParamName);
        Assert.Same(original[0], candles[0]);
        Assert.Same(original[1], candles[1]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void RejectsDuplicateOpenTimesWithoutDeduplicationEvenBeforeWarmUp(int period)
    {
        var first = Create(10m, Start);
        var candles = new[] { first, first };

        var exception = Assert.Throws<ArgumentException>(() => new SimpleMovingAverage(period).Calculate(candles));

        Assert.Equal("candles", exception.ParamName);
        Assert.Equal(2, candles.Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void RejectsNullElementsThroughoutTheInputBeforeWarmUp(int nullIndex)
    {
        var candles = Candles(10m, 20m, 30m);
        candles[nullIndex] = null!;

        var exception = Assert.Throws<ArgumentException>(() => new SimpleMovingAverage(4).Calculate(candles));

        Assert.Equal("candles", exception.ParamName);
    }

    [Fact]
    public void InstrumentAndIntervalCoherenceUsesValueEquality()
    {
        var candles = new[]
        {
            Create(10m, Start),
            Create(20m, At(1), new InstrumentId(A.Value), new CandleInterval(TimeSpan.FromMinutes(1)))
        };
        Assert.NotSame(candles[0].InstrumentId, candles[1].InstrumentId);
        Assert.NotSame(candles[0].Interval, candles[1].Interval);

        var result = new SimpleMovingAverage(2).Calculate(candles);

        Assert.Equal(15m, Assert.Single(result.Points).Value);
    }

    [Fact]
    public void AllowsGapsWithoutPaddingOrResettingTheWindow()
    {
        var candles = new[] { Create(10m, Start), Create(20m, At(1)), Create(30m, At(5)) };

        var result = new SimpleMovingAverage(3).Calculate(candles);

        var point = Assert.Single(result.Points);
        Assert.Equal(20m, point.Value);
        Assert.Equal(At(6), point.Time);
    }

    [Fact]
    public void CalculationLeavesInputReferencesOhlcvAndTimestampsUnchanged()
    {
        var source = Candles(10.1000m, 10.2000m, 10.3000m, 10.4000m).ToList();
        var originals = source.ToArray();
        var times = source.Select(candle => (candle.OpenTime, candle.CloseTime)).ToArray();
        var values = source.Select(candle => new[]
        {
            candle.Open.Value, candle.High.Value, candle.Low.Value, candle.Close.Value, candle.Volume.Value
        }).ToArray();

        new SimpleMovingAverage(3).Calculate(source);

        Assert.Equal(originals.Length, source.Count);
        for (var index = 0; index < source.Count; index++)
        {
            Assert.Same(originals[index], source[index]);
            Assert.Same(times[index].OpenTime, source[index].OpenTime);
            Assert.Equal(times[index].CloseTime, source[index].CloseTime);
            decimal[] actual = [source[index].Open.Value, source[index].High.Value, source[index].Low.Value,
                source[index].Close.Value, source[index].Volume.Value];
            for (var field = 0; field < actual.Length; field++)
            {
                Assert.Equal(decimal.GetBits(values[index][field]), decimal.GetBits(actual[field]));
            }
        }
    }

    [Fact]
    public void ReturnedResultIsIndependentOfLaterInputCollectionChanges()
    {
        var source = Candles(10m, 20m, 30m, 40m).ToList();
        var result = new SimpleMovingAverage(3).Calculate(source);
        var points = result.Points.ToArray();

        source[0] = Create(1000m, Start);
        source.Clear();
        source.Add(Create(500m, Start));

        Assert.Equal(2, result.Count);
        Assert.Equal(new[] { 20m, 30m }, result.Points.Select(point => point.Value).ToArray());
        for (var index = 0; index < points.Length; index++) Assert.Same(points[index], result.Points[index]);
    }

    [Fact]
    public void RepeatedCalculationsHaveIdenticalValuesTimesAndOrder()
    {
        var sma = new SimpleMovingAverage(3);
        var candles = Candles(10.10m, 10.20m, 10.30m, 10.60m, 10.70m);

        var first = sma.Calculate(candles);
        var second = sma.Calculate(candles);

        Assert.NotSame(first, second);
        Assert.Equal(first.Count, second.Count);
        Assert.Equal(first.Points.ToArray(), second.Points.ToArray());
    }

    [Fact]
    public void ReusingAnIndicatorForAnotherBatchDoesNotRetainWindowState()
    {
        var sma = new SimpleMovingAverage(2);
        var first = sma.Calculate(Candles(10m, 20m, 30m));
        var other = sma.Calculate(Candles(100m, 200m));
        var restarted = sma.Calculate(Candles(10m, 20m, 30m));

        Assert.Equal(150m, Assert.Single(other.Points).Value);
        Assert.Equal(first.Points.ToArray(), restarted.Points.ToArray());
        Assert.Equal(new[] { 15m, 25m }, first.Points.Select(point => point.Value).ToArray());
    }

    [Fact]
    public void AppendingAFutureCandleDoesNotChangePreviouslyComputedPoints()
    {
        var sma = new SimpleMovingAverage(3);
        var candles = Candles(10m, 20m, 30m, 10000m);

        var prefix = sma.Calculate(candles.Take(3).ToArray());
        var complete = sma.Calculate(candles);

        Assert.Equal(20m, Assert.Single(prefix.Points).Value);
        Assert.Equal(prefix.Points[0], complete.Points[0]);
        Assert.Equal(candles[2].CloseTime, complete.Points[0].Time);
        Assert.Equal(2, complete.Count);
    }

    [Fact]
    public void PropagatesDecimalOverflowInInitialWindowWithoutChangingInput()
    {
        var candles = Candles(decimal.MaxValue, decimal.MaxValue);
        var originals = candles.ToArray();

        Assert.Throws<OverflowException>(() => new SimpleMovingAverage(2).Calculate(candles));

        Assert.Same(originals[0], candles[0]);
        Assert.Same(originals[1], candles[1]);
    }

    [Fact]
    public void PropagatesDecimalOverflowInALaterWindow()
    {
        var candles = Candles(1m, 30000000000000000000000000000m, decimal.MaxValue);

        Assert.Throws<OverflowException>(() => new SimpleMovingAverage(2).Calculate(candles));
    }

    [Fact]
    public void SlidingWindowDoesNotOverflowByTemporarilyIncludingAnExtraCandle()
    {
        const decimal close = 30000000000000000000000000000m;

        var result = new SimpleMovingAverage(2).Calculate(Candles(close, close, close));

        Assert.Equal(new[] { close, close }, result.Points.Select(point => point.Value).ToArray());
    }

    [Fact]
    public void PeriodOneSupportsConsecutiveMaximumClosePrices()
    {
        var result = new SimpleMovingAverage(1).Calculate(Candles(decimal.MaxValue, decimal.MaxValue));

        Assert.Equal(new[] { decimal.MaxValue, decimal.MaxValue }, result.Points.Select(point => point.Value).ToArray());
    }

    [Fact]
    public void PreservesMinimumPositiveDecimalPrices()
    {
        const decimal close = 0.0000000000000000000000000001m;

        var result = new SimpleMovingAverage(2).Calculate(Candles(close, close, close));

        Assert.Equal(new[] { close, close }, result.Points.Select(point => point.Value).ToArray());
    }

    [Fact]
    public void SupportsAdjacentTicksAndCloseAtMaximumRepresentableTime()
    {
        var interval = new CandleInterval(TimeSpan.FromTicks(1));
        var candles = new[]
        {
            Create(10m, new UtcInstant(DateTimeOffset.MaxValue.AddTicks(-2)), interval: interval),
            Create(20m, new UtcInstant(DateTimeOffset.MaxValue.AddTicks(-1)), interval: interval)
        };

        var result = new SimpleMovingAverage(2).Calculate(candles);

        var point = Assert.Single(result.Points);
        Assert.Equal(15m, point.Value);
        Assert.Equal(new UtcInstant(DateTimeOffset.MaxValue), point.Time);
    }

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
