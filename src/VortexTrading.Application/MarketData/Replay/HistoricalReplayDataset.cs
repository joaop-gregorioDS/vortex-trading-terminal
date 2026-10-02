using VortexTrading.Domain.Common.Time;

namespace VortexTrading.Application.MarketData.Replay;

/// <summary>An immutable snapshot of historical occurrences in canonical replay order.</summary>
public sealed class HistoricalReplayDataset
{
    public IReadOnlyList<ReplayEvent> Events { get; }
    public int Count => Events.Count;
    public UtcInstant StartTime => Events[0].ReplayTime;
    public UtcInstant EndTime => Events[^1].ReplayTime;

    public HistoricalReplayDataset(IReadOnlyCollection<ReplayEvent>? events)
    {
        ArgumentNullException.ThrowIfNull(events);
        var snapshot = events.ToArray();
        if (snapshot.Length == 0 || snapshot.Any(replayEvent => replayEvent is null))
        {
            throw new ArgumentException("Replay datasets require at least one event and no null events.", nameof(events));
        }

        var ordered = snapshot.Select((replayEvent, ordinal) => (Event: replayEvent, Ordinal: ordinal))
            .OrderBy(item => item.Event.ReplayTime)
            .ThenBy(item => item.Event.InstrumentId.Value)
            .ThenBy(item => item.Event.Kind)
            .ThenBy(item => item.Ordinal)
            .Select(item => item.Event)
            .ToArray();

        Events = Array.AsReadOnly(ordered);
    }
}
