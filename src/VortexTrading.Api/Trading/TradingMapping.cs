using System.Globalization;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Portfolio;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Api.Trading;

/// <summary>Raised when a trading request cannot be interpreted; always becomes a 400.</summary>
public sealed class TradingTransportException : Exception
{
    public TradingTransportException(string message) : base(message)
    {
    }
}

/// <summary>Translates between the transport shapes and the domain.</summary>
/// <remarks>
/// Parsing is strict and culture invariant, and it never invents a value: an unparseable decimal, an
/// unknown side, type or time in force, or a limit price on a market order all fail the request. A
/// default would silently trade something other than what the caller asked for.
/// </remarks>
public static class TradingMapping
{
    /// <summary>Builds a domain order from a request, or fails with a transport error.</summary>
    /// <remarks>
    /// The creation instant is supplied by the caller because it must be the simulation's instant, not
    /// the machine clock (ADR-033).
    /// </remarks>
    public static Order ToOrder(OrderRequest? request, InstrumentId sessionInstrument, UtcInstant createdAt)
    {
        ArgumentNullException.ThrowIfNull(createdAt);

        if (request is null || request.InstrumentId == Guid.Empty)
        {
            throw new TradingTransportException("An instrument and order shape are required.");
        }

        var instrumentId = new InstrumentId(request.InstrumentId);
        if (instrumentId != sessionInstrument)
        {
            throw new TradingTransportException("The requested instrument is not traded by this session.");
        }

        var side = request.Side switch
        {
            "buy" => OrderSide.Buy,
            "sell" => OrderSide.Sell,
            _ => throw new TradingTransportException("Side must be 'buy' or 'sell'.")
        };
        var timeInForce = request.TimeInForce switch
        {
            null or "" or "gtc" => TimeInForce.GTC,
            _ => throw new TradingTransportException("Unsupported time in force.")
        };
        var quantity = new Quantity(Decimal(request.Quantity, "quantity"));

        return request.Type switch
        {
            "market" when request.LimitPrice is null or "" => Order.CreateMarket(
                OrderId.From(Guid.NewGuid()), instrumentId, side, quantity, timeInForce, createdAt),
            "limit" => Order.CreateLimit(OrderId.From(Guid.NewGuid()), instrumentId, side, quantity,
                new Price(Decimal(request.LimitPrice, "limit price")), timeInForce, createdAt),
            "market" => throw new TradingTransportException("A market order must not carry a limit price."),
            _ => throw new TradingTransportException("Type must be 'market' or 'limit'.")
        };
    }

    public static OrderResponse ToResponse(Order? order)
    {
        ArgumentNullException.ThrowIfNull(order);

        // The domain exposes CreatedAt through the order itself; the session supplies it at creation,
        // so the transport simply reflects the value the order carries.
        return new OrderResponse(
            order.Id.ToString(),
            order.InstrumentId.ToString(),
            order.Side == OrderSide.Buy ? "buy" : "sell",
            order.Type == OrderType.Market ? "market" : "limit",
            Decimal(order.Quantity.Value),
            order.LimitPrice is null ? null : Decimal(order.LimitPrice.Value),
            order.TimeInForce.ToString().ToLowerInvariant(),
            Status(order.Status),
            Decimal(order.FilledQuantity.Value),
            Decimal(order.RemainingQuantity.Value),
            order.RejectionReason?.ToString(),
            Time(order.CreatedAt),
            Time(order.LastUpdatedAt),
            order.CompletedAt is null ? null : Time(order.CompletedAt),
            order.Fills.Select(fill => new FillResponse(fill.ExecutionId.ToString(),
                Decimal(fill.Quantity.Value), Decimal(fill.Price.Value), Time(fill.ExecutedAt))).ToArray());
    }

    public static PortfolioResponse ToResponse(Portfolio? portfolio, InstrumentId sessionInstrument,
        Price? mark, decimal cash)
    {
        ArgumentNullException.ThrowIfNull(portfolio);

        // Only the session instrument has a price. A position without a mark reports no unrealized
        // result rather than a fabricated one: marking it at zero would show its whole cost as loss.
        var marks = mark is null
            ? new Dictionary<InstrumentId, Price>()
            : new Dictionary<InstrumentId, Price> { [sessionInstrument] = mark };

        var positions = portfolio.Positions.Values
            .OrderBy(position => position.InstrumentId.Value)
            .Select(position => new PositionResponse(
                position.InstrumentId.ToString(),
                Decimal(position.Quantity),
                Decimal(position.TotalCost),
                Decimal(position.AverageCost),
                Decimal(position.RealizedProfitAndLoss),
                Decimal(position.InstrumentId == sessionInstrument && mark is not null
                    ? position.UnrealizedProfitAndLoss(mark)
                    : 0m),
                Decimal(position.InstrumentId == sessionInstrument && mark is not null
                    ? position.TotalProfitAndLoss(mark)
                    : position.RealizedProfitAndLoss),
                position.IsClosed,
                position.UpdatedAt is null ? null : Time(position.UpdatedAt)))
            .ToArray();

        return new PortfolioResponse(
            Decimal(cash),
            Decimal(portfolio.Equity(marks, cash)),
            Decimal(portfolio.TotalCost),
            Decimal(portfolio.RealizedProfitAndLoss),
            Decimal(portfolio.UnrealizedProfitAndLoss(marks)),
            Decimal(portfolio.TotalProfitAndLoss(marks)),
            positions);
    }

    public static MarketResponse ToResponse(InstrumentId instrumentId, Price? lastPrice, UtcInstant? time,
        int restingOrderCount) => new(
        instrumentId.ToString(),
        lastPrice is null ? null : Decimal(lastPrice.Value),
        time is null ? null : Time(time),
        restingOrderCount);

    public static Guid ParseOrderId(string? value) =>
        Guid.TryParse(value, out var parsed) && parsed != Guid.Empty
            ? parsed
            : throw new TradingTransportException("A non-empty order id is required.");

    private static decimal Decimal(string? value, string field) =>
        decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new TradingTransportException($"The {field} must be a decimal number.");

    private static string Decimal(decimal value) =>
        value.ToString("0.############################", CultureInfo.InvariantCulture);

    private static string Time(UtcInstant instant) =>
        instant.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture);

    private static string Status(OrderStatus status) => status switch
    {
        OrderStatus.Created => "created",
        OrderStatus.Accepted => "accepted",
        OrderStatus.PartiallyFilled => "partially_filled",
        OrderStatus.Filled => "filled",
        OrderStatus.Cancelled => "cancelled",
        OrderStatus.Rejected => "rejected",
        _ => throw new TradingTransportException("The order carries an unsupported status.")
    };
}
