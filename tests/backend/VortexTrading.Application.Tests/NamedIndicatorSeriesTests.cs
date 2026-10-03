using VortexTrading.Application.Indicators;
using VortexTrading.Domain.Common.Time;

namespace VortexTrading.Application.Tests;

public sealed class NamedIndicatorSeriesTests
{
    private static readonly UtcInstant Time = new(new DateTimeOffset(2026, 10, 3, 10, 1, 0, TimeSpan.Zero));

    [Theory]
    [InlineData("Primary", "Primary")]
    [InlineData("  Secondary  ", "Secondary")]
    [InlineData("\tTertiary\r\n", "Tertiary")]
    [InlineData("\u00a0Primary\u2003", "Primary")]
    [InlineData("pRiMaRy", "pRiMaRy")]
    [InlineData(" Primary Series ", "Primary Series")]
    public void PreservesNameCaseAndInteriorSpacesWhileTrimmingTheEdges(string input, string expected)
    {
        var series = new IndicatorSeries([]);

        var named = new NamedIndicatorSeries(input, series);

        Assert.Equal(expected, named.Name);
        Assert.Same(series, named.Series);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    [InlineData("\u00a0")]
    [InlineData("\u2003")]
    public void RejectsNullEmptyOrWhitespaceNameWithArgumentException(string? name)
    {
        var exception = Assert.Throws<ArgumentException>(() => new NamedIndicatorSeries(name, new IndicatorSeries([])));

        Assert.Equal("name", exception.ParamName);
    }

    [Fact]
    public void RejectsNullSeries()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new NamedIndicatorSeries("Primary", null));

        Assert.Equal("series", exception.ParamName);
    }

    [Fact]
    public void EmptySeriesIsValidAndItsExactInstanceIsPreserved()
    {
        var series = new IndicatorSeries([]);

        var named = new NamedIndicatorSeries("Primary", series);

        Assert.Same(series, named.Series);
        Assert.Empty(named.Series.Points);
    }

    [Fact]
    public void WrappingDoesNotCloneRewriteOrSortTheExistingSeries()
    {
        var later = new IndicatorPoint(new UtcInstant(Time.Value.AddMinutes(1)), -10m);
        var earlier = new IndicatorPoint(Time, 0m);
        var series = new IndicatorSeries([later, earlier, later]);

        var named = new NamedIndicatorSeries("Primary", series);

        Assert.Same(series, named.Series);
        Assert.Equal(3, named.Series.Count);
        Assert.Same(later, named.Series.Points[0]);
        Assert.Same(earlier, named.Series.Points[1]);
        Assert.Same(later, named.Series.Points[2]);
    }

    [Fact]
    public void EqualNormalizedNamesAndTheSameSeriesHaveEqualPairValuesAndHashes()
    {
        var series = new IndicatorSeries([new IndicatorPoint(Time, 1m)]);
        var first = new NamedIndicatorSeries(" Primary ", series);
        var second = new NamedIndicatorSeries("Primary", series);

        Assert.NotSame(first, second);
        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void PairEqualityDistinguishesNameCase()
    {
        var series = new IndicatorSeries([]);

        var upper = new NamedIndicatorSeries("Primary", series);
        var lower = new NamedIndicatorSeries("primary", series);

        Assert.NotEqual(upper, lower);
    }

    [Fact]
    public void PairEqualityDistinguishesOrdinallyDifferentUnicodeNames()
    {
        var series = new IndicatorSeries([]);

        var composed = new NamedIndicatorSeries("Primary\u00e9", series);
        var decomposed = new NamedIndicatorSeries("Primarye\u0301", series);

        Assert.NotEqual(composed, decomposed);
    }

    [Fact]
    public void EqualPointContentsInDistinctSeriesRetainExistingReferenceEquality()
    {
        var firstSeries = new IndicatorSeries([new IndicatorPoint(Time, 1m)]);
        var secondSeries = new IndicatorSeries([new IndicatorPoint(Time, 1m)]);
        Assert.Equal(firstSeries.Points.ToArray(), secondSeries.Points.ToArray());

        var first = new NamedIndicatorSeries("Primary", firstSeries);
        var second = new NamedIndicatorSeries("Primary", secondSeries);

        Assert.NotEqual(first, second);
        Assert.Same(firstSeries, first.Series);
        Assert.Same(secondSeries, second.Series);
    }
}
