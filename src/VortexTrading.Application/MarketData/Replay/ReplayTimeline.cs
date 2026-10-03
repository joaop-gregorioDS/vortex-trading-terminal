using VortexTrading.Domain.Common.Time;

namespace VortexTrading.Application.MarketData.Replay;

/// <summary>A bounded monotonic virtual timeline, advanced explicitly by its owner.</summary>
public sealed class ReplayTimeline
{
    public UtcInstant StartTime { get; }
    public UtcInstant EndTime { get; }
    public UtcInstant CurrentTime { get; private set; }

    public ReplayTimeline(HistoricalReplayDataset? dataset)
        : this(dataset?.StartTime ?? throw new ArgumentNullException(nameof(dataset)), dataset!.EndTime)
    {
    }

    public ReplayTimeline(UtcInstant startTime, UtcInstant endTime)
    {
        ArgumentNullException.ThrowIfNull(startTime);
        ArgumentNullException.ThrowIfNull(endTime);
        if (endTime.CompareTo(startTime) < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(endTime), "Virtual time end must not precede its start.");
        }

        StartTime = startTime;
        EndTime = endTime;
        CurrentTime = startTime;
    }

    public void AdvanceTo(UtcInstant? instant)
    {
        ArgumentNullException.ThrowIfNull(instant);
        if (instant.CompareTo(StartTime) < 0 || instant.CompareTo(EndTime) > 0)
        {
            throw new ArgumentOutOfRangeException(nameof(instant), "Virtual time must remain within the dataset range.");
        }

        var comparison = instant.CompareTo(CurrentTime);
        if (comparison < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(instant), "Virtual time must not move backward.");
        }

        if (comparison > 0)
        {
            CurrentTime = instant;
        }
    }
}
