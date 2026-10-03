using VortexTrading.Application.Indicators;
using VortexTrading.Domain.Common.Time;

namespace VortexTrading.Application.Tests;

public sealed class IndicatorPointTests
{
    private static readonly UtcInstant Time = new(new DateTimeOffset(2026, 10, 3, 10, 1, 0, TimeSpan.Zero));

    [Fact]
    public void RejectsNullTime()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new IndicatorPoint(null, 1m));

        Assert.Equal("time", exception.ParamName);
    }

    public static TheoryData<decimal> Values => new() { decimal.MinValue, -10.1000m, 0m, 10.1000m, decimal.MaxValue };

    [Theory]
    [MemberData(nameof(Values))]
    public void AcceptsNegativeZeroAndPositiveValuesWithoutChangingTheirRepresentation(decimal value)
    {
        var point = new IndicatorPoint(Time, value);

        Assert.Same(Time, point.Time);
        Assert.Equal(decimal.GetBits(value), decimal.GetBits(point.Value));
    }

    [Fact]
    public void EqualityUsesTimeAndDecimalValue()
    {
        var first = new IndicatorPoint(Time, 10.10m);
        var equal = new IndicatorPoint(new UtcInstant(Time.Value), 10.1000m);

        Assert.NotSame(first, equal);
        Assert.Equal(first, equal);
        Assert.Equal(first.GetHashCode(), equal.GetHashCode());
        Assert.NotEqual(first, new IndicatorPoint(Time, 10.11m));
        Assert.NotEqual(first, new IndicatorPoint(new UtcInstant(Time.Value.AddTicks(1)), 10.10m));
    }
}
