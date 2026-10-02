using System.Diagnostics.CodeAnalysis;
using VortexTrading.Domain.Common.Time;

namespace VortexTrading.Application.MarketData.Replay;

/// <summary>A synchronous, consumer-driven execution over one canonical dataset.</summary>
public sealed class ReplaySession
{
    private readonly HistoricalReplayDataset _dataset;
    private readonly ReplayTimeline _timeline;

    /// <summary>Number of historical occurrences already delivered.</summary>
    public int Position { get; private set; }

    public bool IsCompleted => Position == _dataset.Count;
    public UtcInstant CurrentTime => _timeline.CurrentTime;

    public ReplaySession(HistoricalReplayDataset? dataset)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        _dataset = dataset;
        _timeline = new ReplayTimeline(dataset);
    }

    /// <summary>Advances virtual time and progress before returning the original next event.</summary>
    public bool TryMoveNext([NotNullWhen(true)] out ReplayEvent? replayEvent)
    {
        replayEvent = null;
        if (IsCompleted)
        {
            return false;
        }

        var next = _dataset.Events[Position];
        _timeline.AdvanceTo(next.ReplayTime);
        Position++;
        replayEvent = next;
        return true;
    }
}
