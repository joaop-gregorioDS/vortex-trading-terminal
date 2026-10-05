using VortexTrading.Application.IndicatorConsumption;
using VortexTrading.Application.Indicators;
using VortexTrading.Application.MarketData.Replay;
using VortexTrading.Application.ReplayIndicators;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Application.Tests.ReplayIndicators;

public sealed class CausalIndicatorReplayRuntimeTests
{
    private static readonly InstrumentId A = new(new Guid("11111111-1111-1111-1111-111111111111"));
    private static readonly InstrumentId B = new(new Guid("22222222-2222-2222-2222-222222222222"));
    private static readonly UtcInstant Start = new(
        new DateTimeOffset(2026, 10, 4, 10, 0, 0, TimeSpan.Zero).AddTicks(1234567));

    [Fact]
    public void RejectsNullDatasetThroughTheOwnedSession()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new CausalIndicatorReplayRuntime(null));
        Assert.Equal("dataset", exception.ParamName);
    }

    [Fact]
    public void EmptySourceRemainsRejectedByTheDatasetContract()
    {
        // No empty dataset can reach the runtime; do not invent a separate empty-session policy.
        var exception = Assert.Throws<ArgumentException>(() =>
            new CausalIndicatorReplayRuntime(new HistoricalReplayDataset([])));
        Assert.Equal("events", exception.ParamName);
    }

    [Fact]
    public void InitialTimeIsCausalAvailabilityButNoCandleHasBeenConsumed()
    {
        var candle = Flat(10m, interval: CandleInterval.FiveMinutes);
        var runtime = Runtime(ReplayEvent.FromCandle(candle));

        Assert.Equal(0, runtime.Position);
        Assert.Equal(candle.CloseTime, runtime.CurrentTime);
        Assert.False(runtime.IsCompleted);
        Assert.False(runtime.HasAcceptanceFailure);
        Assert.Empty(runtime.Calculate(A, candle.Interval, new SmaSpecification(1)).Series[0].Series.Points);
        Assert.Equal(0, runtime.Position);
    }

    [Theory]
    [InlineData(ReplayEventKind.Quote)]
    [InlineData(ReplayEventKind.Trade)]
    public void QuoteAndTradeReturnOriginalEventsWithoutAddingCandles(ReplayEventKind kind)
    {
        var original = kind == ReplayEventKind.Quote ? QuoteAt(Start) : TradeAt(Start);
        var runtime = Runtime(original);
        var before = runtime.Calculate(A, CandleInterval.OneMinute, new VwapSpecification());

        Assert.True(runtime.TryMoveNext(out var delivered));
        Assert.Same(original, delivered);
        Assert.Equal(1, runtime.Position);
        Assert.Equal(Start, runtime.CurrentTime);
        Assert.True(runtime.IsCompleted);
        Assert.False(runtime.HasAcceptanceFailure);
        AssertResult(before, runtime.Calculate(A, CandleInterval.OneMinute, new VwapSpecification()));
    }

    [Fact]
    public void CandleBecomesVisibleOnlyAfterItsOriginalEventIsDelivered()
    {
        var candle = Flat(10.000000000000000000000000001m);
        var original = ReplayEvent.FromCandle(candle);
        var runtime = Runtime(original);
        var specification = new SmaSpecification(1);

        Assert.Empty(runtime.Calculate(A, candle.Interval, specification).Series[0].Series.Points);
        Assert.True(runtime.TryMoveNext(out var delivered));
        Assert.Same(original, delivered);
        Assert.Same(candle, delivered.Candle);
        Assert.Equal(candle.CloseTime, runtime.CurrentTime);
        AssertOracle(runtime, A, candle.Interval, specification, [candle]);
    }

    [Fact]
    public void FutureCandleDoesNotLeakThroughAnEarlierQuote()
    {
        var candle = Flat(40m, interval: CandleInterval.FiveMinutes);
        var quote = QuoteAt(At(2));
        var runtime = Runtime(ReplayEvent.FromCandle(candle), quote);
        var specification = new SmaSpecification(1);

        Assert.Equal(At(2), runtime.CurrentTime);
        Assert.True(runtime.TryMoveNext(out var delivered));
        Assert.Same(quote, delivered);
        Assert.Empty(runtime.Calculate(A, candle.Interval, specification).Series[0].Series.Points);
        Assert.Equal(1, runtime.Position);
        Assert.Equal(At(2), runtime.CurrentTime);
        Assert.True(runtime.TryMoveNext(out _));
        AssertOracle(runtime, A, candle.Interval, specification, [candle]);
    }

    [Fact]
    public void EqualAvailabilityExposesOnlyTheCandlesDeliveredAtEachPosition()
    {
        var first = Flat(10m, interval: CandleInterval.FiveMinutes);
        var second = Flat(90m, At(4), B);
        var firstEvent = ReplayEvent.FromCandle(first);
        var secondEvent = ReplayEvent.FromCandle(second);
        var quote = QuoteAt(At(5));
        var trade = TradeAt(At(5));
        var runtime = Runtime(trade, secondEvent, quote, firstEvent);
        var specification = new SmaSpecification(1);

        Assert.Equal(At(5), runtime.CurrentTime);
        Assert.Empty(runtime.Calculate(A, first.Interval, specification).Series[0].Series.Points);
        Assert.Empty(runtime.Calculate(B, second.Interval, specification).Series[0].Series.Points);
        Assert.True(runtime.TryMoveNext(out var deliveredFirst));
        Assert.Same(firstEvent, deliveredFirst);
        Assert.Equal(1, runtime.Position);
        Assert.Equal(At(5), runtime.CurrentTime);
        AssertOracle(runtime, A, first.Interval, specification, [first]);
        Assert.Empty(runtime.Calculate(B, second.Interval, specification).Series[0].Series.Points);
        Assert.True(runtime.TryMoveNext(out var deliveredSecond));
        Assert.Same(secondEvent, deliveredSecond);
        AssertOracle(runtime, B, second.Interval, specification, [second]);

        var before = runtime.Calculate(A, first.Interval, specification);
        foreach (var expected in new[] { quote, trade })
        {
            Assert.True(runtime.TryMoveNext(out var delivered));
            Assert.Same(expected, delivered);
            Assert.Equal(At(5), runtime.CurrentTime);
            AssertResult(before, runtime.Calculate(A, first.Interval, specification));
        }
        Assert.Equal(4, runtime.Position);
    }

    [Fact]
    public void QuoteAndTradeBetweenCandlesChangeObservationTimeButNotIndicatorPoints()
    {
        var first = Flat(10m);
        var second = Flat(30m, At(4));
        var quote = QuoteAt(At(2));
        var trade = TradeAt(At(3));
        var runtime = Runtime(ReplayEvent.FromCandle(second), trade, ReplayEvent.FromCandle(first), quote);
        var specification = new SmaSpecification(1);
        Assert.True(runtime.TryMoveNext(out _));
        var before = runtime.Calculate(A, first.Interval, specification);

        foreach (var expected in new[] { quote, trade })
        {
            Assert.True(runtime.TryMoveNext(out var delivered));
            Assert.Same(expected, delivered);
            Assert.True(runtime.CurrentTime.CompareTo(before.Series[0].Series.Points[0].Time) > 0);
            AssertResult(before, runtime.Calculate(A, first.Interval, specification));
        }

        Assert.True(runtime.TryMoveNext(out _));
        AssertOracle(runtime, A, first.Interval, specification, [first, second]);
        Assert.Single(before.Series[0].Series.Points);
    }

    [Fact]
    public void MultipleInstrumentsHaveIndependentPrefixesAndValueBasedKeys()
    {
        var candles = new[] { Flat(10m), Flat(100m, At(1), B), Flat(20m, At(2)), Flat(200m, At(3), B) };
        var runtime = Runtime(candles.Select(ReplayEvent.FromCandle).ToArray());
        var acceptedA = new List<Candle>();
        var acceptedB = new List<Candle>();
        while (runtime.TryMoveNext(out var delivered))
        {
            var candle = delivered.Candle!;
            (candle.InstrumentId == A ? acceptedA : acceptedB).Add(candle);
            AssertOracle(runtime, new InstrumentId(A.Value), CandleInterval.OneMinute, new SmaSpecification(1), acceptedA);
            AssertOracle(runtime, new InstrumentId(B.Value), CandleInterval.OneMinute, new SmaSpecification(1), acceptedB);
        }
    }

    [Fact]
    public void OneFiveAndSixtyMinuteContextsRemainIndependent()
    {
        var candles = new[]
        {
            Flat(10m), Flat(100m, interval: CandleInterval.FiveMinutes),
            Flat(1000m, interval: CandleInterval.OneHour), Flat(20m, At(2))
        };
        var runtime = Runtime(candles.Select(ReplayEvent.FromCandle).Reverse().ToArray());
        ConsumeAll(runtime);

        foreach (var interval in new[] { CandleInterval.OneMinute, CandleInterval.FiveMinutes, CandleInterval.OneHour })
            AssertOracle(runtime, A, interval, new SmaSpecification(1),
                candles.Where(candle => candle.Interval == interval).ToArray());
        Assert.False(runtime.HasAcceptanceFailure);
    }

    [Fact]
    public void EquivalentIntervalInstancesAddressTheSameBuffer()
    {
        var equivalent = new CandleInterval(CandleInterval.OneMinute.Duration);
        var first = Flat(10m);
        var second = Flat(20m, At(2), interval: equivalent);
        var runtime = Runtime(ReplayEvent.FromCandle(first), ReplayEvent.FromCandle(second));
        ConsumeAll(runtime);

        Assert.NotSame(first.Interval, second.Interval);
        AssertOracle(runtime, A, new CandleInterval(equivalent.Duration), new SmaSpecification(2), [first, second]);
        Assert.Single(runtime.Calculate(A, CandleInterval.OneMinute, new SmaSpecification(2)).Series[0].Series.Points);
    }

    [Fact]
    public void MissingContextIsAnEmptyBatchRatherThanAnError()
    {
        var runtime = Runtime(ReplayEvent.FromCandle(Flat(10m)));
        ConsumeAll(runtime);
        AssertOracle(runtime, B, CandleInterval.OneHour, new MacdSpecification(2, 4, 3), []);
        AssertOracle(runtime, A, CandleInterval.OneHour, new ObvSpecification(), []);
        Assert.Equal(1, runtime.Position);
        Assert.False(runtime.HasAcceptanceFailure);
    }

    [Fact]
    public void MultipleSpecificationsUseTheSamePrefixWithoutRetainingConfiguration()
    {
        var candles = Sequence(60);
        var runtime = Runtime(candles.Select(ReplayEvent.FromCandle).ToArray());
        ConsumeAll(runtime);
        var specifications = new IndicatorSpecification[]
        {
            new SmaSpecification(20), new SmaSpecification(50), new EmaSpecification(20),
            new RsiSpecification(14), new MacdSpecification(12, 26, 9)
        };
        var before = runtime.Calculate(A, CandleInterval.OneMinute, specifications[0]);

        foreach (var specification in specifications)
            AssertOracle(runtime, A, CandleInterval.OneMinute, specification, candles);
        AssertResult(before, runtime.Calculate(A, CandleInterval.OneMinute, new SmaSpecification(20)));
        Assert.Equal(60, runtime.Position);
    }

    public static TheoryData<IndicatorSpecification> AllSpecifications => new()
    {
        new SmaSpecification(4), new EmaSpecification(4), new RsiSpecification(4),
        new AtrSpecification(4), new MacdSpecification(2, 7, 3),
        new StochasticSpecification(5, 2, 3), new BollingerBandsSpecification(4, 2.125m),
        new VwapSpecification(), new AdxSpecification(4), new ObvSpecification()
    };

    [Theory]
    [MemberData(nameof(AllSpecifications))]
    public void AllTenIndicatorsMatchTheBoundaryOnEveryActuallyAcceptedPrefix(IndicatorSpecification specification)
    {
        var candles = Sequence(30);
        var runtime = Runtime(candles.Select(ReplayEvent.FromCandle).Reverse().ToArray());
        var accepted = new List<Candle>();
        AssertOracle(runtime, A, CandleInterval.OneMinute, specification, accepted);
        while (runtime.TryMoveNext(out var delivered))
        {
            accepted.Add(delivered.Candle!);
            var position = runtime.Position;
            var time = runtime.CurrentTime;
            AssertOracle(runtime, A, CandleInterval.OneMinute, specification, accepted);
            Assert.Equal(position, runtime.Position);
            Assert.Equal(time, runtime.CurrentTime);
            Assert.False(runtime.HasAcceptanceFailure);
        }
    }

    public static TheoryData<IndicatorSpecification, int, int[]> WarmUps => new()
    {
        { new SmaSpecification(3), 2, [0] },
        { new SmaSpecification(3), 3, [1] },
        { new MacdSpecification(2, 4, 3), 3, [0, 0, 0] },
        { new MacdSpecification(2, 4, 3), 4, [1, 0, 0] },
        { new MacdSpecification(2, 4, 3), 5, [2, 0, 0] },
        { new MacdSpecification(2, 4, 3), 6, [3, 1, 1] },
        { new AdxSpecification(2), 2, [0, 0, 0] },
        { new AdxSpecification(2), 3, [1, 1, 0] },
        { new AdxSpecification(2), 4, [2, 2, 1] }
    };

    [Theory]
    [MemberData(nameof(WarmUps))]
    public void WarmUpPreservesEmptyAndIndependentComponents(
        IndicatorSpecification specification, int count, int[] expectedCounts)
    {
        var candles = Sequence(count + 1);
        var runtime = Runtime(candles.Select(ReplayEvent.FromCandle).ToArray());
        for (var index = 0; index < count; index++) Assert.True(runtime.TryMoveNext(out _));
        var result = runtime.Calculate(A, CandleInterval.OneMinute, specification);

        Assert.Equal(expectedCounts, result.Series.Select(entry => entry.Series.Count).ToArray());
        AssertOracle(runtime, A, CandleInterval.OneMinute, specification, candles.Take(count).ToArray());
        Assert.False(runtime.IsCompleted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VwapAndObvKeepTheContextAnchorAcrossUtcMidnightGapsAndOtherContexts(bool obv)
    {
        var first = Flat(2m, new UtcInstant(new DateTimeOffset(2026, 10, 3, 23, 58, 0, TimeSpan.Zero)));
        var second = Flat(4m, new UtcInstant(new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero)));
        var runtime = Runtime(
            ReplayEvent.FromCandle(second), ReplayEvent.FromCandle(first), QuoteAt(Start), TradeAt(At(1)),
            ReplayEvent.FromCandle(Flat(100m, Start, B)),
            ReplayEvent.FromCandle(Flat(200m, At(2), interval: CandleInterval.FiveMinutes)));
        ConsumeAll(runtime);
        IndicatorSpecification specification = obv ? new ObvSpecification() : new VwapSpecification();
        var result = runtime.Calculate(A, CandleInterval.OneMinute, specification);

        AssertOracle(runtime, A, CandleInterval.OneMinute, specification, [first, second]);
        Assert.Equal(obv ? new[] { 0m, 1m } : new[] { 2m, 3m },
            result.Series[0].Series.Points.Select(point => point.Value).ToArray());
        Assert.Equal(second.CloseTime, result.Series[0].Series.Points[^1].Time);
    }

    [Theory]
    [InlineData("same envelope")]
    [InlineData("shared payload")]
    [InlineData("equal distinct payload")]
    [InlineData("different values")]
    public void DuplicateContextOpenTimeFailsAfterDeliveryAndTerminallyStopsTheRuntime(string duplicateKind)
    {
        var first = ReplayEvent.FromCandle(Flat(10m));
        var duplicate = duplicateKind switch
        {
            "same envelope" => first,
            "shared payload" => ReplayEvent.FromCandle(first.Candle),
            "equal distinct payload" => ReplayEvent.FromCandle(Flat(10m)),
            "different values" => ReplayEvent.FromCandle(Flat(99m)),
            _ => throw new InvalidOperationException()
        };
        var source = new HistoricalReplayDataset([first, duplicate, QuoteAt(At(3))]);
        var runtime = new CausalIndicatorReplayRuntime(source);
        Assert.True(runtime.TryMoveNext(out var accepted));
        Assert.Same(first, accepted);
        var prefixResult = runtime.Calculate(A, CandleInterval.OneMinute, new SmaSpecification(1));
        ReplayEvent? reported = first;

        var exception = Assert.Throws<InvalidOperationException>(() => runtime.TryMoveNext(out reported));

        Assert.Null(reported);
        Assert.Contains("replay position 2", exception.Message);
        Assert.Contains(A.ToString(), exception.Message);
        Assert.Contains(CandleInterval.OneMinute.ToString(), exception.Message);
        Assert.Contains($"incoming OpenTime {duplicate.Candle!.OpenTime}", exception.Message);
        Assert.Contains($"previous accepted OpenTime {first.Candle!.OpenTime}", exception.Message);
        Assert.Equal(2, runtime.Position);
        Assert.Equal(first.Candle.CloseTime, runtime.CurrentTime);
        Assert.False(runtime.IsCompleted);
        Assert.True(runtime.HasAcceptanceFailure);

        // A previously returned immutable result remains usable; no failed-runtime buffer escape hatch is added.
        AssertResult(new IndicatorBatchCalculator().Calculate(new SmaSpecification(1), [first.Candle]), prefixResult);
        Assert.Same(first, source.Events[0]);
        Assert.Same(duplicate, source.Events[1]);
        var time = runtime.CurrentTime;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            reported = first;
            Assert.Throws<InvalidOperationException>(() => runtime.TryMoveNext(out reported));
            Assert.Null(reported);
            // If calculation were reached, this subtype would throw NotSupportedException instead.
            Assert.Throws<InvalidOperationException>(() =>
                runtime.Calculate(A, CandleInterval.OneMinute, new UnsupportedSpecification()));
            Assert.Throws<InvalidOperationException>(() => runtime.Calculate(null!, null!, null!));
            Assert.Equal(2, runtime.Position);
            Assert.Same(time, runtime.CurrentTime);
            Assert.False(runtime.IsCompleted);
            Assert.True(runtime.HasAcceptanceFailure);
        }
    }

    [Fact]
    public void DuplicateOnTheFinalEventCanCompleteReplayAndFailAcceptanceSimultaneously()
    {
        var first = ReplayEvent.FromCandle(Flat(10m));
        var runtime = Runtime(first, ReplayEvent.FromCandle(Flat(20m)));
        Assert.True(runtime.TryMoveNext(out _));
        Assert.Throws<InvalidOperationException>(() => runtime.TryMoveNext(out _));

        Assert.Equal(2, runtime.Position);
        Assert.Equal(first.Candle!.CloseTime, runtime.CurrentTime);
        Assert.True(runtime.IsCompleted);
        Assert.True(runtime.HasAcceptanceFailure);
        Assert.Throws<InvalidOperationException>(() => runtime.TryMoveNext(out _));
        Assert.Throws<InvalidOperationException>(() => runtime.Calculate(A, CandleInterval.OneMinute, new SmaSpecification(1)));
    }

    [Fact]
    public void SameOpenTimeForDifferentInstrumentsIsAcceptedIndependently()
    {
        var first = Flat(10m);
        var second = Flat(100m, instrument: B);
        var runtime = Runtime(ReplayEvent.FromCandle(second), ReplayEvent.FromCandle(first));
        ConsumeAll(runtime);

        AssertOracle(runtime, A, first.Interval, new SmaSpecification(1), [first]);
        AssertOracle(runtime, B, second.Interval, new SmaSpecification(1), [second]);
        Assert.False(runtime.HasAcceptanceFailure);
    }

    [Fact]
    public void SameOpenTimeForDifferentIntervalsIsAcceptedIndependently()
    {
        var first = Flat(10m);
        var second = Flat(100m, interval: CandleInterval.FiveMinutes);
        var runtime = Runtime(ReplayEvent.FromCandle(second), ReplayEvent.FromCandle(first));
        ConsumeAll(runtime);

        AssertOracle(runtime, A, first.Interval, new SmaSpecification(1), [first]);
        AssertOracle(runtime, A, second.Interval, new SmaSpecification(1), [second]);
        Assert.False(runtime.HasAcceptanceFailure);
    }

    [Fact]
    public void ShuffledSourceFollowsTheSessionAvailabilityOrderRatherThanDatasetOccurrenceOrder()
    {
        var candle = ReplayEvent.FromCandle(Flat(10m, interval: CandleInterval.FiveMinutes));
        var late = ReplayEvent.FromCandle(Flat(20m, At(6)));
        var quote = QuoteAt(At(4));
        var trade = TradeAt(At(2));
        var dataset = new HistoricalReplayDataset([late, quote, candle, trade]);
        Assert.Same(candle, dataset.Events[0]);
        var runtime = new CausalIndicatorReplayRuntime(dataset);
        var expected = new[] { trade, quote, candle, late };
        var times = new[] { At(2), At(4), At(5), At(7) };

        for (var index = 0; index < expected.Length; index++)
        {
            Assert.True(runtime.TryMoveNext(out var delivered));
            Assert.Same(expected[index], delivered);
            Assert.Equal(index + 1, runtime.Position);
            Assert.Equal(times[index], runtime.CurrentTime);
        }
    }

    [Theory]
    [InlineData("null specification")]
    [InlineData("invalid period")]
    [InlineData("invalid ordering")]
    [InlineData("unsupported subtype")]
    public void ConfigurationFailuresPropagateUnchangedWithoutFaultingOrAdvancing(string failure)
    {
        IndicatorSpecification? specification = failure switch
        {
            "null specification" => null,
            "invalid period" => new SmaSpecification(0),
            "invalid ordering" => new MacdSpecification(7, 2, 3),
            "unsupported subtype" => new UnsupportedSpecification(),
            _ => throw new InvalidOperationException()
        };
        var first = Flat(10m);
        var second = Flat(20m, At(2));
        var runtime = Runtime(ReplayEvent.FromCandle(first), ReplayEvent.FromCandle(second));
        Assert.True(runtime.TryMoveNext(out _));
        var time = runtime.CurrentTime;
        var expected = Record.Exception(() => new IndicatorBatchCalculator().Calculate(specification!, [first]));
        var actual = Record.Exception(() => runtime.Calculate(A, first.Interval, specification!));

        Assert.NotNull(expected);
        Assert.NotNull(actual);
        Assert.Equal(expected.GetType(), actual.GetType());
        Assert.Equal(expected.Message, actual.Message);
        if (expected is ArgumentException expectedArgument)
            Assert.Equal(expectedArgument.ParamName, Assert.IsAssignableFrom<ArgumentException>(actual).ParamName);
        Assert.Equal(1, runtime.Position);
        Assert.Same(time, runtime.CurrentTime);
        Assert.False(runtime.HasAcceptanceFailure);
        AssertOracle(runtime, A, first.Interval, new SmaSpecification(1), [first]);
        Assert.True(runtime.TryMoveNext(out _));
        AssertOracle(runtime, A, first.Interval, new SmaSpecification(1), [first, second]);
    }

    [Fact]
    public void TrueOverflowKeepsAcceptedCandlesAndAllowsAnotherCalculationAndAdvance()
    {
        var candles = new[] { Flat(decimal.MaxValue), Flat(decimal.MaxValue, At(2)) };
        var quote = QuoteAt(At(4));
        var runtime = Runtime(ReplayEvent.FromCandle(candles[0]), ReplayEvent.FromCandle(candles[1]), quote);
        Assert.True(runtime.TryMoveNext(out _));
        Assert.True(runtime.TryMoveNext(out _));
        var time = runtime.CurrentTime;
        var specification = new SmaSpecification(2);
        var expected = Assert.Throws<OverflowException>(() => new IndicatorBatchCalculator().Calculate(specification, candles));
        var actual = Assert.Throws<OverflowException>(() => runtime.Calculate(A, CandleInterval.OneMinute, specification));

        Assert.Equal(expected.Message, actual.Message);
        Assert.Equal(2, runtime.Position);
        Assert.Same(time, runtime.CurrentTime);
        Assert.False(runtime.HasAcceptanceFailure);
        Assert.Throws<OverflowException>(() => runtime.Calculate(A, CandleInterval.OneMinute, specification));
        AssertOracle(runtime, A, CandleInterval.OneMinute, new SmaSpecification(1), candles);
        Assert.True(runtime.TryMoveNext(out var delivered));
        Assert.Same(quote, delivered);
        AssertOracle(runtime, A, CandleInterval.OneMinute, new SmaSpecification(1), candles);
    }

    [Fact]
    public void AdvancingAcceptsCandleWithoutAutomaticallyRunningItsOverflowingVwap()
    {
        var candle = Flat(decimal.MaxValue, volume: decimal.MaxValue);
        var runtime = Runtime(ReplayEvent.FromCandle(candle));
        Assert.True(runtime.TryMoveNext(out _));
        Assert.False(runtime.HasAcceptanceFailure);
        Assert.Throws<OverflowException>(() => runtime.Calculate(A, candle.Interval, new VwapSpecification()));
        AssertOracle(runtime, A, candle.Interval, new SmaSpecification(1), [candle]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NullContextArgumentsFailWithoutAdvancingOrFaulting(bool nullInstrument)
    {
        var runtime = Runtime(ReplayEvent.FromCandle(Flat(10m)));
        var time = runtime.CurrentTime;
        var exception = Assert.Throws<ArgumentNullException>(() => runtime.Calculate(
            nullInstrument ? null! : A, nullInstrument ? CandleInterval.OneMinute : null!, new SmaSpecification(1)));

        Assert.Equal(nullInstrument ? "instrumentId" : "interval", exception.ParamName);
        Assert.Equal(0, runtime.Position);
        Assert.Same(time, runtime.CurrentTime);
        Assert.False(runtime.HasAcceptanceFailure);
        Assert.True(runtime.TryMoveNext(out _));
    }

    [Fact]
    public void HealthyCompletionRemainsStableAndAllowsFinalCalculations()
    {
        var candle = Flat(10m);
        var runtime = Runtime(ReplayEvent.FromCandle(candle), QuoteAt(At(3)));
        ConsumeAll(runtime);
        var time = runtime.CurrentTime;
        Assert.Equal(2, runtime.Position);
        Assert.True(runtime.IsCompleted);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            Assert.False(runtime.TryMoveNext(out var delivered));
            Assert.Null(delivered);
            AssertOracle(runtime, A, candle.Interval, new SmaSpecification(1), [candle]);
            Assert.Equal(2, runtime.Position);
            Assert.Same(time, runtime.CurrentTime);
            Assert.False(runtime.HasAcceptanceFailure);
        }
    }

    [Fact]
    public void EquivalentDatasetsProduceIdenticalEventsStateAndCalculations()
    {
        var candles = Sequence(12);
        var events = candles.Select(ReplayEvent.FromCandle).Concat(new[] { QuoteAt(At(3)), TradeAt(At(7)) }).Reverse().ToArray();
        var first = new CausalIndicatorReplayRuntime(new HistoricalReplayDataset(events));
        var second = new CausalIndicatorReplayRuntime(new HistoricalReplayDataset(events));

        do
        {
            Assert.Equal(first.Position, second.Position);
            Assert.Equal(first.CurrentTime, second.CurrentTime);
            Assert.Equal(first.IsCompleted, second.IsCompleted);
            foreach (var specification in AllSpecifications)
                AssertResult(first.Calculate(A, CandleInterval.OneMinute, specification),
                    second.Calculate(A, CandleInterval.OneMinute, specification));
            var moved = first.TryMoveNext(out var firstEvent);
            Assert.Equal(moved, second.TryMoveNext(out var secondEvent));
            Assert.Same(firstEvent, secondEvent);
            if (!moved) break;
        } while (true);
    }

    [Fact]
    public void EquivalentExecutionsFailAtTheSamePositionWithoutAffectingEachOther()
    {
        var events = new[] { ReplayEvent.FromCandle(Flat(10m)), ReplayEvent.FromCandle(Flat(99m)), QuoteAt(At(3)) };
        var first = Runtime(events);
        var second = Runtime(events);
        Assert.True(first.TryMoveNext(out _));
        var firstException = Assert.Throws<InvalidOperationException>(() => first.TryMoveNext(out _));
        Assert.Equal(0, second.Position);
        Assert.False(second.HasAcceptanceFailure);
        Assert.True(second.TryMoveNext(out _));
        var secondException = Assert.Throws<InvalidOperationException>(() => second.TryMoveNext(out _));

        Assert.Equal(firstException.Message, secondException.Message);
        Assert.Equal(first.Position, second.Position);
        Assert.Equal(first.CurrentTime, second.CurrentTime);
        Assert.Equal(first.IsCompleted, second.IsCompleted);
        Assert.True(first.HasAcceptanceFailure);
        Assert.True(second.HasAcceptanceFailure);
    }

    [Fact]
    public void DatasetPayloadsSpecificationsAndPreviouslyReturnedResultsRemainIntact()
    {
        var candles = Sequence(6);
        var input = candles.Select(ReplayEvent.FromCandle).Reverse().ToList();
        var dataset = new HistoricalReplayDataset(input);
        var events = dataset.Events.ToArray();
        var start = dataset.StartTime;
        var end = dataset.EndTime;
        var runtime = new CausalIndicatorReplayRuntime(dataset);
        input.Clear();
        Assert.True(runtime.TryMoveNext(out var first));
        Assert.Same(candles[0], first.Candle);
        var specification = new BollingerBandsSpecification(1, 2.000000000000000000000000001m);
        var retained = runtime.Calculate(A, CandleInterval.OneMinute, specification);
        var point = retained.Series[0].Series.Points[0];
        ConsumeAll(runtime);

        AssertResult(new IndicatorBatchCalculator().Calculate(specification, [candles[0]]), retained);
        Assert.Same(point, retained.Series[0].Series.Points[0]);
        Assert.Throws<NotSupportedException>(() => ((IList<IndicatorPoint>)retained.Series[0].Series.Points).Clear());
        Assert.Equal(new BollingerBandsSpecification(1, 2.000000000000000000000000001m), specification);
        Assert.Same(start, dataset.StartTime);
        Assert.Same(end, dataset.EndTime);
        Assert.Equal(events.Length, dataset.Count);
        for (var index = 0; index < events.Length; index++) Assert.Same(events[index], dataset.Events[index]);
        AssertOracle(runtime, A, CandleInterval.OneMinute, specification, candles);
    }

    private static CausalIndicatorReplayRuntime Runtime(params ReplayEvent[] events) =>
        new(new HistoricalReplayDataset(events));

    private static void ConsumeAll(CausalIndicatorReplayRuntime runtime)
    {
        while (runtime.TryMoveNext(out _)) { }
    }

    private static void AssertOracle(CausalIndicatorReplayRuntime runtime, InstrumentId instrument,
        CandleInterval interval, IndicatorSpecification specification, IReadOnlyList<Candle> expectedPrefix) =>
        AssertResult(new IndicatorBatchCalculator().Calculate(specification, expectedPrefix),
            runtime.Calculate(instrument, interval, specification));

    private static void AssertResult(MultiSeriesIndicatorResult expected, MultiSeriesIndicatorResult actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var index = 0; index < expected.Count; index++)
        {
            Assert.Equal(expected.Series[index].Name, actual.Series[index].Name);
            var expectedPoints = expected.Series[index].Series.Points;
            var actualPoints = actual.Series[index].Series.Points;
            Assert.Equal(expectedPoints.Count, actualPoints.Count);
            for (var pointIndex = 0; pointIndex < expectedPoints.Count; pointIndex++)
            {
                Assert.Equal(expectedPoints[pointIndex].Time, actualPoints[pointIndex].Time);
                Assert.Equal(decimal.GetBits(expectedPoints[pointIndex].Value), decimal.GetBits(actualPoints[pointIndex].Value));
            }
        }
    }

    private static Candle[] Sequence(int count) => Enumerable.Range(0, count).Select(index =>
    {
        var close = 10m + index * index % 17 + index * 0.125m;
        return new Candle(A, CandleInterval.OneMinute, At(index * 2), new Price(close + 0.5m),
            new Price(close + 2m + index % 4 * 0.5m), new Price(close - 3m), new Price(close),
            new Quantity(index % 5 == 0 ? 0m : (index + 1) * 0.125m));
    }).ToArray();

    private static Candle Flat(decimal close, UtcInstant? open = null, InstrumentId? instrument = null,
        CandleInterval? interval = null, decimal volume = 1m)
    {
        var price = new Price(close);
        return new Candle(instrument ?? A, interval ?? CandleInterval.OneMinute, open ?? Start,
            price, price, price, price, new Quantity(volume));
    }

    private static ReplayEvent QuoteAt(UtcInstant time) => ReplayEvent.FromQuote(
        new Quote(A, time, new UtcInstant(time.Value.AddTicks(7)),
            new Price(10m), new Quantity(1m), new Price(11m), new Quantity(1m)));

    private static ReplayEvent TradeAt(UtcInstant time) => ReplayEvent.FromTrade(
        new Trade(A, time, new UtcInstant(time.Value.AddTicks(7)), new Price(12m), new Quantity(1m)));

    private static UtcInstant At(int minutes) => new(Start.Value.AddMinutes(minutes));

    private sealed record UnsupportedSpecification : IndicatorSpecification
    {
        public override string MachineId => "sma";
        public UnsupportedSpecification() : base(new SmaSpecification(1)) { }
    }
}
