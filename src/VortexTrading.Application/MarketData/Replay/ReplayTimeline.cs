using VortexTrading.Domain.Common.Time;

namespace VortexTrading.Application.MarketData.Replay;

/// <summary>Session-owned virtual time, advanced explicitly within one dataset range.</summary>
public sealed class ReplayTimeline
{
    public UtcInstant StartTime { get; }
    public UtcInstant EndTime { get; }
    public UtcInstant CurrentTime { get; private set; }

    public ReplayTimeline(HistoricalReplayDataset? dataset)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        StartTime = dataset.StartTime;
        EndTime = dataset.EndTime;
        CurrentTime = StartTime;
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
