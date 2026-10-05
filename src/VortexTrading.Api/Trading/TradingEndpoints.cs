using System.Text.Json;
using VortexTrading.Domain.Common;
using VortexTrading.Domain.Portfolio;
using VortexTrading.Domain.Trading;
using VortexTrading.Infrastructure.Trading.Session;

namespace VortexTrading.Api.Trading;

/// <summary>HTTP surface for the simulated paper trading terminal.</summary>
/// <remarks>
/// Endpoints stay thin: they translate transport, delegate to the session and translate back. All
/// trading rules live in the domain and application layers, and every value that matters travels as a
/// decimal string (ADR-002).
/// <para>
/// Cash is a session constant rather than a stored balance. The domain deliberately does not model a
/// ledger (ADR-038), so the endpoint reports the configured starting cash and lets positions drive the
/// rest of equity; a real balance would need its own decision and persistence.
/// </para>
/// </remarks>
public static class TradingEndpoints
{
    /// <summary>Starting cash reported for the simulated account.</summary>
    public const decimal StartingCash = 100000m;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8
    };

    public static void MapTradingEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/api/trading/market", (PaperTradingSession session) =>
            Results.Json(TradingMapping.ToResponse(PaperTradingSession.InstrumentId, session.LastPrice,
                session.CurrentTime, session.RestingOrderCount), Options));

        app.MapGet("/api/orders", (PaperTradingSession session) => Results.Json(
            session.Coordinator.Orders.Select(TradingMapping.ToResponse).ToArray(), Options));

        app.MapGet("/api/orders/{orderId}", (string orderId, PaperTradingSession session) =>
            Guard(() =>
            {
                var id = OrderId.From(TradingMapping.ParseOrderId(orderId));
                return session.Coordinator.TryGetOrder(id, out var order) && order is not null
                    ? Results.Json(TradingMapping.ToResponse(order), Options)
                    : NotFound();
            }));

        app.MapGet("/api/portfolio", (PaperTradingSession session) => Results.Json(
            TradingMapping.ToResponse(session.Portfolio(), PaperTradingSession.InstrumentId,
                session.LastPrice, StartingCash), Options));

        app.MapPost("/api/orders", async (HttpContext context, PaperTradingSession session) =>
            await GuardAsync(async () =>
            {
                var request = await context.Request.ReadFromJsonAsync<OrderRequest>(Options,
                    context.RequestAborted);
                var order = TradingMapping.ToOrder(request, PaperTradingSession.InstrumentId, session.Now());
                await session.SubmitAsync(order, context.RequestAborted);
                return Results.Json(TradingMapping.ToResponse(order), Options, statusCode: 201);
            }));

        app.MapPost("/api/orders/{orderId}/cancel", async (string orderId, PaperTradingSession session) =>
            await GuardAsync(async () =>
            {
                var id = OrderId.From(TradingMapping.ParseOrderId(orderId));
                await session.CancelAsync(id, CancellationToken.None);
                return session.Coordinator.TryGetOrder(id, out var order) && order is not null
                    ? Results.Json(TradingMapping.ToResponse(order), Options)
                    : NotFound();
            }));
    }

    private static IResult Guard(Func<IResult> operation)
    {
        try
        {
            return operation();
        }
        catch (Exception exception)
        {
            return Failure(exception);
        }
    }

    private static async Task<IResult> GuardAsync(Func<Task<IResult>> operation)
    {
        try
        {
            return await operation().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            return Failure(exception);
        }
    }

    /// <summary>Maps failures to status codes without leaking internals.</summary>
    /// <remarks>
    /// A transport or argument problem is a 400, a domain guard the caller violated is a 422 and a
    /// missing order is a 404. Provider refusals and unknown orders surface as 409, because the request
    /// was well formed but the current state does not allow it. Nothing echoes exception text.
    /// </remarks>
    private static IResult Failure(Exception exception) => exception switch
    {
        TradingTransportException => Problem(400, "invalid_request", "The order request is invalid."),
        ArgumentException or FormatException => Problem(400, "invalid_request", "The order request is invalid."),
        DomainException => Problem(422, "domain_rule_violation", "The order violates a trading rule."),
        InvalidOperationException => Problem(409, "order_state_conflict", "The order is not in a state that allows this operation."),
        _ => Problem(500, "internal_error", "The operation could not be completed.")
    };

    private static IResult NotFound() => Problem(404, "order_not_found", "The order was not found.");

    private static IResult Problem(int status, string code, string title) =>
        Results.Problem(statusCode: status, title: title,
            extensions: new Dictionary<string, object?> { ["code"] = code });
}
