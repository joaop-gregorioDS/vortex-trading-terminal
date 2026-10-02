using VortexTrading.Domain.Common;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Domain.Tests;

public sealed class CandleTests
{
    private static readonly InstrumentId Id = InstrumentId.From(new Guid("0d319028-b047-4e71-bd7d-9f6983dd17a4"));
    private static readonly UtcInstant OpenTime = new(new DateTimeOffset(2026, 10, 1, 22, 0, 0, TimeSpan.Zero).AddTicks(1234567));

    public static TheoryData<decimal, decimal, decimal, decimal> InvalidOhlcCases => new()
    {
        { 10m, 9m, 8m, 8.5m },
        { 8.5m, 9m, 8m, 10m },
        { 8m, 10m, 9m, 9.5m },
        { 9.5m, 10m, 9m, 8m },
        { 10m, 9m, 11m, 10m }
    };

    public static TheoryData<decimal, decimal, decimal, decimal> ValidOhlcCases => new()
    {
        { 10m, 12m, 9m, 11m },
        { 11m, 12m, 9m, 10m },
        { 10m, 10m, 10m, 10m }
    };

    public static TheoryData<decimal> Volumes => new()
    {
        0m,
        0.12500m,
        0.0000000000000000000000000001m
    };

    public static TheoryData<TimeSpan> Durations => new()
    {
        TimeSpan.FromTicks(1),
        TimeSpan.FromSeconds(17).Add(TimeSpan.FromTicks(3)),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromHours(24)
    };

    public static TheoryData<DateTimeOffset, TimeSpan> OverflowCases => new()
    {
        { DateTimeOffset.MaxValue, TimeSpan.FromTicks(1) },
        { DateTimeOffset.MinValue, TimeSpan.MaxValue }
    };

    public static TheoryData<decimal> PreciseValues => new()
    {
        0.0000000000000000000000000001m,
        7.1234567890123456789012345678m,
        decimal.MaxValue
    };

    [Fact]
    public void ConstructionPreservesAllRequiredComponents()
    {
        var interval = CandleInterval.OneMinute;
        var open = new Price(10.0500m);
        var high = new Price(10.0900m);
        var low = new Price(10.0100m);
        var close = new Price(10.0700m);
        var volume = new Quantity(0.12500m);

        var candle = new Candle(Id, interval, OpenTime, open, high, low, close, volume);

        Assert.Equal(Id, candle.InstrumentId);
        Assert.Same(interval, candle.Interval);
        Assert.Same(OpenTime, candle.OpenTime);
        Assert.Same(open, candle.Open);
        Assert.Same(high, candle.High);
        Assert.Same(low, candle.Low);
        Assert.Same(close, candle.Close);
        Assert.Same(volume, candle.Volume);
        Assert.Equal(decimal.GetBits(10.0500m), decimal.GetBits(candle.Open.Value));
        Assert.Equal(decimal.GetBits(10.0900m), decimal.GetBits(candle.High.Value));
        Assert.Equal(decimal.GetBits(10.0100m), decimal.GetBits(candle.Low.Value));
        Assert.Equal(decimal.GetBits(10.0700m), decimal.GetBits(candle.Close.Value));
        Assert.Equal(decimal.GetBits(0.12500m), decimal.GetBits(candle.Volume.Value));
    }

    [Theory]
    [InlineData("InstrumentId")]
    [InlineData("Interval")]
    [InlineData("OpenTime")]
    [InlineData("Open")]
    [InlineData("High")]
    [InlineData("Low")]
    [InlineData("Close")]
    [InlineData("Volume")]
    public void RejectsEachMissingRequiredComponent(string component)
    {
        Assert.Throws<DomainException>(() => new Candle(
            component == "InstrumentId" ? null : Id,
            component == "Interval" ? null : CandleInterval.OneMinute,
            component == "OpenTime" ? null : OpenTime,
            component == "Open" ? null : new Price(10m),
            component == "High" ? null : new Price(12m),
            component == "Low" ? null : new Price(9m),
            component == "Close" ? null : new Price(11m),
            component == "Volume" ? null : new Quantity(1m)));
    }

    [Theory]
    [InlineData("Open")]
    [InlineData("High")]
    [InlineData("Low")]
    [InlineData("Close")]
    public void RejectsZeroInEveryOhlcPrice(string component)
    {
        Assert.Throws<DomainException>(() => CreateCandle(
            open: component == "Open" ? 0m : 10m,
            high: component == "High" ? 0m : 12m,
            low: component == "Low" ? 0m : 9m,
            close: component == "Close" ? 0m : 11m));
    }

    [Theory]
    [MemberData(nameof(InvalidOhlcCases))]
    public void RejectsImpossibleHighLowRanges(decimal open, decimal high, decimal low, decimal close)
    {
        Assert.Throws<DomainException>(() => CreateCandle(open, high, low, close));
    }

    [Theory]
    [MemberData(nameof(ValidOhlcCases))]
    public void AcceptsRisingFallingAndFlatCandles(decimal open, decimal high, decimal low, decimal close)
    {
        var candle = CreateCandle(open, high, low, close);

        Assert.Equal(open, candle.Open.Value);
        Assert.Equal(high, candle.High.Value);
        Assert.Equal(low, candle.Low.Value);
        Assert.Equal(close, candle.Close.Value);
    }

