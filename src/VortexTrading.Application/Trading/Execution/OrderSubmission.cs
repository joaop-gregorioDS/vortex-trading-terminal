using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;
using VortexTrading.Domain.Trading;

namespace VortexTrading.Application.Trading.Execution;

/// <summary>Contains a detached, immutable execution intent using canonical Domain values.</summary>
/// <remarks>
/// Construction validates required components and intent shape only. It does not validate instrument
/// existence, increments, permissions, risk or lifecycle, and stores no mutable Order reference.
/// CreatedAt is the original intent creation time, not the time of provider submission.
/// </remarks>
public sealed class OrderSubmission
{
    public OrderId OrderId { get; }
    public InstrumentId InstrumentId { get; }
    public OrderSide Side { get; }
    public OrderType Type { get; }
    public Quantity Quantity { get; }
    public Price? LimitPrice { get; }
    public TimeInForce TimeInForce { get; }
    public UtcInstant CreatedAt { get; }

    public OrderSubmission(OrderId? orderId, InstrumentId? instrumentId, OrderSide side, OrderType type,
        Quantity? quantity, Price? limitPrice, TimeInForce timeInForce, UtcInstant? createdAt)
    {
        ArgumentNullException.ThrowIfNull(orderId);
        ArgumentNullException.ThrowIfNull(instrumentId);
        ArgumentNullException.ThrowIfNull(quantity);
        ArgumentNullException.ThrowIfNull(createdAt);

        if (!Enum.IsDefined(side))
        {
            throw new ArgumentOutOfRangeException(nameof(side));
        }

        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type));
        }

        if (!Enum.IsDefined(timeInForce))
        {
            throw new ArgumentOutOfRangeException(nameof(timeInForce));
        }

        if (quantity.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Submitted quantity must be positive.");
        }

        if (type == OrderType.Market && limitPrice is not null)
        {
            throw new ArgumentException("Market submission must not contain a limit price.", nameof(limitPrice));
        }

        if (type == OrderType.Limit)
        {
            ArgumentNullException.ThrowIfNull(limitPrice);
            if (limitPrice.Value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(limitPrice), "Limit price must be positive.");
            }
        }

        OrderId = orderId;
        InstrumentId = instrumentId;
        Side = side;
        Type = type;
        Quantity = quantity;
        LimitPrice = limitPrice;
        TimeInForce = timeInForce;
        CreatedAt = createdAt;
    }
}
