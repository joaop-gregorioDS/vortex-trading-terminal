using System.Globalization;
using VortexTrading.Domain.Common.Time;

namespace VortexTrading.Domain.Tests;

public sealed class UtcInstantTests
{
    private static readonly DateTimeOffset ReferenceTime = new(2026, 10, 1, 22, 0, 0, TimeSpan.Zero);

    public static TheoryData<DateTimeOffset, DateTimeOffset> NormalizationCases => new()
    {
        { ReferenceTime, ReferenceTime },
        { new DateTimeOffset(2026, 10, 2, 1, 0, 0, TimeSpan.FromHours(3)), ReferenceTime },
        { new DateTimeOffset(2026, 10, 1, 19, 0, 0, TimeSpan.FromHours(-3)), ReferenceTime },
        { new DateTimeOffset(2026, 10, 2, 3, 30, 0, TimeSpan.FromMinutes(330)), ReferenceTime },
        { new DateTimeOffset(2026, 1, 1, 1, 0, 0, TimeSpan.FromHours(2)),
            new DateTimeOffset(2025, 12, 31, 23, 0, 0, TimeSpan.Zero) }
    };

    public static TheoryData<DateTimeOffset> PrecisionCases => new()
    {
        ReferenceTime.AddTicks(1),
        ReferenceTime.AddTicks(1234567),
        ReferenceTime.AddTicks(1234567).ToOffset(TimeSpan.FromMinutes(-210)),
        DateTimeOffset.MinValue,
        DateTimeOffset.MaxValue
    };

    [Theory]
    [MemberData(nameof(NormalizationCases))]
    public void NormalizesOffsetsToTheSameAbsoluteUtcInstant(DateTimeOffset input, DateTimeOffset expected)
    {
        var instant = new UtcInstant(input);

        Assert.Equal(TimeSpan.Zero, instant.Value.Offset);
        Assert.Equal(expected.Ticks, instant.Value.Ticks);
        Assert.Equal(input.UtcTicks, instant.Value.UtcTicks);
    }

    [Theory]
    [MemberData(nameof(PrecisionCases))]
    public void PreservesEverySupportedTickIncludingRepresentationLimits(DateTimeOffset input)
    {
        var instant = new UtcInstant(input);

        Assert.Equal(input.UtcTicks, instant.Value.Ticks);
        Assert.Equal(TimeSpan.Zero, instant.Value.Offset);
    }

    [Fact]
    public void EqualityAndHashingUseTheInstantRegardlessOfOriginalOffset()
    {
        var utc = new UtcInstant(ReferenceTime.AddTicks(1234567));
        var negativeOffset = new UtcInstant(utc.Value.ToOffset(TimeSpan.FromHours(-3)));
        var positiveOffset = new UtcInstant(utc.Value.ToOffset(TimeSpan.FromMinutes(330)));

        Assert.Equal(utc, negativeOffset);
        Assert.Equal(utc, positiveOffset);
        Assert.True(utc == negativeOffset);
        Assert.Equal(utc.GetHashCode(), negativeOffset.GetHashCode());
        Assert.Equal(utc.GetHashCode(), positiveOffset.GetHashCode());
        Assert.Single(new HashSet<UtcInstant> { utc, negativeOffset, positiveOffset });
        Assert.NotEqual(utc, new UtcInstant(utc.Value.AddTicks(1)));
    }

    [Fact]
    public void ComparisonOrdersInstantsAtTickPrecisionAcrossOffsets()
    {
        var earlier = new UtcInstant(ReferenceTime.ToOffset(TimeSpan.FromHours(3)));
        var later = new UtcInstant(ReferenceTime.AddTicks(1).ToOffset(TimeSpan.FromHours(-3)));
        var equivalent = new UtcInstant(ReferenceTime.ToOffset(TimeSpan.FromMinutes(-210)));

        Assert.True(earlier.CompareTo(later) < 0);
        Assert.True(later.CompareTo(earlier) > 0);
        Assert.Equal(0, earlier.CompareTo(equivalent));
        Assert.Equal(new[] { earlier, later }, new[] { later, earlier }.OrderBy(value => value));
    }

    [Fact]
    public void ComparisonTreatsMissingReferenceAsLessThanAnExistingInstant()
    {
        Assert.True(new UtcInstant(ReferenceTime).CompareTo(null) > 0);
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("pt-BR")]
    [InlineData("ar-SA")]
    public void StringRepresentationIsCanonicalUtcAndCultureInvariant(string cultureName)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            var input = new DateTimeOffset(2026, 10, 1, 19, 0, 0, TimeSpan.FromHours(-3)).AddTicks(1234567);
            var instant = new UtcInstant(input);

            Assert.Equal("2026-10-01T22:00:00.1234567+00:00", instant.ToString());
            var roundTrip = DateTimeOffset.ParseExact(instant.ToString(), "O", CultureInfo.InvariantCulture);
            Assert.Equal(instant.Value.Ticks, roundTrip.Ticks);
            Assert.Equal(TimeSpan.Zero, roundTrip.Offset);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }
}
