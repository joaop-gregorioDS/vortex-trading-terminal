using VortexTrading.Application.MarketData.Replay;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Application.Tests;

public sealed class ReplayTimelineTests
{
    private static readonly InstrumentId Id = new(new Guid("11111111-1111-1111-1111-111111111111"));
    private static readonly UtcInstant Start = new(new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero).AddTicks(7));
    private static readonly UtcInstant End = new(Start.Value.AddTicks(10));

    [Fact]
    public void RejectsNullDataset()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new ReplayTimeline(null));
        Assert.Equal("dataset", exception.ParamName);
    }

    [Fact]
    public void RejectsNullAdvanceWithoutChangingCurrentTime()
    {
        var timeline = new ReplayTimeline(CreateDataset());

        var exception = Assert.Throws<ArgumentNullException>(() => timeline.AdvanceTo(null));

        Assert.Equal("instant", exception.ParamName);
        Assert.Same(Start, timeline.CurrentTime);
    }

    [Fact]
    public void StartsAtDatasetStartAndPreservesItsRange()
    {
        var dataset = CreateDataset();
        var timeline = new ReplayTimeline(dataset);

        Assert.Same(dataset.StartTime, timeline.CurrentTime);
        Assert.Same(dataset.StartTime, timeline.StartTime);
        Assert.Same(dataset.EndTime, timeline.EndTime);
    }

    [Fact]
    public void AdvancesToLaterTimeWithTickPrecision()
    {
        var timeline = new ReplayTimeline(CreateDataset());
        var next = new UtcInstant(Start.Value.AddTicks(1));

        timeline.AdvanceTo(next);

        Assert.Same(next, timeline.CurrentTime);
        Assert.Same(Start, timeline.StartTime);
        Assert.Same(End, timeline.EndTime);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EqualInstantIsAllowedAndLeavesTheCurrentPositionUnchanged(bool atEnd)
    {
        var timeline = new ReplayTimeline(CreateDataset());
        if (atEnd) timeline.AdvanceTo(End);
        var current = timeline.CurrentTime;

        timeline.AdvanceTo(new UtcInstant(current.Value));

        Assert.Same(current, timeline.CurrentTime);
    }

    [Fact]
    public void RejectsBackwardAdvanceWithinTheRangeWithoutChangingPosition()
    {
        var timeline = new ReplayTimeline(CreateDataset());
        var current = new UtcInstant(Start.Value.AddTicks(5));
        timeline.AdvanceTo(current);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => timeline.AdvanceTo(new UtcInstant(Start.Value.AddTicks(4))));

        Assert.Equal("instant", exception.ParamName);
        Assert.Same(current, timeline.CurrentTime);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(11)]
    public void RejectsTimeOutsideEitherDatasetBoundaryWithoutChangingPosition(int offsetTicks)
    {
        var timeline = new ReplayTimeline(CreateDataset());

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => timeline.AdvanceTo(new UtcInstant(Start.Value.AddTicks(offsetTicks))));

        Assert.Equal("instant", exception.ParamName);
        Assert.Same(Start, timeline.CurrentTime);
    }

    [Fact]
    public void AcceptsExactlyTheDatasetEndTime()
    {
        var timeline = new ReplayTimeline(CreateDataset());

        timeline.AdvanceTo(End);

        Assert.Same(End, timeline.CurrentTime);
    }

    [Fact]
    public void SingleTimestampDatasetAllowsItsOnlyPosition()
    {
        var timeline = new ReplayTimeline(new HistoricalReplayDataset([CreateTrade(Start)]));

        timeline.AdvanceTo(new UtcInstant(Start.Value));

        Assert.Same(Start, timeline.CurrentTime);
        Assert.Equal(timeline.StartTime, timeline.EndTime);
        Assert.Throws<ArgumentOutOfRangeException>(() => timeline.AdvanceTo(new UtcInstant(Start.Value.AddTicks(-1))));
        Assert.Throws<ArgumentOutOfRangeException>(() => timeline.AdvanceTo(new UtcInstant(Start.Value.AddTicks(1))));
    }

    [Fact]
    public void AdvancesAcrossTheFullInstantRangeWithoutDurationArithmetic()
    {
        var minimum = new UtcInstant(DateTimeOffset.MinValue);
        var maximum = new UtcInstant(DateTimeOffset.MaxValue);
        var timeline = new ReplayTimeline(new HistoricalReplayDataset([CreateTrade(maximum), CreateTrade(minimum)]));

        timeline.AdvanceTo(maximum);

        Assert.Same(maximum, timeline.CurrentTime);
        Assert.Same(minimum, timeline.StartTime);
    }

    [Fact]
    public void NewTimelinesHaveIndependentProgressAndRestartAtDatasetStart()
    {
        var dataset = CreateDataset();
        var first = new ReplayTimeline(dataset);
        var second = new ReplayTimeline(dataset);
        first.AdvanceTo(End);

        Assert.Same(End, first.CurrentTime);
        Assert.Same(Start, second.CurrentTime);
        second.AdvanceTo(new UtcInstant(Start.Value.AddTicks(1)));
        Assert.Same(End, first.CurrentTime);
        Assert.Same(Start, new ReplayTimeline(dataset).CurrentTime);
    }

    [Fact]
    public void MultipleEventsAtTheSameTimeCanBeProcessedWithoutAdvancingWallTime()
    {
        var price = new Price(10.0500m);
        var quantity = new Quantity(0.00100m);
        var quote = ReplayEvent.FromQuote(new Quote(Id, Start, new UtcInstant(Start.Value.AddDays(1)), price, quantity, price, quantity));
        var trade = CreateTrade(Start);
        var candle = ReplayEvent.FromCandle(new Candle(Id, CandleInterval.OneMinute, Start, price, price, price, price, quantity));
        var dataset = new HistoricalReplayDataset([candle, CreateTrade(End), trade, quote, trade]);
        var timeline = new ReplayTimeline(dataset);
        var positions = new List<UtcInstant>();

        foreach (var replayEvent in dataset.Events)
        {
            timeline.AdvanceTo(replayEvent.ReplayTime);
            positions.Add(timeline.CurrentTime);
        }

        Assert.Equal(new[] { Start, Start, Start, Start, End }, positions);
        Assert.Same(End, timeline.CurrentTime);
        Assert.Equal(new[] { ReplayEventKind.Quote, ReplayEventKind.Trade, ReplayEventKind.Trade, ReplayEventKind.Candle, ReplayEventKind.Trade }, dataset.Events.Select(replayEvent => replayEvent.Kind));
        Assert.True(candle.Candle!.CloseTime.CompareTo(dataset.EndTime) > 0);
    }

    [Fact]
    public void ExplicitBoundsInitializeTheTimelineAndPreserveSuppliedReferences()
    {
        var start = new UtcInstant(new DateTimeOffset(2026, 10, 2, 10, 0, 0, TimeSpan.Zero));
        var end = new UtcInstant(start.Value.AddHours(1));

        var timeline = new ReplayTimeline(start, end);

        Assert.Same(start, timeline.StartTime);
        Assert.Same(end, timeline.EndTime);
        Assert.Same(start, timeline.CurrentTime);
    }

    [Fact]
    public void ExplicitBoundsRejectNullStartAtRuntime()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new ReplayTimeline(null!, End));

        Assert.Equal("startTime", exception.ParamName);
    }

    [Fact]
    public void ExplicitBoundsRejectNullEndAtRuntime()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new ReplayTimeline(Start, null!));

        Assert.Equal("endTime", exception.ParamName);
    }

    [Fact]
    public void ExplicitBoundsRejectAnEndBeforeTheStart()
    {
        var start = new UtcInstant(new DateTimeOffset(2026, 10, 2, 11, 0, 0, TimeSpan.Zero));
        var end = new UtcInstant(start.Value.AddHours(-1));

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new ReplayTimeline(start, end));

        Assert.Equal("endTime", exception.ParamName);
    }

    [Fact]
    public void EqualExplicitBoundsAllowOnlyTheirSingleInstant()
    {
        var end = new UtcInstant(Start.Value);
        var timeline = new ReplayTimeline(Start, end);

        Assert.Same(Start, timeline.StartTime);
        Assert.Same(end, timeline.EndTime);
        Assert.Same(Start, timeline.CurrentTime);
        timeline.AdvanceTo(new UtcInstant(Start.Value));
        Assert.Same(Start, timeline.CurrentTime);
        Assert.Throws<ArgumentOutOfRangeException>(() => timeline.AdvanceTo(new UtcInstant(Start.Value.AddTicks(-1))));
        Assert.Throws<ArgumentOutOfRangeException>(() => timeline.AdvanceTo(new UtcInstant(Start.Value.AddTicks(1))));
        Assert.Same(Start, timeline.CurrentTime);
    }

    [Fact]
    public void AdvancesWithinExplicitBoundsWithoutChangingThoseBounds()
    {
        var start = new UtcInstant(new DateTimeOffset(2026, 10, 2, 10, 0, 0, TimeSpan.Zero));
        var end = new UtcInstant(start.Value.AddHours(1));
        var middle = new UtcInstant(start.Value.AddMinutes(30));
        var timeline = new ReplayTimeline(start, end);

        timeline.AdvanceTo(middle);

        Assert.Same(middle, timeline.CurrentTime);
        Assert.Same(start, timeline.StartTime);
        Assert.Same(end, timeline.EndTime);
    }

    [Fact]
    public void AcceptsExactlyTheExplicitEnd()
    {
        var timeline = new ReplayTimeline(Start, End);

        timeline.AdvanceTo(End);

        Assert.Same(End, timeline.CurrentTime);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(11)]
    public void RejectsInstantsOutsideExplicitBoundsWithoutChangingCurrentTime(int offsetTicks)
    {
        var timeline = new ReplayTimeline(Start, End);
        var current = new UtcInstant(Start.Value.AddTicks(5));
        timeline.AdvanceTo(current);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => timeline.AdvanceTo(new UtcInstant(Start.Value.AddTicks(offsetTicks))));

        Assert.Equal("instant", exception.ParamName);
        Assert.Same(current, timeline.CurrentTime);
        Assert.Same(Start, timeline.StartTime);
        Assert.Same(End, timeline.EndTime);
    }

    [Fact]
    public void ExplicitTimelineRejectsBackwardAdvanceWithinItsBounds()
    {
        var start = new UtcInstant(new DateTimeOffset(2026, 10, 2, 10, 0, 0, TimeSpan.Zero));
        var timeline = new ReplayTimeline(start, new UtcInstant(start.Value.AddHours(1)));
        var current = new UtcInstant(start.Value.AddMinutes(30));
        timeline.AdvanceTo(current);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => timeline.AdvanceTo(new UtcInstant(start.Value.AddMinutes(29))));

        Assert.Equal("instant", exception.ParamName);
        Assert.Same(current, timeline.CurrentTime);
    }

    [Fact]
    public void EqualCurrentTimeRemainsANoOpWithExplicitBounds()
    {
        var timeline = new ReplayTimeline(Start, End);
        var current = new UtcInstant(Start.Value.AddTicks(5));
        timeline.AdvanceTo(current);

        timeline.AdvanceTo(new UtcInstant(current.Value));

        Assert.Same(current, timeline.CurrentTime);
    }

    [Fact]
    public void ExplicitTimelineRejectsNullAdvanceWithoutChangingState()
    {
        var timeline = new ReplayTimeline(Start, End);

        var exception = Assert.Throws<ArgumentNullException>(() => timeline.AdvanceTo(null));

        Assert.Equal("instant", exception.ParamName);
        Assert.Same(Start, timeline.CurrentTime);
    }

    [Fact]
    public void ExplicitTimelineCanRepresentALaterExecutionHorizonWithoutADataset()
    {
        var start = new UtcInstant(new DateTimeOffset(2026, 10, 2, 10, 3, 0, TimeSpan.Zero));
        var end = new UtcInstant(new DateTimeOffset(2026, 10, 2, 10, 5, 0, TimeSpan.Zero));
        var timeline = new ReplayTimeline(start, end);

        Assert.Same(start, timeline.CurrentTime);
        timeline.AdvanceTo(end);

        Assert.Same(end, timeline.CurrentTime);
        Assert.Same(start, timeline.StartTime);
        Assert.Same(end, timeline.EndTime);
    }

    [Fact]
    public void ExplicitBoundsSupportTheFullInstantRangeWithoutDurationArithmetic()
    {
        var minimum = new UtcInstant(DateTimeOffset.MinValue);
        var maximum = new UtcInstant(DateTimeOffset.MaxValue);
        var timeline = new ReplayTimeline(minimum, maximum);

        timeline.AdvanceTo(new UtcInstant(DateTimeOffset.MinValue.AddTicks(1)));
        timeline.AdvanceTo(maximum);

        Assert.Same(minimum, timeline.StartTime);
        Assert.Same(maximum, timeline.EndTime);
        Assert.Same(maximum, timeline.CurrentTime);
    }

    private static HistoricalReplayDataset CreateDataset() => new([CreateTrade(End), CreateTrade(Start)]);

    private static ReplayEvent CreateTrade(UtcInstant time) => ReplayEvent.FromTrade(
        new Trade(Id, time, time, new Price(10.0500m), new Quantity(0.00100m)));
}
