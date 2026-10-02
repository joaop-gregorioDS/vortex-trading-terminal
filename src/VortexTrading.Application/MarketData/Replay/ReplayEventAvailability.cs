using VortexTrading.Domain.Common.Time;

namespace VortexTrading.Application.MarketData.Replay;

/// <summary>Derives historical availability of complete payloads separately from occurrence time.</summary>
public static class ReplayEventAvailability
{
    public static UtcInstant GetAvailabilityTime(ReplayEvent? replayEvent)
    {
        ArgumentNullException.ThrowIfNull(replayEvent);
        return replayEvent.Kind switch
        {
            ReplayEventKind.Quote => replayEvent.Quote!.EventTime,
            ReplayEventKind.Trade => replayEvent.Trade!.EventTime,
            ReplayEventKind.Candle => replayEvent.Candle!.CloseTime,
            _ => throw new InvalidOperationException("Unsupported replay event kind.")
        };
    }
}
