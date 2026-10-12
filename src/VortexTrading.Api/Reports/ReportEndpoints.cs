using System.Globalization;
using System.Text.Json;
using VortexTrading.Api.Trading;
using VortexTrading.Application.Trading;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Trading;
using VortexTrading.Infrastructure.Trading.Session;

namespace VortexTrading.Api.Reports;

public static class ReportEndpoints
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8
    };

    public static void MapReportEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/api/reports/performance", (PaperTradingSession session) =>
            Results.Json(BuildPerformance(session), Options));

        app.MapGet("/api/reports/trades", (string? ticker, PaperTradingSession session) =>
            Results.Json(BuildTrades(session, ticker), Options));
    }

    /// <summary>Day summary from the session's fills. The same object the terminal snapshot embeds.</summary>
    public static PerformanceReportResponse BuildPerformance(PaperTradingSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var summary = Summarize(session);
        return new PerformanceReportResponse(
            Decimal(summary.StartingCash),
            Decimal(summary.CurrentBalance),
            Decimal(summary.Equity),
            Decimal(summary.RealizedProfitAndLoss),
            Decimal(summary.DailyProfitAndLoss),
            Decimal(summary.WinRate),
            summary.Wins,
            summary.Losses,
            summary.TotalTrades,
            summary.ProfitFactor is null ? "" : Decimal(summary.ProfitFactor.Value),
            summary.Payoff is null ? "" : Decimal(summary.Payoff.Value),
            Decimal(summary.MaxDrawdown),
            summary.Best is null ? null : new BestWorstTradeResponse(summary.Best.Ticker, Decimal(summary.Best.ProfitAndLoss)),
            summary.Worst is null ? null : new BestWorstTradeResponse(summary.Worst.Ticker, Decimal(summary.Worst.ProfitAndLoss)),
            summary.EquityCurve.Select(step => new EquityPointResponse(
                Instant(step.Time), Decimal(step.Equity), Decimal(step.RealizedProfitAndLoss))).ToArray());
    }

    /// <summary>Closed trades from the session's fills. A blank ticker or "all" returns every book.</summary>
    public static ClosedTradeResponse[] BuildTrades(PaperTradingSession session, string? ticker)
    {
        ArgumentNullException.ThrowIfNull(session);
        var trades = RoundTripLedger.Close(session.TradeFills());
        if (!string.IsNullOrWhiteSpace(ticker) && !ticker.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            trades = trades.Where(trade => trade.Ticker.Equals(ticker, StringComparison.OrdinalIgnoreCase)).ToArray();
        }

        return trades.Select(trade => new ClosedTradeResponse(
            trade.Id,
            Instant(trade.ExitedAt),
            trade.Ticker,
            "daytrade",
            trade.OpenedSide == OrderSide.Sell ? "sell" : "buy",
            Decimal(trade.Quantity),
            Decimal(trade.EntryPrice),
            Decimal(trade.ExitPrice),
            Decimal(trade.ProfitAndLoss))).ToArray();
    }

    private static PerformanceSummary Summarize(PaperTradingSession session)
    {
        var day = session.CurrentTime is { } now
            ? DateOnly.FromDateTime(now.Value.UtcDateTime)
            : (DateOnly?)null;
        var trades = RoundTripLedger.Close(session.TradeFills());
        return RoundTripLedger.Summarize(trades, TradingEndpoints.StartingCash, session.UnrealizedProfitAndLoss(), day);
    }

    private static string Decimal(decimal value) =>
        value.ToString("0.############################", CultureInfo.InvariantCulture);

    private static string Instant(UtcInstant instant) =>
        instant.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture);
}
