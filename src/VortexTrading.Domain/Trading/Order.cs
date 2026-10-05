using System.Numerics;
using VortexTrading.Domain.Common;
using VortexTrading.Domain.Common.Time;
using VortexTrading.Domain.Markets;

namespace VortexTrading.Domain.Trading;

public sealed class Order : IEquatable<Order>
{
    private static readonly BigInteger MaximumCoefficient = (BigInteger.One << 96) - BigInteger.One;
    private readonly List<OrderFill> _fills = [];

    public OrderId Id { get; }
    public InstrumentId InstrumentId { get; }
    public OrderSide Side { get; }
    public OrderType Type { get; }
    public Quantity Quantity { get; }
    public Price? LimitPrice { get; }
    public TimeInForce TimeInForce { get; }
    public UtcInstant CreatedAt { get; }
    public OrderStatus Status { get; private set; }
    public UtcInstant LastUpdatedAt { get; private set; }
    public OrderRejectionReason? RejectionReason { get; private set; }
    public IReadOnlyList<OrderFill> Fills { get; }
    public Quantity FilledQuantity => FromQuantityUnits(SumFillUnits());
    public Quantity RemainingQuantity => FromQuantityUnits(ToQuantityUnits(Quantity.Value) - SumFillUnits());
    public UtcInstant? CompletedAt => IsTerminal ? LastUpdatedAt : null;

    private bool IsTerminal => Status is OrderStatus.Filled or OrderStatus.Cancelled or OrderStatus.Rejected;

    private Order(OrderId? id, InstrumentId? instrumentId, OrderSide side, OrderType type,
        Quantity? quantity, Price? limitPrice, TimeInForce timeInForce, UtcInstant? createdAt)
    {
        if (id is null || instrumentId is null || quantity is null || createdAt is null)
        {
            throw new DomainException("Order requires an ID, instrument ID, quantity and creation time.");
        }

        if (!Enum.IsDefined(side) || !Enum.IsDefined(type) || !Enum.IsDefined(timeInForce))
        {
            throw new DomainException("Order requires supported side, type and time in force.");
        }

        if (quantity.Value <= 0)
        {
            throw new DomainException("Order quantity must be positive.");
        }

        if ((type == OrderType.Market && limitPrice is not null)
            || (type == OrderType.Limit && (limitPrice is null || limitPrice.Value <= 0)))
        {
            throw new DomainException("Market orders have no limit price; limit orders require a positive limit price.");
        }

        Id = id;
        InstrumentId = instrumentId;
        Side = side;
        Type = type;
        Quantity = quantity;
        LimitPrice = limitPrice;
        TimeInForce = timeInForce;
        CreatedAt = createdAt;
        LastUpdatedAt = createdAt;
        Status = OrderStatus.Created;
        Fills = _fills.AsReadOnly();
    }

    public static Order CreateMarket(OrderId? id, InstrumentId? instrumentId, OrderSide side,
        Quantity? quantity, TimeInForce timeInForce, UtcInstant? createdAt)
        => new(id, instrumentId, side, OrderType.Market, quantity, null, timeInForce, createdAt);

    public static Order CreateLimit(OrderId? id, InstrumentId? instrumentId, OrderSide side,
        Quantity? quantity, Price? limitPrice, TimeInForce timeInForce, UtcInstant? createdAt)
        => new(id, instrumentId, side, OrderType.Limit, quantity, limitPrice, timeInForce, createdAt);

    public void Accept(UtcInstant? at)
    {
        if (Status != OrderStatus.Created)
        {
            throw new DomainException("Only a created order can be accepted.");
        }

        ValidateApplicationTime(at);
        Status = OrderStatus.Accepted;
        LastUpdatedAt = at!;
    }

    public void Reject(OrderRejectionReason reason, UtcInstant? at)
    {
        if (!Enum.IsDefined(reason))
        {
            throw new DomainException("Order requires a supported rejection reason.");
        }

        if (Status != OrderStatus.Created
            && !(Status == OrderStatus.Accepted && _fills.Count == 0
                && reason == OrderRejectionReason.ExecutionProviderRejected))
        {
            throw new DomainException("Order cannot be rejected in its current state with this reason.");
        }

        ValidateApplicationTime(at);
        RejectionReason = reason;
        Status = OrderStatus.Rejected;
        LastUpdatedAt = at!;
    }

