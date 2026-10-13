using VortexTrading.Application.MarketData;

namespace VortexTrading.Infrastructure.Trading.Session;

/// <summary>One synchronized catalog read. <see cref="SessionOpen"/> is the B3 cash session, not each symbol.</summary>
public sealed record MarketRound(bool SessionOpen, IReadOnlyList<InstrumentMarketUpdate> Instruments);

/// <summary>Last quote and the minute bars of one catalog ticker, already scaled onto its tick.</summary>
public sealed record InstrumentMarketUpdate(
    string Ticker,
    decimal Price,
    DateTimeOffset MarketTime,
    IReadOnlyList<SessionBar> MinuteBars);
