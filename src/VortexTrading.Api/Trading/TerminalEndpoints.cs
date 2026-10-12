using System.Text.Json;
using VortexTrading.Api.Market;
using VortexTrading.Api.Reports;
using VortexTrading.Infrastructure.Trading.Session;

namespace VortexTrading.Api.Trading;

/// <summary>One HTTP read of the session the shell displays. It does not keep a second account.</summary>
public static class TerminalEndpoints
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8
    };

    public static void MapTerminalEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/api/terminal", (PaperTradingSession session) =>
        {
            session.EnsureRecovered();
            var time = session.CurrentTime;
            var snapshot = new TerminalSnapshotResponse(
                time is null ? null : TradingMapping.Instant(time),
                MarketQuotesEndpoints.BuildQuotes(session),
                TradingEndpoints.BuildPortfolio(session),
                session.Orders.Select(order => TradingMapping.ToResponse(order, session.ProtectionRole(order.Id))).ToArray(),
                ReportEndpoints.BuildPerformance(session),
                ReportEndpoints.BuildTrades(session, null));
            return Results.Json(snapshot, Options);
        });

        app.MapPost("/api/session/reset", (PaperTradingSession session) =>
        {
            session.ResetDemonstration();
            return Results.Json(new SessionResetResponse("ok"), Options);
        });
    }

    private sealed record SessionResetResponse(string Status);
}
