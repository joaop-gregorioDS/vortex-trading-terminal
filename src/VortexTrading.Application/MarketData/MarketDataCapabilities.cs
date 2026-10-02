namespace VortexTrading.Application.MarketData;

[Flags]
public enum MarketDataCapabilities
{
    None = 0,
    Quotes = 1,
    Trades = 2,
    Candles = 4
}
