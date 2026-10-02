namespace VortexTrading.Application.MarketData.Replay;

/// <summary>Deterministic replay tie order, without financial priority.</summary>
public enum ReplayEventKind
{
    Quote = 0,
    Trade = 1,
    Candle = 2
}
