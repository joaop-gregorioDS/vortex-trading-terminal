using VortexTrading.Application.Indicators;
using VortexTrading.Domain.Common.Time;

namespace VortexTrading.Application.Tests;

public sealed class MultiSeriesIndicatorResultTests
{
    private static readonly UtcInstant Time = new(new DateTimeOffset(2026, 10, 3, 10, 1, 0, TimeSpan.Zero));

    [Fact]
    public void EmptyResultIsValidWithoutInventingNamedSeries()
    {
        var result = new MultiSeriesIndicatorResult([]);

        Assert.Equal(0, result.Count);
        Assert.Empty(result.Series);
    }

    [Fact]
    public void RejectsNullCollection()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new MultiSeriesIndicatorResult(null));

        Assert.Equal("series", exception.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void RejectsNullElementsAtEveryPosition(int nullIndex)
    {
        var source = Entries();
        source[nullIndex] = null!;

        var exception = Assert.Throws<ArgumentException>(() => new MultiSeriesIndicatorResult(source));

        Assert.Equal("series", exception.ParamName);
    }

    [Fact]
    public void PreservesSuppliedOrderNamedReferencesAndSeriesReferences()
    {
        var source = Entries();

        var result = new MultiSeriesIndicatorResult(source);

        Assert.Equal(3, result.Count);
        Assert.Equal(new[] { "Secondary", "Primary", "Tertiary" }, result.Series.Select(item => item.Name).ToArray());
        for (var index = 0; index < source.Length; index++)
        {
            Assert.Same(source[index], result.Series[index]);
            Assert.Same(source[index].Series, result.Series[index].Series);
        }
    }

    [Fact]
    public void RejectsDuplicateNamesEvenWhenSeriesInstancesDiffer()
    {
        var source = new[] { Named("Primary", 1m), Named("Primary", 2m) };

        var exception = Assert.Throws<ArgumentException>(() => new MultiSeriesIndicatorResult(source));

        Assert.Equal("series", exception.ParamName);
    }

    [Fact]
    public void RejectsARepeatedNamedSeriesReference()
    {
        var item = Named("Primary", 1m);

        var exception = Assert.Throws<ArgumentException>(() => new MultiSeriesIndicatorResult([item, item]));

        Assert.Equal("series", exception.ParamName);
    }

    [Fact]
    public void RejectsNamesThatBecomeDuplicatesAfterTrimming()
    {
        var source = new[] { Named(" Primary ", 1m), Named("Primary", 2m) };

        var exception = Assert.Throws<ArgumentException>(() => new MultiSeriesIndicatorResult(source));

        Assert.Equal("series", exception.ParamName);
    }

    [Fact]
    public void CaseSensitiveNamesAreDistinctAndLookupSelectsEachExactSeries()
    {
        var upper = Named("Primary", 1m);
        var lower = Named("primary", 2m);

        var result = new MultiSeriesIndicatorResult([upper, lower]);

        Assert.Equal(2, result.Count);
        Assert.True(result.TryGetSeries("Primary", out var upperFound));
        Assert.True(result.TryGetSeries("primary", out var lowerFound));
        Assert.Same(upper.Series, upperFound);
        Assert.Same(lower.Series, lowerFound);
    }

    [Fact]
    public void OrdinalUniquenessDoesNotMergeUnicodeCanonicalEquivalents()
    {
        var composed = Named("Primary\u00e9", 1m);
        var decomposed = Named("Primarye\u0301", 2m);

        var result = new MultiSeriesIndicatorResult([composed, decomposed]);

        Assert.Equal(2, result.Count);
        Assert.True(result.TryGetSeries(composed.Name, out var first));
        Assert.True(result.TryGetSeries(decomposed.Name, out var second));
        Assert.Same(composed.Series, first);
        Assert.Same(decomposed.Series, second);
    }

    [Theory]
    [InlineData("add")]
    [InlineData("remove")]
    [InlineData("clear")]
    [InlineData("reorder")]
    [InlineData("replace")]
    public void SourceListMutationsDoNotChangeOrderCountReferencesOrLookup(string operation)
    {
        var originals = Entries();
        var source = originals.ToList();
        var result = new MultiSeriesIndicatorResult(source);

        switch (operation)
        {
            case "add": source.Add(Named("Additional", 4m)); break;
            case "remove": source.RemoveAt(0); break;
            case "clear": source.Clear(); break;
            case "reorder": source.Reverse(); break;
            case "replace": source[0] = Named("Secondary", 999m); break;
            default: throw new ArgumentException("Unknown source mutation.", nameof(operation));
        }

        Assert.Equal(3, result.Count);
        for (var index = 0; index < originals.Length; index++)
        {
            Assert.Same(originals[index], result.Series[index]);
            Assert.True(result.TryGetSeries(originals[index].Name, out var found));
            Assert.Same(originals[index].Series, found);
        }
        Assert.False(result.TryGetSeries("Additional", out var missing));
        Assert.Null(missing);
    }

    [Fact]
    public void SourceArrayReplacementDoesNotChangeTheSnapshot()
    {
        var source = Entries();
        var original = source[0];
        var result = new MultiSeriesIndicatorResult(source);

        source[0] = Named("Additional", 999m);

        Assert.Same(original, result.Series[0]);
        Assert.True(result.TryGetSeries("Secondary", out var found));
        Assert.Same(original.Series, found);
        Assert.False(result.TryGetSeries("Additional", out var missing));
        Assert.Null(missing);
    }

    [Fact]
    public void AOneShotEnumerableIsMaterializedOnceAndIsNotRevisitedForLookup()
    {
        var originals = Entries();
        var enumerations = 0;
        IEnumerable<NamedIndicatorSeries> Source()
        {
            if (++enumerations > 1) throw new InvalidOperationException("Source can only be consumed once.");
            foreach (var item in originals) yield return item;
        }

        var result = new MultiSeriesIndicatorResult(Source());

        Assert.Equal(1, enumerations);
        Assert.Equal(3, result.Count);
        Assert.True(result.TryGetSeries("Secondary", out var found));
        Assert.Same(originals[0].Series, found);
        Assert.False(result.TryGetSeries("Missing", out _));
        Assert.Equal(1, enumerations);
    }

    [Fact]
    public void ExposedCollectionRejectsReplacementAdditionRemovalAndClear()
    {
        var originals = Entries();
        var result = new MultiSeriesIndicatorResult(originals);
        var list = Assert.IsAssignableFrom<IList<NamedIndicatorSeries>>(result.Series);

        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => list[0] = Named("Additional", 4m));
        Assert.Throws<NotSupportedException>(() => list.Add(Named("Additional", 4m)));
        Assert.Throws<NotSupportedException>(() => list.RemoveAt(0));
        Assert.Throws<NotSupportedException>(() => list.Clear());
        Assert.Equal(3, result.Count);
        Assert.Same(originals[0], result.Series[0]);
    }

    [Fact]
    public void NonGenericCollectionCastsCannotModifyTheSnapshotEither()
    {
        var original = Named("Primary", 1m);
        var result = new MultiSeriesIndicatorResult([original]);
        var list = Assert.IsAssignableFrom<System.Collections.IList>(result.Series);

        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => list[0] = Named("Secondary", 2m));
        Assert.Throws<NotSupportedException>(() => list.Add(Named("Secondary", 2m)));
        Assert.Throws<NotSupportedException>(() => list.Clear());
        Assert.Same(original, Assert.Single(result.Series));
    }

    [Fact]
    public void DifferentLengthsIncludingAnEmptySeriesAreAcceptedWithoutPadding()
    {
        var primary = Named("Primary", 1m, 2m, 3m);
        var secondary = Named("Secondary", 4m);
        var tertiary = Named("Tertiary");

        var result = new MultiSeriesIndicatorResult([primary, secondary, tertiary]);

        Assert.Equal(new[] { 3, 1, 0 }, result.Series.Select(item => item.Series.Count).ToArray());
        Assert.Same(primary.Series, result.Series[0].Series);
        Assert.Same(secondary.Series, result.Series[1].Series);
        Assert.Same(tertiary.Series, result.Series[2].Series);
    }

    [Fact]
    public void DifferentStartEndAndInteriorTimestampsAreAcceptedWithoutAlignment()
    {
        var firstPoints = new[] { Point(0, 1m), Point(5, 2m), Point(10, 3m) };
        var secondPoints = new[] { Point(2, 4m), Point(6, 5m), Point(20, 6m) };
        var first = new IndicatorSeries(firstPoints);
        var second = new IndicatorSeries(secondPoints);

        var result = new MultiSeriesIndicatorResult([
            new NamedIndicatorSeries("Primary", first), new NamedIndicatorSeries("Secondary", second)]);

        Assert.Same(first, result.Series[0].Series);
        Assert.Same(second, result.Series[1].Series);
        for (var index = 0; index < 3; index++)
        {
            Assert.Same(firstPoints[index], result.Series[0].Series.Points[index]);
            Assert.Same(secondPoints[index], result.Series[1].Series.Points[index]);
        }
    }

    [Fact]
    public void UnorderedDuplicateAndSameTimestampPointsKeepExistingSeriesSemantics()
    {
        var later = Point(5, -1m);
        var earlier = Point(0, 0m);
        var equal = Point(0, 0m);
        var different = Point(0, 2m);
        var originals = new[] { later, earlier, equal, different, earlier };
        var series = new IndicatorSeries(originals);

        var result = new MultiSeriesIndicatorResult([new NamedIndicatorSeries("Primary", series), Named("Secondary")]);

        Assert.Same(series, result.Series[0].Series);
        Assert.Equal(5, result.Series[0].Series.Count);
        for (var index = 0; index < originals.Length; index++)
        {
            Assert.Same(originals[index], result.Series[0].Series.Points[index]);
        }
    }

    [Fact]
    public void DifferentNamesMayReferenceTheSameIndicatorSeries()
    {
        var series = new IndicatorSeries([Point(0, 1m)]);

        var result = new MultiSeriesIndicatorResult([
            new NamedIndicatorSeries("Primary", series), new NamedIndicatorSeries("Secondary", series)]);

        Assert.Equal(2, result.Count);
        Assert.True(result.TryGetSeries("Primary", out var first));
        Assert.True(result.TryGetSeries("Secondary", out var second));
        Assert.Same(series, first);
        Assert.Same(series, second);
    }

    [Theory]
    [InlineData("Secondary")]
    [InlineData("  Secondary  ")]
    [InlineData("\tSecondary\r\n")]
    [InlineData("\u00a0Secondary\u2003")]
    public void ExistingExactOrTrimmedLookupReturnsTheOriginalSeries(string name)
    {
        var originals = Entries();
        var result = new MultiSeriesIndicatorResult(originals);

        Assert.True(result.TryGetSeries(name, out var found));

        Assert.Same(originals[0].Series, found);
    }

    [Theory]
    [InlineData("Missing")]
    [InlineData("secondary")]
    [InlineData("SECONDARY")]
    public void MissingOrCaseMismatchedLookupReturnsFalseAndClearsTheOutValue(string name)
    {
        var result = new MultiSeriesIndicatorResult(Entries());
        IndicatorSeries? found = new([]);

        Assert.False(result.TryGetSeries(name, out found));

        Assert.Null(found);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    [InlineData("\u00a0")]
    public void RejectsInvalidLookupNamesEvenWhenResultIsEmpty(string? name)
    {
        var result = new MultiSeriesIndicatorResult([]);

        var exception = Assert.Throws<ArgumentException>(() => result.TryGetSeries(name, out _));

        Assert.Equal("name", exception.ParamName);
    }

    [Fact]
    public void ValidLookupOnAnEmptyResultReturnsFalseAndNull()
    {
        var result = new MultiSeriesIndicatorResult([]);

        Assert.False(result.TryGetSeries("Primary", out var found));

        Assert.Null(found);
    }

    [Fact]
    public void RepeatedConstructionPreservesCountOrderNamesReferencesAndLookupBehavior()
    {
        var source = Entries();
        var first = new MultiSeriesIndicatorResult(source);
        var second = new MultiSeriesIndicatorResult(source);

        Assert.Equal(first.Count, second.Count);
        for (var index = 0; index < source.Length; index++)
        {
            Assert.Equal(first.Series[index].Name, second.Series[index].Name);
            Assert.Same(first.Series[index], second.Series[index]);
            Assert.Same(first.Series[index].Series, second.Series[index].Series);
            Assert.True(first.TryGetSeries(source[index].Name, out var firstFound));
            Assert.True(second.TryGetSeries(source[index].Name, out var secondFound));
            Assert.Same(firstFound, secondFound);
        }
        Assert.False(first.TryGetSeries("secondary", out var firstMissing));
        Assert.False(second.TryGetSeries("secondary", out var secondMissing));
        Assert.Null(firstMissing);
        Assert.Null(secondMissing);
    }

    private static NamedIndicatorSeries[] Entries() =>
    [
        Named("Secondary", 2m), Named("Primary", 1m), Named("Tertiary", 3m)
    ];

    private static NamedIndicatorSeries Named(string name, params decimal[] values) =>
        new(name, new IndicatorSeries(values.Select((value, index) => Point(index, value)).ToArray()));

    private static IndicatorPoint Point(int minutes, decimal value) =>
        new(new UtcInstant(Time.Value.AddMinutes(minutes)), value);
}
