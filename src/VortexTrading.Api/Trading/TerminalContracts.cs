using VortexTrading.Api.Market;
using VortexTrading.Api.Reports;

namespace VortexTrading.Api.Trading;

/// <summary>One reading of the simulated session for the terminal shell.</summary>
public sealed record TerminalSnapshotResponse(
    string? SimulatedTime,
    MarketQuoteResponse[] Quotes,
    PortfolioResponse Portfolio,
    OrderResponse[] Orders,
    PerformanceReportResponse Performance,
    ClosedTradeResponse[] ClosedTrades);