    [Theory]
    [MemberData(nameof(Volumes))]
    public void AcceptsZeroAndFractionalVolumeWithoutChangingScale(decimal volume)
    {
        Assert.Equal(decimal.GetBits(volume), decimal.GetBits(CreateCandle(volume: volume).Volume.Value));
    }

    [Theory]
    [MemberData(nameof(Durations))]
    public void DerivesCloseTimeExactlyFromOpenTimeAndDuration(TimeSpan duration)
    {
        var candle = new Candle(Id, new CandleInterval(duration), OpenTime,
            new Price(10m), new Price(12m), new Price(9m), new Price(11m), new Quantity(1m));

        Assert.Equal(OpenTime.Value.UtcTicks + duration.Ticks, candle.CloseTime.Value.UtcTicks);
        Assert.Equal(TimeSpan.Zero, candle.CloseTime.Value.Offset);
        Assert.Equal(duration.Ticks, candle.CloseTime.Value.UtcTicks - candle.OpenTime.Value.UtcTicks);
    }

    [Theory]
    [MemberData(nameof(OverflowCases))]
    public void RejectsUnrepresentableCloseTimeWithDomainException(DateTimeOffset openTime, TimeSpan duration)
    {
        Assert.Throws<DomainException>(() => new Candle(Id, new CandleInterval(duration), new UtcInstant(openTime),
            new Price(10m), new Price(12m), new Price(9m), new Price(11m), new Quantity(1m)));
    }

    [Fact]
    public void AcceptsCloseTimeExactlyAtTheMaximumRepresentableInstant()
    {
        var candle = new Candle(Id, new CandleInterval(TimeSpan.FromTicks(1)),
            new UtcInstant(DateTimeOffset.MaxValue.AddTicks(-1)),
            new Price(10m), new Price(10m), new Price(10m), new Price(10m), new Quantity(0m));

        Assert.Equal(new UtcInstant(DateTimeOffset.MaxValue), candle.CloseTime);
    }

    [Fact]
    public void StructuralEqualityAndHashingUseEquivalentComponentValues()
    {
        var first = CreateCandle();
        var second = new Candle(InstrumentId.From(Id.Value), new CandleInterval(TimeSpan.FromSeconds(60)),
            new UtcInstant(OpenTime.Value.ToOffset(TimeSpan.FromHours(-3))),
            new Price(10.00m), new Price(12.00m), new Price(9.00m), new Price(11.00m), new Quantity(1.00m));

        Assert.NotSame(first, second);
        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.Single(new HashSet<Candle> { first, second });
    }

    [Theory]
    [InlineData("InstrumentId")]
    [InlineData("Interval")]
    [InlineData("OpenTime")]
    [InlineData("Open")]
    [InlineData("High")]
    [InlineData("Low")]
    [InlineData("Close")]
    [InlineData("Volume")]
    public void DifferentSnapshotComponentsMakeCandlesUnequal(string component)
    {
        var first = CreateCandle();
        var second = new Candle(
            component == "InstrumentId" ? InstrumentId.From(new Guid("12067718-caa9-4b98-996d-b73234605d9f")) : Id,
            component == "Interval" ? CandleInterval.FiveMinutes : CandleInterval.OneMinute,
            component == "OpenTime" ? new UtcInstant(OpenTime.Value.AddTicks(1)) : OpenTime,
            new Price(component == "Open" ? 10.5m : 10m),
            new Price(component == "High" ? 13m : 12m),
            new Price(component == "Low" ? 8m : 9m),
            new Price(component == "Close" ? 10.5m : 11m),
            new Quantity(component == "Volume" ? 2m : 1m));

        Assert.NotEqual(first, second);
        Assert.True(first != second);
    }

    [Theory]
    [MemberData(nameof(PreciseValues))]
    public void PreservesHighPrecisionAndExtremeDecimals(decimal value)
    {
        var candle = CreateCandle(value, value, value, value, value);
        var bits = decimal.GetBits(value);

        Assert.Equal(bits, decimal.GetBits(candle.Open.Value));
        Assert.Equal(bits, decimal.GetBits(candle.High.Value));
        Assert.Equal(bits, decimal.GetBits(candle.Low.Value));
        Assert.Equal(bits, decimal.GetBits(candle.Close.Value));
        Assert.Equal(bits, decimal.GetBits(candle.Volume.Value));
    }

    [Fact]
    public void SnapshotConstructionDoesNotValidateInstrumentIncrements()
    {
        var instrument = new Instrument(Id, new Symbol("TEST"), new Venue("CUSTOM"), AssetClass.Equity,
            new Currency("USD"), new Price(0.05m), new Quantity(100m));
        var candle = CreateCandle(10.07m, 10.07m, 10.07m, 10.07m, 0.001m);

        Assert.Equal(instrument.Id, candle.InstrumentId);
        Assert.False(instrument.IsValidPrice(candle.Open));
        Assert.False(instrument.IsValidPrice(candle.High));
        Assert.False(instrument.IsValidPrice(candle.Low));
        Assert.False(instrument.IsValidPrice(candle.Close));
        Assert.False(instrument.IsValidQuantity(candle.Volume));
    }

    private static Candle CreateCandle(decimal open = 10m, decimal high = 12m,
        decimal low = 9m, decimal close = 11m, decimal volume = 1m)
        => new(InstrumentId.From(Id.Value), CandleInterval.OneMinute, OpenTime,
            new Price(open), new Price(high), new Price(low), new Price(close), new Quantity(volume));
}
