using System.Diagnostics.CodeAnalysis;
using VortexTrading.Domain.Common.Time;

namespace VortexTrading.Application.MarketData.Replay;

/// <summary>A serial pull session delivering historical occurrences at their causal availability.</summary>
public sealed class CausalReplaySession
{
    private readonly ScheduledReplayEvent[] _schedule;
    private readonly ReplayTimeline _timeline;

    public int Position { get; private set; }
    public bool IsCompleted => Position == _schedule.Length;
    public UtcInstant CurrentTime => _timeline.CurrentTime;
    public UtcInstant EndTime => _timeline.EndTime;

    public CausalReplaySession(HistoricalReplayDataset? dataset)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        _schedule = dataset.Events
            .Select((replayEvent, ordinal) => new ScheduledReplayEvent(
                replayEvent, ReplayEventAvailability.GetAvailabilityTime(replayEvent), ordinal))
            .OrderBy(entry => entry.AvailabilityTime)
            .ThenBy(entry => entry.OriginalOrdinal)
            .ToArray();

        _timeline = new ReplayTimeline(_schedule[0].AvailabilityTime, _schedule[^1].AvailabilityTime);
    }

    public bool TryMoveNext([NotNullWhen(true)] out ReplayEvent? replayEvent)
    {
        replayEvent = null;
        if (IsCompleted) return false;

        var entry = _schedule[Position];
        _timeline.AdvanceTo(entry.AvailabilityTime);
        Position++;
        replayEvent = entry.Event;
        return true;
    }

    private sealed record ScheduledReplayEvent(
        ReplayEvent Event,
        UtcInstant AvailabilityTime,
        int OriginalOrdinal);
}