    public void Cancel(UtcInstant? at)
    {
        if (Status is not (OrderStatus.Created or OrderStatus.Accepted or OrderStatus.PartiallyFilled))
        {
            throw new DomainException("Only a created, accepted or partially filled order can be cancelled.");
        }

        ValidateApplicationTime(at);
        Status = OrderStatus.Cancelled;
        LastUpdatedAt = at!;
    }

    public bool ApplyFill(OrderFill? fill, UtcInstant? at)
    {
        if (fill is null || at is null)
        {
            throw new DomainException("Applying an order fill requires a fill and an application time.");
        }

        if (fill.OrderId != Id)
        {
            throw new DomainException("Order fill targets a different order.");
        }

        var previous = _fills.Find(existing => existing.ExecutionId == fill.ExecutionId);
        if (previous is not null)
        {
            if (previous != fill)
            {
                throw new DomainException("Execution ID was already applied with conflicting fill content.");
            }

            return false;
        }

        if (Status is not (OrderStatus.Accepted or OrderStatus.PartiallyFilled))
        {
            throw new DomainException("Only an accepted or partially filled order can receive a new fill.");
        }

        ValidateApplicationTime(at);
        if (LimitPrice is not null
            && ((Side == OrderSide.Buy && fill.Price.Value > LimitPrice.Value)
                || (Side == OrderSide.Sell && fill.Price.Value < LimitPrice.Value)))
        {
            throw new DomainException("Order fill price violates the order limit.");
        }

        var totalUnits = ToQuantityUnits(Quantity.Value);
        var filledUnits = SumFillUnits() + ToQuantityUnits(fill.Quantity.Value);
        if (filledUnits > totalUnits)
        {
            throw new DomainException("Order fill exceeds the remaining quantity.");
        }

        // Decimal addition/subtraction can round without overflowing. Prove both exact results
        // are representable before publishing any fill, lifecycle or timestamp mutation.
        _ = FromQuantityUnits(filledUnits);
        _ = FromQuantityUnits(totalUnits - filledUnits);
        var nextStatus = filledUnits == totalUnits ? OrderStatus.Filled : OrderStatus.PartiallyFilled;

        _fills.Add(fill);
        Status = nextStatus;
        LastUpdatedAt = at;
        return true;
    }

    private void ValidateApplicationTime(UtcInstant? at)
    {
        if (at is null || at.CompareTo(LastUpdatedAt) < 0)
        {
            throw new DomainException("Order application time is required and must not precede the last update.");
        }
    }

    private BigInteger SumFillUnits()
    {
        var sum = BigInteger.Zero;
        foreach (var fill in _fills)
        {
            sum += ToQuantityUnits(fill.Quantity.Value);
        }

        return sum;
    }

    // Nonnegative decimal quantities are represented exactly as integer multiples of 10^-28.
    private static BigInteger ToQuantityUnits(decimal value)
    {
        var bits = decimal.GetBits(value);
        var coefficient = (BigInteger)(uint)bits[0]
            + ((BigInteger)(uint)bits[1] << 32)
            + ((BigInteger)(uint)bits[2] << 64);
        var scale = (bits[3] >> 16) & 0xff;
        return coefficient * BigInteger.Pow(10, 28 - scale);
    }

    private static Quantity FromQuantityUnits(BigInteger units)
    {
        var scale = 28;
        while (scale > 0 && units % 10 == 0)
        {
            units /= 10;
            scale--;
        }

        if (units < 0 || units > MaximumCoefficient)
        {
            throw new DomainException("Order quantity arithmetic must be exactly representable as decimal.");
        }

        var low = unchecked((int)(uint)(units & uint.MaxValue));
        var middle = unchecked((int)(uint)((units >> 32) & uint.MaxValue));
        var high = unchecked((int)(uint)((units >> 64) & uint.MaxValue));
        return new Quantity(new decimal(low, middle, high, false, (byte)scale));
    }

    public bool Equals(Order? other) => other is not null && Id == other.Id;

    public override bool Equals(object? obj) => obj is Order other && Equals(other);

    public override int GetHashCode() => Id.GetHashCode();

    public static bool operator ==(Order? left, Order? right)
        => ReferenceEquals(left, right) || (left is not null && left.Equals(right));

    public static bool operator !=(Order? left, Order? right) => !(left == right);
}
