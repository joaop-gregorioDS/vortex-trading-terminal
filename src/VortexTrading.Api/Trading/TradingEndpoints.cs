using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using VortexTrading.Api.Charts;
using VortexTrading.Application.MarketData;
using VortexTrading.Domain.Common;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Portfolio;
using VortexTrading.Domain.Trading;
using VortexTrading.Infrastructure.MarketData.Yahoo;
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

    /// <summary>Most candles the live chart returns.</summary>
    public const int MaximumChartBars = 200;

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
        {
            session.EnsureRecovered();
            return Results.Json(TradingMapping.ToResponse(PaperTradingSession.InstrumentId, session.LastPrice,
                session.CurrentTime, session.RestingOrderCount), Options);
        });

        app.MapGet("/api/status", (PaperTradingSession session) =>
        {
            // Operational facts in one place, so an operator does not have to read the log to answer
            // "is it alive and is it working". Registered in every environment.
            session.EnsureRecovered();
            var orders = session.Orders;
            var fills = orders.SelectMany(order => order.Fills).ToArray();

            return Results.Json(new StatusResponse(
                InstrumentId: PaperTradingSession.InstrumentId.ToString(),
                SimulatedTime: session.CurrentTime is null ? null : TradingMapping.Instant(session.CurrentTime),
                LastPrice: session.LastPrice is null ? null : TradingMapping.Exact(session.LastPrice.Value),
                RestingOrderCount: session.RestingOrderCount,
                OrderCount: orders.Count,
                FilledOrderCount: orders.Count(order => order.Status == OrderStatus.Filled),
                OpenOrderCount: orders.Count(order => order.Status
                    is OrderStatus.Created or OrderStatus.Accepted or OrderStatus.PartiallyFilled),
                RejectedOrderCount: orders.Count(order => order.Status == OrderStatus.Rejected),
                FillCount: fills.Length,
                TradedQuantity: TradingMapping.Exact(fills.Aggregate(0m, (sum, fill) => sum + fill.Quantity.Value)),
                RestoredOrderCount: session.RestoredOrderCount), Options);
        });

        app.MapGet("/api/orders", (PaperTradingSession session) =>
        {
            // Recovery is explicit on every read of owned state. Leaving it implicit made the list
            // endpoint report an empty book after a restart while the portfolio showed positions.
            session.EnsureRecovered();
            return Results.Json(session.Orders.Select(order =>
                TradingMapping.ToResponse(order, session.ProtectionRole(order.Id))).ToArray(), Options);
        });

        app.MapGet("/api/orders/{orderId}", (string orderId, PaperTradingSession session) =>
            Guard(() =>
            {
                session.EnsureRecovered();
                var id = OrderId.From(TradingMapping.ParseOrderId(orderId));
                return session.TryGetOrder(id, out var order) && order is not null
                    ? Results.Json(TradingMapping.ToResponse(order), Options)
                    : NotFound();
            }));

        app.MapGet("/api/chart", (
            PaperTradingSession session,
            string? ticker,
            string? interval,
            int? maxBars) =>
        {
            var requestedTicker = string.IsNullOrWhiteSpace(ticker) ? "PETR4" : ticker.Trim();
            if (!CandleAggregator.TryGetProfile(requestedTicker, out var profile)
                || profile is null)
            {
                return Results.Problem(statusCode: StatusCodes.Status404NotFound,
                    title: "The instrument is not traded by this session.",
                    extensions: new Dictionary<string, object?> { ["code"] = "instrument_not_found" });
            }

            var requestedInterval = string.IsNullOrWhiteSpace(interval) ? "5m" : interval;
            if (!LiveChartSnapshot.TryParseInterval(requestedInterval, out var parsed))
            {
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: "The chart interval is not supported.",
                    extensions: new Dictionary<string, object?> { ["code"] = "invalid_request" });
            }

            var bars = maxBars.HasValue && maxBars.Value > 0 ? maxBars.Value : MaximumChartBars;
            var snapshot = LiveChartSnapshot.Build(session, profile, parsed, bars);
            return snapshot is null
                ? Results.Json(new { status = "empty" }, Options)
                : Results.Json(ChartSnapshotMapping.ToResponse(snapshot), Options);
        });

        app.MapGet("/api/portfolio", (PaperTradingSession session) =>
            Results.Json(BuildPortfolio(session), Options));

        app.MapPost("/api/orders/chart-preview", (HttpContext context, IChartCloseSource closes) =>
            GuardAsync(() => ChartOrders.Preview(context, closes)));

        app.MapPost("/api/orders", async (HttpContext context, PaperTradingSession session) =>
            await GuardAsync(async () =>
            {
                var request = await context.Request.ReadFromJsonAsync<OrderRequest>(Options,
                    context.RequestAborted);
                var origin = request?.Origin?.Trim().ToLowerInvariant();
                if (origin is not (null or "" or "session" or "chart"))
                {
                    throw new TradingTransportException("Origin must be 'session' or 'chart'.");
                }

                var protection = TradingMapping.ParseProtection(request?.Protection);
                if (origin == "chart")
                {
                    var closes = context.RequestServices.GetRequiredService<IChartCloseSource>();
                    return await ChartOrders.Submit(request!, session, closes, protection, context.RequestAborted);
                }

                var order = TradingMapping.ToOrder(request, session.Trades, session.Now());
                await session.SubmitAsync(order, context.RequestAborted);
                if (protection is { } spec)
                {
                    session.RememberProtection(order, spec);
                }

                return Results.Json(TradingMapping.ToResponse(order, session.ProtectionRole(order.Id)), Options,
                    statusCode: 201);
            }));

        app.MapPost("/api/orders/flatten", async (HttpContext context, PaperTradingSession session) =>
            await GuardAsync(async () =>
            {
                var request = await context.Request.ReadFromJsonAsync<InstrumentActionRequest>(Options,
                    context.RequestAborted);
                var instrumentId = RequireInstrument(request?.InstrumentId ?? Guid.Empty);
                var order = session.Flatten(instrumentId);
                return order is null
                    ? Results.Json(new { status = "flat" }, Options)
                    : Results.Json(new { status = "sent", order = TradingMapping.ToResponse(order) }, Options);
            }));

        app.MapPost("/api/orders/reverse", async (HttpContext context, PaperTradingSession session) =>
            await GuardAsync(async () =>
            {
                var request = await context.Request.ReadFromJsonAsync<InstrumentActionRequest>(Options,
                    context.RequestAborted);
                var instrumentId = RequireInstrument(request?.InstrumentId ?? Guid.Empty);
                var order = session.Reverse(instrumentId);
                return order is null
                    ? Results.Json(new { status = "flat" }, Options)
                    : Results.Json(new { status = "sent", order = TradingMapping.ToResponse(order) }, Options);
            }));

        app.MapPost("/api/orders/{orderId}/revise", async (string orderId, HttpContext context, PaperTradingSession session) =>
            await GuardAsync(async () =>
            {
                var request = await context.Request.ReadFromJsonAsync<ReviseOrderRequest>(Options, context.RequestAborted);
                var id = OrderId.From(TradingMapping.ParseOrderId(orderId));
                if (!session.TryGetOrder(id, out var order) || order is null)
                {
                    return NotFound();
                }

                var price = TradingMapping.ParseDecimal(request?.Price, "price");
                var located = await Locate(order.InstrumentId, order.Origin, context);
                session.ReviseResting(order, price, located.Reference, located.Tick, located.Decimals);
                return Results.Json(TradingMapping.ToResponse(order, session.ProtectionRole(order.Id)), Options);
            }));

        app.MapPost("/api/orders/protection", async (HttpContext context, PaperTradingSession session) =>
            await GuardAsync(async () =>
            {
                var request = await context.Request.ReadFromJsonAsync<ProtectionLevelRequest>(Options,
                    context.RequestAborted);
                var instrumentId = RequireInstrument(request?.InstrumentId ?? Guid.Empty);
                var origin = request?.Origin?.Trim().ToLowerInvariant() switch
                {
                    null or "" or "session" => OrderOrigin.Session,
                    "chart" => OrderOrigin.Chart,
                    _ => throw new TradingTransportException("Origin must be 'session' or 'chart'.")
                };
                var price = TradingMapping.ParseDecimal(request?.Price, "price");
                var located = await Locate(instrumentId, origin, context);
                var chartBar = origin == OrderOrigin.Chart ? located.ChartBar : null;
                var order = session.PlaceProtection(instrumentId, origin, price, located.Reference, located.Tick,
                    located.Decimals, chartBar);
                return Results.Json(TradingMapping.ToResponse(order, session.ProtectionRole(order.Id)), Options,
                    statusCode: 201);
            }));

        app.MapPost("/api/orders/{orderId}/cancel", async (string orderId, PaperTradingSession session) =>
            await GuardAsync(async () =>
            {
                var id = OrderId.From(TradingMapping.ParseOrderId(orderId));
                await session.CancelAsync(id, CancellationToken.None);
                return session.TryGetOrder(id, out var order) && order is not null
                    ? Results.Json(TradingMapping.ToResponse(order), Options)
                    : NotFound();
            }));

        app.MapPost("/api/orders/emergency-flat", async (HttpContext context, PaperTradingSession session) =>
            await GuardAsync(async () =>
            {
                session.EnsureRecovered();
                var resting = session.Orders
                    .Where(order => order.Status is OrderStatus.Created or OrderStatus.Accepted or OrderStatus.PartiallyFilled)
                    .ToArray();

                var cancelledCount = 0;
                foreach (var order in resting)
                {
                    try
                    {
                        await session.CancelAsync(order.Id, context.RequestAborted);
                        cancelledCount++;
                    }
                    catch
                    {
                        // One book refusing a cancel must not skip the other instruments.
                    }
                }

                var flattenedCount = session.FlattenAll();

                return Results.Json(new
                {
                    status = "ok",
                    cancelledOrders = cancelledCount,
                    flattenedPositions = flattenedCount
                }, Options);
            }));
    }

    private static InstrumentId RequireInstrument(Guid instrumentId)
    {
        if (instrumentId == Guid.Empty || !CandleAggregator.TryGetProfile(new InstrumentId(instrumentId), out _))
        {
            throw new UnknownInstrumentException();
        }

        return new InstrumentId(instrumentId);
    }

    /// <summary>Reference, tick and chart bar for an order's own tape.</summary>
    private static async Task<(decimal Reference, decimal Tick, int Decimals, UtcInstant? ChartBar)> Locate(
        InstrumentId instrumentId, OrderOrigin origin, HttpContext context)
    {
        if (!CandleAggregator.TryGetProfile(instrumentId, out var profile) || profile is null)
        {
            throw new UnknownInstrumentException();
        }

        if (origin != OrderOrigin.Chart)
        {
            var session = context.RequestServices.GetRequiredService<PaperTradingSession>();
            var last = session.LastPriceFor(profile.InstrumentId)
                ?? throw new InvalidOperationException("The simulated book has no price for this order yet.");
            return (last.Value, profile.TickSize, profile.Decimals, null);
        }

        if (!YahooSeriesCatalog.TryForProfile(profile, out var series))
        {
            throw new TradingTransportException("This instrument has no chart series.");
        }

        var closes = context.RequestServices.GetRequiredService<IChartCloseSource>();
        ChartSeriesClose? close;
        try
        {
            close = await closes.LatestMinuteAsync(profile.Ticker, context.RequestAborted);
        }
        catch (Exception error) when (error is HttpRequestException or YahooChartException
            || (error is TaskCanceledException && !context.RequestAborted.IsCancellationRequested))
        {
            throw new ChartSeriesUnavailableException();
        }

        if (close is null || close.Close <= 0m)
        {
            throw new ChartSeriesUnavailableException();
        }

        return (close.Close, series.TickSize, series.Decimals, close.OpenTime);
    }

    /// <summary>Marked portfolio of the session. Equity uses the same cash and point values as the reports.</summary>
    public static PortfolioResponse BuildPortfolio(PaperTradingSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var marks = new Dictionary<InstrumentId, Price>();
        var points = new Dictionary<InstrumentId, decimal>();
        foreach (var profile in CandleAggregator.Catalog)
        {
            points[profile.InstrumentId] = profile.PointValue;
            var last = session.LastPriceFor(profile.InstrumentId);
            if (last is not null)
            {
                marks[profile.InstrumentId] = last;
            }
        }

        return TradingMapping.ToResponse(session.Portfolio(), marks, points, StartingCash);
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
        UnknownInstrumentException => Problem(404, "instrument_not_found", "The instrument is not traded by this session."),
        ChartSeriesUnavailableException => Problem(503, "chart_series_unavailable", "A série do gráfico não respondeu."),
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
