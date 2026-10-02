using System.Globalization;
using VortexTrading.Domain.Common;
using VortexTrading.Domain.Markets;

namespace VortexTrading.Domain.Tests;

public sealed class CandleIntervalTests
{
    public static TheoryData<TimeSpan> PositiveDurations => new()
    {
        TimeSpan.FromTicks(1),
        TimeSpan.FromSeconds(17).Add(TimeSpan.FromTicks(3)),
        TimeSpan.FromMinutes(7),
        TimeSpan.FromHours(2),
        TimeSpan.MaxValue
    };

    public static TheoryData<TimeSpan> InvalidDurations => new()
    {
        TimeSpan.Zero,
        TimeSpan.FromTicks(-1),
        TimeSpan.FromMinutes(-1),
        TimeSpan.MinValue
    };

    public static TheoryData<CandleInterval, TimeSpan> StandardIntervals => new()
    {
        { CandleInterval.OneMinute, TimeSpan.FromMinutes(1) },
        { CandleInterval.FiveMinutes, TimeSpan.FromMinutes(5) },
        { CandleInterval.FifteenMinutes, TimeSpan.FromMinutes(15) },
        { CandleInterval.ThirtyMinutes, TimeSpan.FromMinutes(30) },
        { CandleInterval.OneHour, TimeSpan.FromHours(1) },
        { CandleInterval.FourHours, TimeSpan.FromHours(4) },
        { CandleInterval.OneDay, TimeSpan.FromHours(24) }
    };

    [Theory]
    [MemberData(nameof(PositiveDurations))]
    public void AcceptsCustomPositiveDurationsWithoutTruncatingTicks(TimeSpan duration)
    {
        Assert.Equal(duration.Ticks, new CandleInterval(duration).Duration.Ticks);
    }

    [Theory]
    [MemberData(nameof(InvalidDurations))]
    public void RejectsZeroAndNegativeDurations(TimeSpan duration)
    {
        Assert.Throws<DomainException>(() => new CandleInterval(duration));
    }

    [Theory]
    [MemberData(nameof(StandardIntervals))]
    public void NamedIntervalsHaveTheDeclaredExactDuration(CandleInterval interval, TimeSpan expected)
    {
        Assert.Equal(expected, interval.Duration);
        Assert.Equal(new CandleInterval(expected), interval);
    }

    [Fact]
    public void EqualityAndHashingUseDurationAtTickPrecision()
    {
        var first = new CandleInterval(TimeSpan.FromTicks(123456789));
        var second = new CandleInterval(TimeSpan.FromTicks(123456789));

        Assert.NotSame(first, second);
        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.Single(new HashSet<CandleInterval> { first, second });
        Assert.NotEqual(first, new CandleInterval(TimeSpan.FromTicks(123456790)));
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("pt-BR")]
    [InlineData("ar-SA")]
    public void StringRepresentationPreservesDurationAndIsCultureInvariant(string cultureName)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            var duration = TimeSpan.FromDays(1).Add(TimeSpan.FromTicks(1234567));
            var interval = new CandleInterval(duration);

            Assert.Equal("1.00:00:00.1234567", interval.ToString());
            Assert.Equal(duration.Ticks, TimeSpan.ParseExact(interval.ToString(), "c", CultureInfo.InvariantCulture).Ticks);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }
}
