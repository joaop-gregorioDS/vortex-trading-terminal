using VortexTrading.Api.Market;
using VortexTrading.Api.Reports;

namespace VortexTrading.Api.Trading;

/// <summary>One reading of the paper session for the terminal shell.</summary>
public sealed record TerminalSnapshotResponse(
    string? SimulatedTime,
    bool? SessionOpen,
    MarketQuoteResponse[] Quotes,
    PortfolioResponse Portfolio,
    OrderResponse[] Orders,
    PerformanceReportResponse Performance,
    ClosedTradeResponse[] ClosedTrades);
