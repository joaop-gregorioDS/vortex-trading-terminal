using VortexTrading.Application.Indicators;
using VortexTrading.Domain.Common.Time;

namespace VortexTrading.Application.Tests;

public sealed class IndicatorSeriesTests
{
    private static readonly UtcInstant Time = new(new DateTimeOffset(2026, 10, 3, 10, 1, 0, TimeSpan.Zero));

    [Fact]
    public void RejectsNullCollection()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new IndicatorSeries(null));

        Assert.Equal("points", exception.ParamName);
    }

    [Fact]
    public void RejectsNullPoint()
    {
        var exception = Assert.Throws<ArgumentException>(() => new IndicatorSeries([new IndicatorPoint(Time, 1m), null!]));

        Assert.Equal("points", exception.ParamName);
    }

    [Fact]
    public void EmptySeriesIsValid()
    {
        var series = new IndicatorSeries([]);

        Assert.Equal(0, series.Count);
        Assert.Empty(series.Points);
    }

    [Fact]
    public void PreservesSuppliedOrderWithoutSortingByTimeOrValue()
    {
        var later = new IndicatorPoint(new UtcInstant(Time.Value.AddMinutes(1)), -5m);
        var earlier = new IndicatorPoint(Time, 20m);
        var series = new IndicatorSeries([later, earlier]);

        Assert.Equal(2, series.Count);
        Assert.Same(later, series.Points[0]);
        Assert.Same(earlier, series.Points[1]);
    }

    [Fact]
    public void PreservesRepeatedReferencesEqualPointsAndDifferentValuesAtOneTime()
    {
        var first = new IndicatorPoint(Time, 1m);
        var equal = new IndicatorPoint(new UtcInstant(Time.Value), 1m);
        var different = new IndicatorPoint(Time, 2m);
        var expected = new[] { first, equal, different, first };
        var series = new IndicatorSeries(expected);

        Assert.Equal(4, series.Count);
        for (var index = 0; index < expected.Length; index++) Assert.Same(expected[index], series.Points[index]);
    }

    [Fact]
    public void SnapshotIsIndependentOfLaterSourceListChanges()
    {
        var first = new IndicatorPoint(Time, 1m);
        var second = new IndicatorPoint(Time, 2m);
        var source = new List<IndicatorPoint> { first, second };
        var series = new IndicatorSeries(source);

        source[0] = second;
        source.Clear();
        source.Add(new IndicatorPoint(Time, -10m));

        Assert.Equal(2, series.Count);
        Assert.Same(first, series.Points[0]);
        Assert.Same(second, series.Points[1]);
    }

    [Fact]
    public void SnapshotIsIndependentOfSourceArrayReplacement()
    {
        var original = new IndicatorPoint(Time, 1m);
        var source = new[] { original };
        var series = new IndicatorSeries(source);

        source[0] = new IndicatorPoint(Time, 100m);

        Assert.Single(series.Points);
        Assert.Same(original, series.Points[0]);
    }

    [Fact]
    public void ExposedCollectionRejectsReplacementAdditionAndRemoval()
    {
        var point = new IndicatorPoint(Time, 1m);
        var series = new IndicatorSeries([point]);
        var list = Assert.IsAssignableFrom<IList<IndicatorPoint>>(series.Points);

        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => list[0] = new IndicatorPoint(Time, 2m));
        Assert.Throws<NotSupportedException>(() => list.Add(point));
        Assert.Throws<NotSupportedException>(() => list.RemoveAt(0));
        Assert.Throws<NotSupportedException>(() => list.Clear());
        Assert.Equal(1, series.Count);
        Assert.Same(point, series.Points[0]);
    }
}
