using System.Globalization;
using VortexTrading.Application.Trading;
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

/// <summary>Raised when the request names an instrument the session does not trade. Always a 404.</summary>
public sealed class UnknownInstrumentException : Exception
{
    public UnknownInstrumentException() : base("The requested instrument is not traded by this session.")
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
    public static Order ToOrder(OrderRequest? request, Func<InstrumentId, bool> isTraded, UtcInstant createdAt)
    {
        ArgumentNullException.ThrowIfNull(isTraded);
        ArgumentNullException.ThrowIfNull(createdAt);

        if (request is null || request.InstrumentId == Guid.Empty)
        {
            throw new TradingTransportException("An instrument and order shape are required.");
        }

        var instrumentId = new InstrumentId(request.InstrumentId);
        if (!isTraded(instrumentId))
        {
            throw new UnknownInstrumentException();
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
        var quantity = new Quantity(ParseDecimal(request.Quantity, "quantity"));

        return request.Type switch
        {
            "market" when request.LimitPrice is null or "" => Order.CreateMarket(
                OrderId.From(Guid.NewGuid()), instrumentId, side, quantity, timeInForce, createdAt),
            "limit" => Order.CreateLimit(OrderId.From(Guid.NewGuid()), instrumentId, side, quantity,
                new Price(ParseDecimal(request.LimitPrice, "limit price")), timeInForce, createdAt),
            "market" => throw new TradingTransportException("A market order must not carry a limit price."),
            _ => throw new TradingTransportException("Type must be 'market' or 'limit'.")
        };
    }

    public static ProtectionSpec? ParseProtection(ProtectionRequest? request)
    {
        if (request is null)
        {
            return null;
        }

        var unit = request.Unit?.Trim().ToLowerInvariant() switch
        {
            "ticks" => ProtectionUnit.Ticks,
            "points" => ProtectionUnit.Points,
            "percent" => ProtectionUnit.Percent,
            _ => throw new TradingTransportException("Protection unit must be 'ticks', 'points' or 'percent'.")
        };
        var spec = new ProtectionSpec(
            ParseDecimal(request.Gain, "gain"),
            ParseDecimal(request.Loss, "loss"),
            unit,
            string.IsNullOrWhiteSpace(request.Trail) ? null : ParseDecimal(request.Trail, "trail"),
            string.IsNullOrWhiteSpace(request.BreakEven) ? null : ParseDecimal(request.BreakEven, "breakEven"));
        try
        {
            spec.Validate();
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new TradingTransportException("Protection gain, loss, trail and break-even must be positive.");
        }

        return spec;
    }

    public static OrderResponse ToResponse(Order? order, string? role = null)
    {
        ArgumentNullException.ThrowIfNull(order);

        // The domain exposes CreatedAt through the order itself; the session supplies it at creation,
        // so the transport simply reflects the value the order carries.
        return new OrderResponse(
            order.Id.ToString(),
            order.InstrumentId.ToString(),
            order.Side == OrderSide.Buy ? "buy" : "sell",
            TypeName(order.Type),
            order.Origin == OrderOrigin.Chart ? "chart" : "session",
            Decimal(order.Quantity.Value),
            order.LimitPrice is null ? null : Decimal(order.LimitPrice.Value),
            order.TimeInForce.ToString().ToLowerInvariant(),
            Status(order.Status),
            Decimal(order.FilledQuantity.Value),
            Decimal(order.RemainingQuantity.Value),
            order.RejectionReason?.ToString(),
            Instant(order.CreatedAt),
            Instant(order.LastUpdatedAt),
            order.CompletedAt is null ? null : Instant(order.CompletedAt),
            order.Fills.Select(fill => new FillResponse(fill.ExecutionId.ToString(),
                Decimal(fill.Quantity.Value), Decimal(fill.Price.Value), Instant(fill.ExecutedAt))).ToArray(),
            role);
    }

    public static PortfolioResponse ToResponse(Portfolio? portfolio,
        IReadOnlyDictionary<InstrumentId, Price> marks,
        IReadOnlyDictionary<InstrumentId, decimal> pointValues,
        decimal cash)
    {
        ArgumentNullException.ThrowIfNull(portfolio);
        ArgumentNullException.ThrowIfNull(marks);
        ArgumentNullException.ThrowIfNull(pointValues);

        // Realized and unrealized results are money. Average cost stays the traded price. A position
        // without a mark contributes no unrealized result: marking it at zero would show its cost as a loss.
        var realized = 0m;
        var unrealized = 0m;
        var positions = portfolio.Positions.Values
            .OrderBy(position => position.InstrumentId.Value)
            .Select(position =>
            {
                var point = pointValues.TryGetValue(position.InstrumentId, out var value) ? value : 1m;
                var positionRealized = Scale(position.RealizedProfitAndLoss, point);
                realized += positionRealized;
                decimal positionUnrealized = 0m;
                if (marks.TryGetValue(position.InstrumentId, out var mark))
                {
                    positionUnrealized = Scale(position.UnrealizedProfitAndLoss(mark), point);
                    unrealized += positionUnrealized;
                }

                return new PositionResponse(
                    position.InstrumentId.ToString(),
                    Decimal(position.Quantity),
                    Decimal(position.TotalCost),
                    Decimal(position.AverageCost),
                    Decimal(positionRealized),
                    Decimal(positionUnrealized),
                    Decimal(positionRealized + positionUnrealized),
                    position.IsClosed,
                    position.UpdatedAt is null ? null : Instant(position.UpdatedAt));
            })
            .ToArray();

        return new PortfolioResponse(
            Decimal(cash),
            Decimal(cash + realized + unrealized),
            Decimal(portfolio.TotalCost),
            Decimal(realized),
            Decimal(unrealized),
            Decimal(realized + unrealized),
            positions);
    }

    private static decimal Scale(decimal amount, decimal pointValue) =>
        pointValue == 1m ? amount : amount * pointValue;

    public static MarketResponse ToResponse(InstrumentId instrumentId, Price? lastPrice, UtcInstant? time,
        int restingOrderCount) => new(
        instrumentId.ToString(),
        lastPrice is null ? null : Decimal(lastPrice.Value),
        time is null ? null : Instant(time),
        restingOrderCount);

    public static Guid ParseOrderId(string? value) =>
        Guid.TryParse(value, out var parsed) && parsed != Guid.Empty
            ? parsed
            : throw new TradingTransportException("A non-empty order id is required.");

    internal static decimal ParseDecimal(string? value, string field) =>
        decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new TradingTransportException($"The {field} must be a decimal number.");

    /// <summary>Formats an exact value as a decimal string for transport.</summary>
    private static string Decimal(decimal value) =>
        value.ToString("0.############################", CultureInfo.InvariantCulture);

    /// <summary>Formats an exact value for transport, for callers outside this class.</summary>
    public static string Exact(decimal value) => Decimal(value);

    /// <summary>Formats an instant in UTC for transport.</summary>
    public static string Instant(UtcInstant instant) =>
        instant.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture);

    private static string TypeName(OrderType type) => type switch
    {
        OrderType.Market => "market",
        OrderType.Limit => "limit",
        OrderType.Stop => "stop",
        _ => throw new TradingTransportException("The order carries an unsupported type.")
    };

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
