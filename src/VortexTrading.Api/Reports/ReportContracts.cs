namespace VortexTrading.Api.Reports;

public sealed record BestWorstTradeResponse(
    string Ticker,
    string Pnl
);

public sealed record EquityPointResponse(
    string Time,
    string Equity,
    string RealizedPnl
);

public sealed record PerformanceReportResponse(
    string StartingBalance,
    string CurrentBalance,
    string Equity,
    string RealizedPnl,
    string DailyPnl,
    string WinRate,
    int WinsCount,
    int LossesCount,
    int TotalTrades,
    string ProfitFactor,
    string Payoff,
    string MaxDrawdown,
    BestWorstTradeResponse? BestTrade,
    BestWorstTradeResponse? WorstTrade,
    EquityPointResponse[] EquityCurve
);

public sealed record ClosedTradeResponse(
    string Id,
    string Timestamp,
    string Ticker,
    string Market,
    string Type,
    string Quantity,
    string EntryPrice,
    string ExitPrice,
    string Pnl
);
